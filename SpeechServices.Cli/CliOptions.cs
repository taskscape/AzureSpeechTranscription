namespace SpeechServices.Cli;

internal enum InputKind
{
    Auto,
    File,
    YouTube,
}

internal sealed record CliOptions(
    string Input,
    InputKind InputKind,
    string Language,
    int Speakers,
    string SpeechKey,
    string SpeechRegion,
    string? OutputPath,
    string WorkingDirectory,
    bool KeepTemp,
    bool Json);

internal sealed record CliParseResult(CliOptions? Options, bool ShowHelp, string? Error);

internal static class CliOptionsParser
{
    public const string HelpText = """
        Azure Speech transcription CLI

        Required:
          --input <file-or-youtube-url>      Local audio path or YouTube URL.
          --speech-key <key>                 Azure AI Speech resource key, or AZURE_SPEECH_KEY.
          --speech-region <region>           Azure AI Speech resource region, or AZURE_SPEECH_REGION.

        Optional:
          --input-type <auto|file|youtube>   Defaults to auto.
          --language <locale>                Speech locale such as en-US or pl-PL. Defaults to en-US.
          --speakers <count>                 Use 2 or more to enable conversation transcription with speaker labels.
          --output <path>                    Save final transcript text to a file.
          --work-dir <path>                  Directory for downloaded/converted audio. Defaults to a temp directory.
          --keep-temp                        Keep downloaded and converted audio for troubleshooting.
          --json                             Emit newline-delimited JSON events for GUI integration.
          --help                             Show this help.

        Examples:
          SpeechServices.Cli --input sample.mp3 --language en-US --output transcript.txt
          SpeechServices.Cli --input https://www.youtube.com/watch?v=VIDEO_ID --speakers 2 --json
        """;

    public static CliParseResult Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var switches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                return new CliParseResult(null, ShowHelp: false, Error: $"Unexpected argument '{argument}'.");
            }

            var name = argument[2..];
            string? inlineValue = null;
            var equalsIndex = name.IndexOf('=', StringComparison.Ordinal);
            if (equalsIndex >= 0)
            {
                inlineValue = name[(equalsIndex + 1)..];
                name = name[..equalsIndex];
            }

            if (IsSwitch(name))
            {
                switches.Add(name);
                continue;
            }

            if (!IsValueOption(name))
            {
                // Reason: rejecting unknown options prevents misspelled parameters from being silently ignored.
                return new CliParseResult(null, ShowHelp: false, Error: $"Unknown option --{name}.");
            }

            var value = inlineValue;
            if (value is null)
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return new CliParseResult(null, ShowHelp: false, Error: $"Missing value for --{name}.");
                }

                value = args[++index];
            }

            values[name] = value;
        }

        if (switches.Contains("help") || switches.Contains("h"))
        {
            return new CliParseResult(null, ShowHelp: true, Error: null);
        }

        // Reason: URL and file aliases make the CLI pleasant to use while still converging to one validated Input option internally.
        var input = GetValue(values, "input") ?? GetValue(values, "file") ?? GetValue(values, "url");
        if (string.IsNullOrWhiteSpace(input))
        {
            return new CliParseResult(null, ShowHelp: false, Error: "Provide --input, --file, or --url.");
        }

        var inputKind = ParseInputKind(GetValue(values, "input-type"));
        if (inputKind is null)
        {
            return new CliParseResult(null, ShowHelp: false, Error: "--input-type must be auto, file, or youtube.");
        }

        if (values.ContainsKey("file"))
        {
            inputKind = InputKind.File;
        }
        else if (values.ContainsKey("url"))
        {
            inputKind = InputKind.YouTube;
        }

        var language = GetValue(values, "language") ?? "en-US";
        if (string.IsNullOrWhiteSpace(language))
        {
            return new CliParseResult(null, ShowHelp: false, Error: "--language cannot be empty.");
        }

        if (!int.TryParse(GetValue(values, "speakers") ?? "1", out var speakers) || speakers < 1)
        {
            return new CliParseResult(null, ShowHelp: false, Error: "--speakers must be a positive integer.");
        }

        // Reason: credentials are accepted from arguments for GUI handoff and from environment variables for normal CLI automation.
        var speechKey =
            GetValue(values, "speech-key") ??
            GetValue(values, "azure-key") ??
            GetValue(values, "key") ??
            Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY") ??
            Environment.GetEnvironmentVariable("SPEECH_KEY");

        if (string.IsNullOrWhiteSpace(speechKey))
        {
            return new CliParseResult(null, ShowHelp: false, Error: "Provide --speech-key or set AZURE_SPEECH_KEY.");
        }

        var speechRegion =
            GetValue(values, "speech-region") ??
            GetValue(values, "azure-region") ??
            GetValue(values, "region") ??
            Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ??
            Environment.GetEnvironmentVariable("SPEECH_REGION");

        if (string.IsNullOrWhiteSpace(speechRegion))
        {
            return new CliParseResult(null, ShowHelp: false, Error: "Provide --speech-region or set AZURE_SPEECH_REGION.");
        }

        var workingDirectory = GetValue(values, "work-dir") ?? Path.Combine(Path.GetTempPath(), "SpeechServices", Guid.NewGuid().ToString("N"));

        var options = new CliOptions(
            Input: input,
            InputKind: inputKind.Value,
            Language: language,
            Speakers: speakers,
            SpeechKey: speechKey,
            SpeechRegion: speechRegion,
            OutputPath: GetValue(values, "output"),
            WorkingDirectory: workingDirectory,
            KeepTemp: switches.Contains("keep-temp"),
            Json: switches.Contains("json"));

        return new CliParseResult(options, ShowHelp: false, Error: null);
    }

    private static bool IsSwitch(string name) =>
        string.Equals(name, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "h", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "json", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "keep-temp", StringComparison.OrdinalIgnoreCase);

    private static bool IsValueOption(string name) =>
        string.Equals(name, "input", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "file", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "url", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "input-type", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "language", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "speakers", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "speech-key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "azure-key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "key", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "speech-region", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "azure-region", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "region", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "output", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "work-dir", StringComparison.OrdinalIgnoreCase);

    private static string? GetValue(Dictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) ? value : null;

    private static InputKind? ParseInputKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return InputKind.Auto;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => InputKind.Auto,
            "file" => InputKind.File,
            "youtube" or "url" => InputKind.YouTube,
            _ => null,
        };
    }
}
