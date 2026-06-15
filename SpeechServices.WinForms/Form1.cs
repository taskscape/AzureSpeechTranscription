using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SpeechServices.Shared;

namespace SpeechServices.WinForms;

public partial class Form1 : Form
{
    private readonly TextBox _inputTextBox = new();
    private readonly ComboBox _inputTypeComboBox = new();
    private readonly TextBox _speechKeyTextBox = new();
    private readonly TextBox _speechRegionTextBox = new();
    private readonly TextBox _languageTextBox = new();
    private readonly NumericUpDown _speakerCountUpDown = new();
    private readonly TextBox _cliPathTextBox = new();
    private readonly TextBox _configPathTextBox = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _statusLabel = new();
    private readonly Label _partialLabel = new();
    private readonly TextBox _transcriptTextBox = new();
    private readonly ListBox _progressListBox = new();
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _clearButton = new();
    private readonly ContextMenuStrip _errorOutputContextMenu = new();
    private readonly ToolStripMenuItem _copyErrorOutputMenuItem = new("Copy Error Output");
    private readonly StringBuilder _transcriptBuilder = new();
    private readonly StringBuilder _processOutputBuilder = new();
    private readonly object _processOutputGate = new();

    private Process? _activeProcess;
    private CancellationTokenSource? _runCancellation;
    private bool _cancelRequested;
    private bool _hasProcessingError;

    public Form1()
    {
        InitializeComponent();

        // Reason: the template designer is intentionally kept minimal, while the runtime layout documents the process-oriented workflow in code.
        ConfigureErrorOutputContextMenu();
        BuildLayout();
        LoadDefaultValues();
        SetBusy(isBusy: false);
    }

    private void BuildLayout()
    {
        Text = "Azure Speech Transcription";
        MinimumSize = new Size(1040, 760);
        Size = new Size(1180, 840);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(CreateInputGroup(), 0, 0);
        root.Controls.Add(CreateAzureGroup(), 0, 1);
        root.Controls.Add(CreateCliGroup(), 0, 2);
        root.Controls.Add(CreateActionGroup(), 0, 3);
        root.Controls.Add(CreateOutputSplit(), 0, 4);
    }

    private GroupBox CreateInputGroup()
    {
        var group = new GroupBox
        {
            Text = "Input",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
        };

        var grid = CreateGrid(columns: 4, rows: 2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _inputTextBox.Dock = DockStyle.Fill;
        _inputTextBox.PlaceholderText = "Local audio file path or YouTube URL";

        var browseButton = new Button
        {
            Text = "Browse...",
            Dock = DockStyle.Fill,
        };
        browseButton.Click += BrowseInputButton_Click;

        _inputTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _inputTypeComboBox.Dock = DockStyle.Fill;
        _inputTypeComboBox.Items.AddRange(["Auto", "File", "YouTube"]);
        _inputTypeComboBox.SelectedIndex = 0;

        grid.Controls.Add(CreateLabel("Source"), 0, 0);
        grid.Controls.Add(_inputTextBox, 1, 0);
        grid.Controls.Add(browseButton, 2, 0);
        grid.Controls.Add(_inputTypeComboBox, 3, 0);

        var helpLabel = CreateHelpLabel("The CLI accepts WAV, MP3, M4A, and other Windows Media Foundation-decodable files. YouTube URLs are downloaded as audio first.");
        grid.Controls.Add(helpLabel, 1, 1);
        grid.SetColumnSpan(helpLabel, 3);

        group.Controls.Add(grid);
        return group;
    }

    private GroupBox CreateAzureGroup()
    {
        var group = new GroupBox
        {
            Text = "Azure Speech Settings",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
        };

        var grid = CreateGrid(columns: 8, rows: 2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));

        _speechKeyTextBox.Dock = DockStyle.Fill;
        _speechKeyTextBox.UseSystemPasswordChar = true;
        _speechKeyTextBox.PlaceholderText = "Leave blank to use AZURE_SPEECH_KEY";

        _speechRegionTextBox.Dock = DockStyle.Fill;
        _speechRegionTextBox.PlaceholderText = "eastus";

        _languageTextBox.Dock = DockStyle.Fill;
        _languageTextBox.PlaceholderText = "en-US";

        _speakerCountUpDown.Dock = DockStyle.Fill;
        _speakerCountUpDown.Minimum = 1;
        _speakerCountUpDown.Maximum = 20;
        _speakerCountUpDown.Value = 1;

        grid.Controls.Add(CreateLabel("Key"), 0, 0);
        grid.Controls.Add(_speechKeyTextBox, 1, 0);
        grid.Controls.Add(CreateLabel("Region"), 2, 0);
        grid.Controls.Add(_speechRegionTextBox, 3, 0);
        grid.Controls.Add(CreateLabel("Language"), 4, 0);
        grid.Controls.Add(_languageTextBox, 5, 0);
        grid.Controls.Add(CreateLabel("Speakers"), 6, 0);
        grid.Controls.Add(_speakerCountUpDown, 7, 0);

        var helpLabel = CreateHelpLabel("Speaker count above 1 enables Azure conversation transcription so returned segments can include speaker labels.");
        grid.Controls.Add(helpLabel, 1, 1);
        grid.SetColumnSpan(helpLabel, 7);

        group.Controls.Add(grid);
        return group;
    }

