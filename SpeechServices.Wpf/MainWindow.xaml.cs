using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using SpeechServices.Shared;

namespace SpeechServices.Wpf;

public partial class MainWindow : Window
{
    private readonly StringBuilder _transcriptBuilder = new();
    private readonly StringBuilder _processOutputBuilder = new();
    private readonly object _processOutputGate = new();

    private Process? _activeProcess;
    private CancellationTokenSource? _runCancellation;
    private bool _cancelRequested;
    private bool _hasProcessingError;
    private bool _isBusy;

    private static readonly JsonSerializerOptions SettingsJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions ConfigurationJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static readonly RegionOption[] KnownSpeechRegions =
    [
        new("South Africa North", "southafricanorth"),
        new("East Asia", "eastasia"),
        new("Southeast Asia", "southeastasia"),
        new("Australia East", "australiaeast"),
        new("Central India", "centralindia"),
        new("Japan East", "japaneast"),
        new("Japan West", "japanwest"),
        new("Korea Central", "koreacentral"),
        new("Canada Central", "canadacentral"),
        new("Canada East", "canadaeast"),
        new("North Europe", "northeurope"),
        new("West Europe", "westeurope"),
        new("France Central", "francecentral"),
        new("Germany West Central", "germanywestcentral"),
        new("Italy North", "italynorth"),
        new("Norway East", "norwayeast"),
        new("Sweden Central", "swedencentral"),
        new("Switzerland North", "switzerlandnorth"),
        new("Switzerland West", "switzerlandwest"),
        new("UK South", "uksouth"),
        new("UK West", "ukwest"),
        new("UAE North", "uaenorth"),
        new("Brazil South", "brazilsouth"),
        new("Qatar Central", "qatarcentral"),
        new("Central US", "centralus"),
        new("East US", "eastus"),
        new("East US 2", "eastus2"),
        new("North Central US", "northcentralus"),
        new("South Central US", "southcentralus"),
        new("West Central US", "westcentralus"),
        new("West US", "westus"),
        new("West US 2", "westus2"),
        new("West US 3", "westus3"),
    ];

    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpeechServices",
        "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        ConfigureSpeakerCount();
        ConfigureSpeechRegions();
        LoadDefaultValues();
        SetBusy(isBusy: false);
    }

    private void ConfigureSpeakerCount()
    {
        for (var count = 1; count <= 20; count++)
        {
            SpeakerCountComboBox.Items.Add(count.ToString());
        }

        SpeakerCountComboBox.SelectedItem = "1";
    }

    private void ConfigureSpeechRegions() => SpeechRegionComboBox.ItemsSource = KnownSpeechRegions;

    private void LoadDefaultValues()
    {
        var environmentRegion = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION")
            ?? Environment.GetEnvironmentVariable("SPEECH_REGION");
        if (!string.IsNullOrWhiteSpace(environmentRegion))
        {
            SelectSpeechRegion(environmentRegion);
        }
        LanguageTextBox.Text = "en-US";
        CliPathTextBox.Text = FindDefaultCliPath();
        StatusTextBlock.Text = "Ready";
        PartialTextBlock.Text = string.Empty;
        LoadPersistedSettings();

        var defaultConfigPath = FindDefaultConfigurationPath();
        if (defaultConfigPath is not null)
        {
            TryLoadConfiguration(defaultConfigPath, showErrors: false);
        }
    }

    private void BrowseInputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose audio file",
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            InputTextBox.Text = dialog.FileName;
            InputTypeComboBox.SelectedIndex = 1;
        }
    }

    private void BrowseCliButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose SpeechServices.Cli executable",
            Filter = "SpeechServices CLI|SpeechServices.Cli.exe|Executables|*.exe|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            CliPathTextBox.Text = dialog.FileName;
        }
    }

    private void LoadConfigCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose transcription configuration file",
            Filter = "JSON configuration|*.json|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            TryLoadConfiguration(dialog.FileName, showErrors: true);
        }
    }

    private void ConfigCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = !_isBusy;

    private void SaveConfigCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var speechKey = SpeechKeyPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(speechKey)
            && MessageBox.Show(
                this,
                "The configuration file will include the Azure Speech key in plain text. Continue?",
                "Save configuration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save transcription configuration",
            Filter = "JSON configuration|*.json|All files|*.*",
            FileName = TranscriptionConfiguration.DefaultFileName,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var configuration = new TranscriptionConfiguration
            {
                Input = InputTextBox.Text.Trim(),
                InputType = GetSelectedInputType(),
                SpeechKey = string.IsNullOrWhiteSpace(speechKey) ? null : speechKey,
                SpeechRegion = GetSelectedSpeechRegion(),
                Language = LanguageTextBox.Text.Trim(),
                Speakers = GetSpeakerCount(),
            };

            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(configuration, ConfigurationJsonOptions));
            StatusTextBlock.Text = $"Saved config: {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show(this, ex.Message, "Configuration file error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateInputs(out var cliPath))
        {
            return;
        }

        ResetRunState();
        SetBusy(isBusy: true);
        _cancelRequested = false;
        _runCancellation = new CancellationTokenSource();

        using var process = new Process
        {
            StartInfo = BuildProcessStartInfo(cliPath),
            EnableRaisingEvents = true,
        };

        _activeProcess = process;

        try
        {
            AppendProgress("Starting CLI process.");

            if (!process.Start())
            {
                throw new InvalidOperationException("The CLI process could not be started.");
            }

            var outputTask = ReadOutputAsync(process.StandardOutput, _runCancellation.Token);
            var errorTask = ReadErrorAsync(process.StandardError, _runCancellation.Token);

            try
            {
                await process.WaitForExitAsync(_runCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                KillActiveProcess();
            }

            await Task.WhenAll(outputTask, errorTask);

            if (_cancelRequested)
            {
                UpdateStatus("Cancelled", 0);
                AppendProgress("Transcription cancelled.");
            }
            else if (process.ExitCode == 0)
            {
                UpdateStatus("Completed", 100);
                AppendProgress("CLI completed successfully.");
            }
            else
            {
                UpdateStatus($"CLI exited with code {process.ExitCode}", ProgressBar.Value);
                MarkProcessingError($"CLI exited with code {process.ExitCode}.", details: null);
                AppendProgress($"CLI exited with code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Failed", ProgressBar.Value);
            MarkProcessingError(ex.Message, ex.ToString());
            AppendProgress($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Transcription failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _activeProcess = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetBusy(isBusy: false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancelRequested = true;
        _runCancellation?.Cancel();
        KillActiveProcess();
        AppendProgress("Cancel requested.");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_transcriptBuilder.Length == 0)
        {
            MessageBox.Show(this, "There is no transcript text to save yet.", "Save transcript", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save transcript",
            Filter = "Text files|*.txt|All files|*.*",
            FileName = "transcript.txt",
        };

        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, _transcriptBuilder.ToString());
            AppendProgress($"Saved transcript to {dialog.FileName}.");
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        ResetRunState();
        UpdateStatus("Ready", 0);
    }

    private ProcessStartInfo BuildProcessStartInfo(string cliPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            WorkingDirectory = Path.GetDirectoryName(cliPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(InputTextBox.Text.Trim());
        startInfo.ArgumentList.Add("--input-type");
        startInfo.ArgumentList.Add(GetSelectedInputType());
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add(LanguageTextBox.Text.Trim());
        startInfo.ArgumentList.Add("--speakers");
        startInfo.ArgumentList.Add(GetSpeakerCount().ToString());
        startInfo.ArgumentList.Add("--json");

        if (!string.IsNullOrWhiteSpace(SpeechKeyPasswordBox.Password))
        {
            startInfo.Environment["AZURE_SPEECH_KEY"] = SpeechKeyPasswordBox.Password;
        }

        if (!string.IsNullOrWhiteSpace(GetSelectedSpeechRegion()))
        {
            startInfo.Environment["AZURE_SPEECH_REGION"] = GetSelectedSpeechRegion();
        }

        return startInfo;
    }

    private async Task ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                CaptureProcessOutput("stdout", line);
                ProcessCliJsonLine(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation closes the process streams as part of a normal user-initiated stop.
        }
    }

    private async Task ReadErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                CaptureProcessOutput("stderr", line);
                PostToUi(() => AppendProgress(line));
            }
        }
        catch (OperationCanceledException)
        {
            // stderr reading follows the same cancellation lifetime as stdout reading.
        }
    }

    private void ProcessCliJsonLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = GetString(root, "type");
            var message = GetString(root, "message") ?? line;
            var percent = GetDouble(root, "percent");
            var text = GetString(root, "text");
            var speakerId = GetString(root, "speakerId");
            var offsetMs = GetLong(root, "offsetMs");
            var details = GetString(root, "details");

            PostToUi(() =>
            {
                switch (type)
                {
                    case "status":
                        UpdateStatus(message, percent);
                        AppendProgress(message);
                        break;
                    case "partial":
                        UpdateStatus("Transcribing", percent);
                        PartialTextBlock.Text = text ?? string.Empty;
                        break;
                    case "segment":
                        UpdateStatus("Transcribing", percent);
                        AppendTranscriptSegment(text, speakerId, offsetMs);
                        break;
                    case "completed":
                        ReplaceTranscriptIfFinalTextProvided(text);
                        UpdateStatus(message, 100);
                        AppendProgress(message);
                        PartialTextBlock.Text = string.Empty;
                        break;
                    case "error":
                        UpdateStatus("Failed", percent);
                        MarkProcessingError(message, details);
                        AppendProgress($"ERROR: {message}");
                        break;
                    default:
                        AppendProgress(line);
                        break;
                }
            });
        }
        catch (JsonException)
        {
            PostToUi(() => AppendProgress(line));
        }
    }

    private void AppendTranscriptSegment(string? text, string? speakerId, long? offsetMs)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var offset = TimeSpan.FromMilliseconds(offsetMs ?? 0);
        var includeSpeaker = GetSpeakerCount() > 1 && !string.IsNullOrWhiteSpace(speakerId);
        var line = FormatTranscriptLine(text, speakerId, offset, includeSpeaker);

        _transcriptBuilder.AppendLine(line);
        TranscriptTextBox.AppendText(line + Environment.NewLine);
        TranscriptTextBox.ScrollToEnd();
        SaveButton.IsEnabled = true;
    }

    private void ReplaceTranscriptIfFinalTextProvided(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _transcriptBuilder.Clear();
        _transcriptBuilder.Append(text);
        TranscriptTextBox.Text = text;
        SaveButton.IsEnabled = _transcriptBuilder.Length > 0;
    }

    private static string FormatTranscriptLine(string text, string? speakerId, TimeSpan offset, bool includeSpeaker)
    {
        var timestamp = offset.ToString(@"hh\:mm\:ss\.fff");
        return includeSpeaker ? $"[{timestamp}] {speakerId}: {text}" : $"[{timestamp}] {text}";
    }

    private bool TryValidateInputs(out string cliPath)
    {
        cliPath = string.Empty;

        if (string.IsNullOrWhiteSpace(InputTextBox.Text))
        {
            return ShowValidationError("Provide a local audio file or YouTube URL.");
        }

        if (GetSelectedInputType() == "file" && !File.Exists(InputTextBox.Text.Trim()))
        {
            return ShowValidationError("The selected local audio file does not exist.");
        }

        if (string.IsNullOrWhiteSpace(LanguageTextBox.Text))
        {
            return ShowValidationError("Provide a speech language such as en-US.");
        }

        if (!TryResolveCliExecutablePath(CliPathTextBox.Text, out cliPath) || !File.Exists(cliPath))
        {
            return ShowValidationError("The CLI executable was not found. Build the solution or choose SpeechServices.Cli.exe.");
        }

        var hasKey = !string.IsNullOrWhiteSpace(SpeechKeyPasswordBox.Password)
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY"));
        if (!hasKey)
        {
            return ShowValidationError("Provide an Azure Speech key or set AZURE_SPEECH_KEY.");
        }

        var hasRegion = !string.IsNullOrWhiteSpace(GetSelectedSpeechRegion())
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION"));
        if (!hasRegion)
        {
            return ShowValidationError("Provide an Azure Speech region or set AZURE_SPEECH_REGION.");
        }

        return true;
    }

    private static bool TryResolveCliExecutablePath(string rawPath, out string cliPath)
    {
        cliPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(rawPath.Trim());
        if (File.Exists(fullPath) && Path.GetExtension(fullPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            cliPath = fullPath;
            return true;
        }

        if (Path.GetFileName(fullPath).Equals("SpeechServices.Cli.dll", StringComparison.OrdinalIgnoreCase))
        {
            var executablePath = Path.ChangeExtension(fullPath, ".exe");
            if (File.Exists(executablePath))
            {
                cliPath = executablePath;
                return true;
            }
        }

        return false;
    }

    private bool ShowValidationError(string message)
    {
        MessageBox.Show(this, message, "Missing configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private string GetSelectedInputType() =>
        ((InputTypeComboBox.SelectedItem as ComboBoxItem)?.Content as string) switch
        {
            "File" => "file",
            "YouTube" => "youtube",
            _ => "auto",
        };

    private int GetSpeakerCount() =>
        int.TryParse(SpeakerCountComboBox.SelectedItem as string, out var speakerCount) ? speakerCount : 1;

    private void ResetRunState()
    {
        _transcriptBuilder.Clear();
        lock (_processOutputGate)
        {
            _processOutputBuilder.Clear();
        }

        TranscriptTextBox.Clear();
        ProgressListBox.Items.Clear();
        PartialTextBlock.Text = string.Empty;
        ProgressBar.Value = 0;
        SaveButton.IsEnabled = false;
        _hasProcessingError = false;
        CopyErrorOutputMenuItem.IsEnabled = false;
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        StartButton.IsEnabled = !isBusy;
        CancelButton.IsEnabled = isBusy;
        ClearButton.IsEnabled = !isBusy;
        SaveButton.IsEnabled = !isBusy && _transcriptBuilder.Length > 0;
        InputTextBox.IsEnabled = !isBusy;
        BrowseInputButton.IsEnabled = !isBusy;
        InputTypeComboBox.IsEnabled = !isBusy;
        SpeechKeyPasswordBox.IsEnabled = !isBusy;
        SpeechRegionComboBox.IsEnabled = !isBusy;
        LanguageTextBox.IsEnabled = !isBusy;
        SpeakerCountComboBox.IsEnabled = !isBusy;
        CliPathTextBox.IsEnabled = !isBusy;
        BrowseCliButton.IsEnabled = !isBusy;
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateStatus(string message, double? percent)
    {
        StatusTextBlock.Text = message;
        if (percent is not null)
        {
            ProgressBar.Value = Math.Clamp(Math.Round(percent.Value), ProgressBar.Minimum, ProgressBar.Maximum);
        }
    }

    private void AppendProgress(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        ProgressListBox.Items.Add(line);
        ProgressListBox.ScrollIntoView(line);
        lock (_processOutputGate)
        {
            _processOutputBuilder.AppendLine(line);
        }
    }

    private void MarkProcessingError(string message, string? details)
    {
        _hasProcessingError = true;
        lock (_processOutputGate)
        {
            if (!string.IsNullOrWhiteSpace(details))
            {
                _processOutputBuilder.AppendLine("----- Error details -----");
                _processOutputBuilder.AppendLine(details);
            }
            else if (!string.IsNullOrWhiteSpace(message))
            {
                _processOutputBuilder.AppendLine("----- Error summary -----");
                _processOutputBuilder.AppendLine(message);
            }

            CopyErrorOutputMenuItem.IsEnabled = _processOutputBuilder.Length > 0;
        }
    }

    private void CaptureProcessOutput(string streamName, string line)
    {
        lock (_processOutputGate)
        {
            _processOutputBuilder.AppendLine($"{DateTime.Now:HH:mm:ss}  [{streamName}] {line}");
        }
    }

    private void ProgressListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        lock (_processOutputGate)
        {
            CopyErrorOutputMenuItem.IsEnabled = _hasProcessingError && _processOutputBuilder.Length > 0;
        }

        if (!CopyErrorOutputMenuItem.IsEnabled)
        {
            e.Handled = true;
        }
    }

    private void CopyErrorOutputMenuItem_Click(object sender, RoutedEventArgs e)
    {
        string output;
        lock (_processOutputGate)
        {
            if (!_hasProcessingError || _processOutputBuilder.Length == 0)
            {
                return;
            }

            output = _processOutputBuilder.ToString();
        }

        Clipboard.SetText(output);
        AppendProgress("Copied error output to clipboard.");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SavePersistedSettings();
        _runCancellation?.Cancel();
        KillActiveProcess();
    }

    private void LoadPersistedSettings()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return;
            }

            var settings = JsonSerializer.Deserialize<PersistedSettings>(File.ReadAllText(SettingsFilePath));
            if (settings is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.ProtectedSpeechKey))
            {
                SpeechKeyPasswordBox.Password = UnprotectSpeechKey(settings.ProtectedSpeechKey);
            }

            SelectSpeechRegion(settings.SpeechRegion);

            if (!string.IsNullOrWhiteSpace(settings.Language))
            {
                LanguageTextBox.Text = settings.Language;
            }

            if (settings.SpeakerCount is { } speakers)
            {
                SpeakerCountComboBox.SelectedItem = Math.Clamp(speakers, 1, 20).ToString();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            // A corrupted or inaccessible per-user settings file must not prevent the application from starting.
        }
    }

    private void SavePersistedSettings()
    {
        try
        {
            var speechKey = SpeechKeyPasswordBox.Password;
            var settings = new PersistedSettings
            {
                ProtectedSpeechKey = string.IsNullOrWhiteSpace(speechKey) ? null : ProtectSpeechKey(speechKey),
                SpeechRegion = GetSelectedSpeechRegion(),
                Language = LanguageTextBox.Text.Trim(),
                SpeakerCount = GetSpeakerCount(),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath)!);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, SettingsJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Closing the application should still succeed if the per-user settings cannot be written.
        }
    }

    private static string ProtectSpeechKey(string speechKey) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(speechKey), optionalEntropy: null, DataProtectionScope.CurrentUser));

    private static string UnprotectSpeechKey(string protectedSpeechKey) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedSpeechKey), optionalEntropy: null, DataProtectionScope.CurrentUser));

    private void KillActiveProcess()
    {
        try
        {
            if (_activeProcess is not null && !_activeProcess.HasExited)
            {
                _activeProcess.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process can exit between the HasExited check and Kill.
        }
    }

    private void PostToUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            if (Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
            }
        }
        catch (InvalidOperationException)
        {
            // Background stream readers can race with application shutdown.
        }
    }

    private bool TryLoadConfiguration(string path, bool showErrors)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var configuration = TranscriptionConfiguration.Load(fullPath);
            ApplyConfiguration(configuration);
            StatusTextBlock.Text = $"Loaded config: {Path.GetFileName(fullPath)}";
            return true;
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                MessageBox.Show(this, ex.Message, "Configuration file error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return false;
        }
    }

    private void ApplyConfiguration(TranscriptionConfiguration configuration)
    {
        var input = configuration.ResolveInput();
        if (!string.IsNullOrWhiteSpace(input))
        {
            InputTextBox.Text = input;
        }

        if (!string.IsNullOrWhiteSpace(configuration.InputType))
        {
            SelectInputType(NormalizeInputTypeForComboBox(configuration.InputType));
        }
        else if (!string.IsNullOrWhiteSpace(configuration.VideoUrl))
        {
            SelectInputType("YouTube");
        }

        var speechKey = configuration.ResolveSpeechKey();
        if (!string.IsNullOrWhiteSpace(speechKey))
        {
            SpeechKeyPasswordBox.Password = speechKey;
        }

        SelectSpeechRegion(configuration.ResolveSpeechRegion());

        if (!string.IsNullOrWhiteSpace(configuration.Language))
        {
            LanguageTextBox.Text = configuration.Language;
        }

        if (configuration.Speakers is { } speakers)
        {
            SpeakerCountComboBox.SelectedItem = Math.Clamp(speakers, 1, 20).ToString();
        }
    }

    private void SelectInputType(string inputType)
    {
        for (var index = 0; index < InputTypeComboBox.Items.Count; index++)
        {
            if ((InputTypeComboBox.Items[index] as ComboBoxItem)?.Content as string == inputType)
            {
                InputTypeComboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private static string NormalizeInputTypeForComboBox(string inputType) =>
        inputType.Trim().ToLowerInvariant() switch
        {
            "file" => "File",
            "youtube" or "url" => "YouTube",
            _ => "Auto",
        };

    private void SelectSpeechRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return;
        }

        SpeechRegionComboBox.SelectedItem = KnownSpeechRegions.FirstOrDefault(option =>
            option.Identifier.Equals(region.Trim(), StringComparison.OrdinalIgnoreCase)
            || option.Name.Equals(region.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private string GetSelectedSpeechRegion() =>
        (SpeechRegionComboBox.SelectedItem as RegionOption)?.Identifier ?? string.Empty;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static double? GetDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out var value)
            ? value
            : null;

    private static long? GetLong(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out var value)
            ? value
            : null;

    private static string FindDefaultCliPath()
    {
        var copiedCli = Path.Combine(AppContext.BaseDirectory, "cli", "SpeechServices.Cli.exe");
        if (File.Exists(copiedCli))
        {
            return copiedCli;
        }

        var localCli = Path.Combine(AppContext.BaseDirectory, "SpeechServices.Cli.exe");
        if (File.Exists(localCli))
        {
            return localCli;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            var configuration =
#if DEBUG
                "Debug";
#else
                "Release";
#endif

            var candidate = Path.Combine(directory.FullName, "SpeechServices.Cli", "bin", configuration, "net10.0", "SpeechServices.Cli.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return copiedCli;
    }

    private static string? FindDefaultConfigurationPath()
    {
        var directPath = TranscriptionConfiguration.FindDefaultPath(Environment.CurrentDirectory, AppContext.BaseDirectory);
        if (directPath is not null)
        {
            return directPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && directory is not null; depth++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, TranscriptionConfiguration.DefaultFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed class PersistedSettings
    {
        public string? ProtectedSpeechKey { get; init; }

        public string? SpeechRegion { get; init; }

        public string? Language { get; init; }

        public int? SpeakerCount { get; init; }
    }

    private sealed record RegionOption(string Name, string Identifier);
}
