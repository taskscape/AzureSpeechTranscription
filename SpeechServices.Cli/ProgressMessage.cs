using System.Text.Json.Serialization;

namespace SpeechServices.Cli;

internal sealed record ProgressMessage(
    string Type,
    string Phase,
    string Message,
    double? Percent = null,
    string? Text = null,
    string? SpeakerId = null,
    long? OffsetMs = null,
    long? DurationMs = null,
    string? OutputPath = null,
    string? Details = null)
{
    public static ProgressMessage Status(string phase, string message, double? percent = null) =>
        new("status", phase, message, percent);

    public static ProgressMessage Partial(string phase, string text, double? percent, string? speakerId, TimeSpan offset, TimeSpan duration) =>
        new("partial", phase, "Intermediate recognition result.", percent, text, speakerId, ToMilliseconds(offset), ToMilliseconds(duration));

    public static ProgressMessage Segment(string phase, string text, double? percent, string? speakerId, TimeSpan offset, TimeSpan duration) =>
        new("segment", phase, "Final transcript segment.", percent, text, speakerId, ToMilliseconds(offset), ToMilliseconds(duration));

    public static ProgressMessage Completed(string phase, string message, string? outputPath, string text) =>
        new("completed", phase, message, 100, text, OutputPath: outputPath);

    public static ProgressMessage Error(string phase, string message, string details) =>
        new("error", phase, message, Details: details);

    private static long ToMilliseconds(TimeSpan value) => (long)Math.Round(value.TotalMilliseconds);
}

[JsonSerializable(typeof(ProgressMessage))]
internal partial class ProgressMessageJsonContext : JsonSerializerContext;
