using System.Text.Json;

namespace SpeechServices.Cli;

internal interface IProgressReporter
{
    void Report(ProgressMessage message);
}

internal sealed class ConsoleProgressReporter(bool writeJson) : IProgressReporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();

    public void Report(ProgressMessage message)
    {
        lock (_gate)
        {
            if (writeJson)
            {
                // Reason: newline-delimited JSON gives the WinForms process runner incremental progress without waiting for process exit.
                Console.Out.WriteLine(JsonSerializer.Serialize(message, JsonOptions));
                Console.Out.Flush();
                return;
            }

            WriteHumanReadable(message);
        }
    }

    private static void WriteHumanReadable(ProgressMessage message)
    {
        switch (message.Type)
        {
            case "segment":
                Console.Out.WriteLine(TranscriptFormatter.FormatSegment(message.Text ?? string.Empty, message.SpeakerId, message.OffsetMs, includeSpeakerLabel: !string.IsNullOrWhiteSpace(message.SpeakerId)));
                break;
            case "partial":
                Console.Error.WriteLine($"{FormatPercent(message.Percent)} {message.Text}");
                break;
            case "error":
                Console.Error.WriteLine($"ERROR: {message.Message}");
                if (!string.IsNullOrWhiteSpace(message.Details))
                {
                    Console.Error.WriteLine(message.Details);
                }

                break;
            default:
                Console.Error.WriteLine($"{FormatPercent(message.Percent)} {message.Message}");
                break;
        }
    }

    private static string FormatPercent(double? percent) =>
        percent is null ? "[..]" : $"[{Math.Clamp(percent.Value, 0, 100):0}%]";
}
