using System.Text;

namespace SpeechServices.Cli;

internal static class TranscriptFormatter
{
    public static string Format(IEnumerable<TranscriptSegment> segments, bool includeSpeakerLabels)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments.OrderBy(segment => segment.Offset))
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            // Reason: timestamps make the plain text searchable while still preserving enough location context to jump back into the source audio.
            builder.AppendLine(FormatSegment(segment.Text, segment.SpeakerId, (long)segment.Offset.TotalMilliseconds, includeSpeakerLabels));
        }

        return builder.ToString();
    }

    public static string FormatSegment(string text, string? speakerId, long? offsetMs, bool includeSpeakerLabel)
    {
        var timestamp = offsetMs is null ? "00:00:00.000" : TimeSpan.FromMilliseconds(offsetMs.Value).ToString(@"hh\:mm\:ss\.fff");

        if (includeSpeakerLabel && !string.IsNullOrWhiteSpace(speakerId))
        {
            return $"[{timestamp}] {speakerId}: {text}";
        }

        return $"[{timestamp}] {text}";
    }
}
