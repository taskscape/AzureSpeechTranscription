namespace SpeechServices.Cli;

internal sealed record TranscriptSegment(TimeSpan Offset, TimeSpan Duration, string Text, string? SpeakerId);
