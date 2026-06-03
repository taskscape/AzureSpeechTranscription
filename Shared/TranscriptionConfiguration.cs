using System.Text.Json;

namespace SpeechServices.Shared;

public sealed class TranscriptionConfiguration
{
    public const string DefaultFileName = "transcription.config.json";

    public string? Input { get; set; }

    public string? VideoUrl { get; set; }

    public string? InputType { get; set; }

    public string? Language { get; set; }

    public int? Speakers { get; set; }

    public string? SpeechKey { get; set; }

    public string? AzureKey { get; set; }

    public string? SpeechRegion { get; set; }

    public string? Region { get; set; }

    public static TranscriptionConfiguration Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The transcription configuration file was not found.", fullPath);
        }

        var json = File.ReadAllText(fullPath);
        var configuration = JsonSerializer.Deserialize<TranscriptionConfiguration>(json, JsonOptions);
        return configuration ?? new TranscriptionConfiguration();
    }

    public static string? FindDefaultPath(params string[] searchDirectories)
    {
        // Reason: the WinForms app should discover the same preload file whether it is launched from source, bin, or a published folder.
        foreach (var directory in searchDirectories.Where(directory => !string.IsNullOrWhiteSpace(directory)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(directory, DefaultFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string? ResolveInput() => FirstNonEmpty(Input, VideoUrl);

    public string? ResolveSpeechKey() => FirstNonEmpty(SpeechKey, AzureKey);

    public string? ResolveSpeechRegion() => FirstNonEmpty(SpeechRegion, Region);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Reason: hand-written configuration files should tolerate common casing styles such as speechRegion, SpeechRegion, or speechregion.
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