    private GroupBox CreateCliGroup()
    {
        var group = new GroupBox
        {
            Text = "CLI",
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
        };

        var grid = CreateGrid(columns: 3, rows: 2);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        _cliPathTextBox.Dock = DockStyle.Fill;

        var browseButton = new Button
        {
            Text = "Browse...",
            Dock = DockStyle.Fill,
        };
        browseButton.Click += BrowseCliButton_Click;

        _configPathTextBox.Dock = DockStyle.Fill;
        _configPathTextBox.PlaceholderText = TranscriptionConfiguration.DefaultFileName;

        var browseConfigButton = new Button
        {
            Text = "Browse...",
            Dock = DockStyle.Fill,
        };
        browseConfigButton.Click += BrowseConfigButton_Click;

        grid.Controls.Add(CreateLabel("CLI path"), 0, 0);
        grid.Controls.Add(_cliPathTextBox, 1, 0);
        grid.Controls.Add(browseButton, 2, 0);
        grid.Controls.Add(CreateLabel("Config"), 0, 1);
        grid.Controls.Add(_configPathTextBox, 1, 1);
        grid.Controls.Add(browseConfigButton, 2, 1);

        group.Controls.Add(grid);
        return group;
    }

    private Control CreateActionGroup()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 6,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));

        _startButton.Text = "Start";
        _startButton.Dock = DockStyle.Fill;
        _startButton.Click += StartButton_Click;

        _cancelButton.Text = "Cancel";
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Click += CancelButton_Click;

        _saveButton.Text = "Save Text...";
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Click += SaveButton_Click;

        _clearButton.Text = "Clear";
        _clearButton.Dock = DockStyle.Fill;
        _clearButton.Click += ClearButton_Click;

        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        _partialLabel.Dock = DockStyle.Fill;
        _partialLabel.AutoEllipsis = true;
        _partialLabel.ForeColor = SystemColors.GrayText;

        panel.Controls.Add(_startButton, 0, 0);
        panel.Controls.Add(_cancelButton, 1, 0);
        panel.Controls.Add(_saveButton, 2, 0);
        panel.Controls.Add(_clearButton, 3, 0);
        panel.Controls.Add(_progressBar, 4, 0);
        panel.Controls.Add(_statusLabel, 5, 0);
        panel.Controls.Add(_partialLabel, 0, 1);
        panel.SetColumnSpan(_partialLabel, 6);

        return panel;
    }

    private Control CreateOutputSplit()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 440,
        };

        _transcriptTextBox.Dock = DockStyle.Fill;
        _transcriptTextBox.Multiline = true;
        _transcriptTextBox.ReadOnly = true;
        _transcriptTextBox.ScrollBars = ScrollBars.Both;
        _transcriptTextBox.WordWrap = true;
        _transcriptTextBox.Font = new Font(FontFamily.GenericMonospace, 10);

        _progressListBox.Dock = DockStyle.Fill;
        _progressListBox.HorizontalScrollbar = true;
        // Reason: error diagnostics are displayed in the progress log, so the popup menu is attached where users naturally inspect failures.
        _progressListBox.ContextMenuStrip = _errorOutputContextMenu;

        split.Panel1.Controls.Add(_transcriptTextBox);
        split.Panel2.Controls.Add(_progressListBox);

        return split;
    }

    private static TableLayoutPanel CreateGrid(int columns, int rows)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = columns,
            RowCount = rows,
            AutoSize = true,
        };

        for (var row = 0; row < rows; row++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return grid;
    }

    private static Label CreateLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(3, 6, 3, 6),
        };

    private static Label CreateHelpLabel(string text) =>
        new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 2, 3, 6),
        };

    private void ConfigureErrorOutputContextMenu()
    {
        // Reason: copying failure output is only useful after a failed run, so the menu item is dynamically enabled by the Opening handler.
        _copyErrorOutputMenuItem.Click += CopyErrorOutputMenuItem_Click;
        _errorOutputContextMenu.Items.Add(_copyErrorOutputMenuItem);
        _errorOutputContextMenu.Opening += ErrorOutputContextMenu_Opening;
    }

    private void LoadDefaultValues()
    {
        // Reason: users can keep credentials in environment variables and avoid typing secrets into the GUI on every run.
        _speechRegionTextBox.Text = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? Environment.GetEnvironmentVariable("SPEECH_REGION") ?? string.Empty;
        _languageTextBox.Text = "en-US";
        _cliPathTextBox.Text = FindDefaultCliPath();
        _statusLabel.Text = "Ready";
        _partialLabel.Text = string.Empty;

        var defaultConfigPath = FindDefaultConfigurationPath();
        if (defaultConfigPath is not null)
        {
            TryLoadConfiguration(defaultConfigPath, showErrors: false);
        }
    }

    private void BrowseInputButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose audio file",
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.aac;*.wma;*.flac|All files|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _inputTextBox.Text = dialog.FileName;
            _inputTypeComboBox.SelectedItem = "File";
        }
    }

    private void BrowseCliButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose SpeechServices.Cli executable",
            Filter = "SpeechServices CLI|SpeechServices.Cli.exe|Executables|*.exe|All files|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _cliPathTextBox.Text = dialog.FileName;
        }
    }

    private void BrowseConfigButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose transcription configuration file",
            Filter = "JSON configuration|*.json|All files|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            TryLoadConfiguration(dialog.FileName, showErrors: true);
        }
    }

    private async void StartButton_Click(object? sender, EventArgs e)
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
                UpdateStatus($"CLI exited with code {process.ExitCode}", _progressBar.Value);
                MarkProcessingError($"CLI exited with code {process.ExitCode}.", details: null);
                AppendProgress($"CLI exited with code {process.ExitCode}.");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus("Failed", _progressBar.Value);
            MarkProcessingError(ex.Message, ex.ToString());
            AppendProgress($"ERROR: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Transcription failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _activeProcess = null;
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetBusy(isBusy: false);
        }
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        _cancelRequested = true;
        _runCancellation?.Cancel();
        KillActiveProcess();
        AppendProgress("Cancel requested.");
    }

    private void SaveButton_Click(object? sender, EventArgs e)
    {
        if (_transcriptBuilder.Length == 0)
        {
            MessageBox.Show(this, "There is no transcript text to save yet.", "Save transcript", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save transcript",
            Filter = "Text files|*.txt|All files|*.*",
            FileName = "transcript.txt",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            // Reason: saving happens in the GUI as well as the CLI so the user can decide after previewing the transcript.
            File.WriteAllText(dialog.FileName, _transcriptBuilder.ToString());
            AppendProgress($"Saved transcript to {dialog.FileName}.");
        }
    }

    private void ClearButton_Click(object? sender, EventArgs e)
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

        // Reason: configuration is a WinForms preload feature only; the CLI receives the current GUI values as ordinary invocation parameters.
        startInfo.ArgumentList.Add("--input");
        startInfo.ArgumentList.Add(_inputTextBox.Text.Trim());
        startInfo.ArgumentList.Add("--input-type");
        startInfo.ArgumentList.Add(GetSelectedInputType());
        startInfo.ArgumentList.Add("--language");
        startInfo.ArgumentList.Add(_languageTextBox.Text.Trim());
        startInfo.ArgumentList.Add("--speakers");
        startInfo.ArgumentList.Add(((int)_speakerCountUpDown.Value).ToString());
        startInfo.ArgumentList.Add("--json");

        // Reason: passing secrets via environment avoids exposing the Speech key in command-line process listings.
        if (!string.IsNullOrWhiteSpace(_speechKeyTextBox.Text))
        {
            startInfo.Environment["AZURE_SPEECH_KEY"] = _speechKeyTextBox.Text;
        }

        if (!string.IsNullOrWhiteSpace(_speechRegionTextBox.Text))
        {
            startInfo.Environment["AZURE_SPEECH_REGION"] = _speechRegionTextBox.Text;
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
            // Reason: cancellation closes the process streams as part of normal user-initiated stop behavior.
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
            // Reason: stderr reading is tied to the same process cancellation token as stdout reading.
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
                        _partialLabel.Text = text ?? string.Empty;
                        break;
                    case "segment":
                        UpdateStatus("Transcribing", percent);
                        AppendTranscriptSegment(text, speakerId, offsetMs);
                        break;
                    case "completed":
                        ReplaceTranscriptIfFinalTextProvided(text);
                        UpdateStatus(message, 100);
                        AppendProgress(message);
                        _partialLabel.Text = string.Empty;
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
        var includeSpeaker = _speakerCountUpDown.Value > 1 && !string.IsNullOrWhiteSpace(speakerId);
        var line = FormatTranscriptLine(text, speakerId, offset, includeSpeaker);

        _transcriptBuilder.AppendLine(line);
        _transcriptTextBox.AppendText(line + Environment.NewLine);
        _saveButton.Enabled = true;
    }

    private void ReplaceTranscriptIfFinalTextProvided(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Reason: the final event is authoritative and protects the GUI from rare missed segment events during fast process shutdown.
        _transcriptBuilder.Clear();
        _transcriptBuilder.Append(text);
        _transcriptTextBox.Text = text;
        _saveButton.Enabled = _transcriptBuilder.Length > 0;
    }

    private static string FormatTranscriptLine(string text, string? speakerId, TimeSpan offset, bool includeSpeaker)
    {
        var timestamp = offset.ToString(@"hh\:mm\:ss\.fff");
        return includeSpeaker ? $"[{timestamp}] {speakerId}: {text}" : $"[{timestamp}] {text}";
    }

    private bool TryValidateInputs(out string cliPath)
    {
        cliPath = string.Empty;

        if (string.IsNullOrWhiteSpace(_inputTextBox.Text))
        {
            return ShowValidationError("Provide a local audio file or YouTube URL.");
        }

        if (GetSelectedInputType() == "file" && !File.Exists(_inputTextBox.Text.Trim()))
        {
            return ShowValidationError("The selected local audio file does not exist.");
        }

        if (string.IsNullOrWhiteSpace(_languageTextBox.Text))
        {
            return ShowValidationError("Provide a speech language such as en-US.");
        }

        if (!TryResolveCliExecutablePath(_cliPathTextBox.Text, out cliPath))
        {
            return ShowValidationError("The CLI executable was not found. Build the solution or choose SpeechServices.Cli.exe.");
        }

        if (!File.Exists(cliPath))
        {
            return ShowValidationError("The CLI executable was not found. Build the solution or choose SpeechServices.Cli.exe.");
        }

        var hasKey = !string.IsNullOrWhiteSpace(_speechKeyTextBox.Text) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY"));
        if (!hasKey)
        {
            return ShowValidationError("Provide an Azure Speech key or set AZURE_SPEECH_KEY.");
        }

        var hasRegion = !string.IsNullOrWhiteSpace(_speechRegionTextBox.Text) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION"));
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
        MessageBox.Show(this, message, "Missing configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private string GetSelectedInputType() =>
        (_inputTypeComboBox.SelectedItem as string) switch
        {
            "File" => "file",
            "YouTube" => "youtube",
            _ => "auto",
        };

    private void ResetRunState()
    {
        _transcriptBuilder.Clear();
        lock (_processOutputGate)
        {
            _processOutputBuilder.Clear();
        }

        _transcriptTextBox.Clear();
        _progressListBox.Items.Clear();
        _partialLabel.Text = string.Empty;
        _progressBar.Value = 0;
        _saveButton.Enabled = false;
        _hasProcessingError = false;
        _copyErrorOutputMenuItem.Enabled = false;
    }

    private void SetBusy(bool isBusy)
    {
        _startButton.Enabled = !isBusy;
        _cancelButton.Enabled = isBusy;
        _clearButton.Enabled = !isBusy;
        _saveButton.Enabled = _transcriptBuilder.Length > 0;
        _inputTextBox.Enabled = !isBusy;
        _inputTypeComboBox.Enabled = !isBusy;
        _speechKeyTextBox.Enabled = !isBusy;
        _speechRegionTextBox.Enabled = !isBusy;
        _languageTextBox.Enabled = !isBusy;
        _speakerCountUpDown.Enabled = !isBusy;
        _cliPathTextBox.Enabled = !isBusy;
        _configPathTextBox.Enabled = !isBusy;
    }

    private void UpdateStatus(string message, double? percent)
    {
        _statusLabel.Text = message;
        if (percent is not null)
        {
            _progressBar.Value = (int)Math.Clamp(Math.Round(percent.Value), _progressBar.Minimum, _progressBar.Maximum);
        }
    }

    private void AppendProgress(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        _progressListBox.Items.Add(line);
        _progressListBox.TopIndex = Math.Max(0, _progressListBox.Items.Count - 1);
        // Reason: the popup copy action should reproduce the same operational output the user saw when diagnosing the failed run.
        lock (_processOutputGate)
        {
            _processOutputBuilder.AppendLine(line);
        }
    }

    private void MarkProcessingError(string message, string? details)
    {
        _hasProcessingError = true;

        // Reason: detailed exception text is intentionally kept out of the visible log unless copied, keeping the UI readable while preserving diagnostics.
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

            _copyErrorOutputMenuItem.Enabled = _processOutputBuilder.Length > 0;
        }
    }

    private void CaptureProcessOutput(string streamName, string line)
    {
        // Reason: raw stdout/stderr is useful when the CLI fails before a message can be converted into a friendly progress entry.
        lock (_processOutputGate)
        {
            _processOutputBuilder.AppendLine($"{DateTime.Now:HH:mm:ss}  [{streamName}] {line}");
        }
    }

    private void ErrorOutputContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        lock (_processOutputGate)
        {
            _copyErrorOutputMenuItem.Enabled = _hasProcessingError && _processOutputBuilder.Length > 0;
        }

        e.Cancel = !_copyErrorOutputMenuItem.Enabled;
    }

    private void CopyErrorOutputMenuItem_Click(object? sender, EventArgs e)
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
            // Reason: the process may exit between the HasExited check and Kill call.
        }
    }

    private void PostToUi(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
        catch (InvalidOperationException)
        {
            // Reason: background stream readers can race with form disposal during application shutdown.
        }
    }

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

    private bool TryLoadConfiguration(string path, bool showErrors)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var configuration = TranscriptionConfiguration.Load(fullPath);
            _configPathTextBox.Text = fullPath;
            ApplyConfiguration(configuration);
            _statusLabel.Text = $"Loaded config: {Path.GetFileName(fullPath)}";
            return true;
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                MessageBox.Show(this, ex.Message, "Configuration file error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }
    }

    private void ApplyConfiguration(TranscriptionConfiguration configuration)
    {
        // Reason: loading the config into the form makes the GUI preview exactly what it will send to the background CLI.
        var input = configuration.ResolveInput();
        if (!string.IsNullOrWhiteSpace(input))
        {
            _inputTextBox.Text = input;
        }

        if (!string.IsNullOrWhiteSpace(configuration.InputType))
        {
            _inputTypeComboBox.SelectedItem = NormalizeInputTypeForComboBox(configuration.InputType);
        }
        else if (!string.IsNullOrWhiteSpace(configuration.VideoUrl))
        {
            _inputTypeComboBox.SelectedItem = "YouTube";
        }

        var speechKey = configuration.ResolveSpeechKey();
        if (!string.IsNullOrWhiteSpace(speechKey))
        {
            _speechKeyTextBox.Text = speechKey;
        }

        var speechRegion = configuration.ResolveSpeechRegion();
        if (!string.IsNullOrWhiteSpace(speechRegion))
        {
            _speechRegionTextBox.Text = speechRegion;
        }

        if (!string.IsNullOrWhiteSpace(configuration.Language))
        {
            _languageTextBox.Text = configuration.Language;
        }

        if (configuration.Speakers is { } speakers)
        {
            _speakerCountUpDown.Value = Math.Clamp(speakers, (int)_speakerCountUpDown.Minimum, (int)_speakerCountUpDown.Maximum);
        }
    }

    private static string NormalizeInputTypeForComboBox(string inputType) =>
        inputType.Trim().ToLowerInvariant() switch
        {
            "file" => "File",
            "youtube" or "url" => "YouTube",
            _ => "Auto",
        };

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

        // Reason: during source-tree debugging, the GUI may run before the copy target has executed, so walk upward to the sibling CLI build output.
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

        // Reason: during Debug runs the executable is under bin, while developers usually keep transcription.config.json at the solution root.
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
}
