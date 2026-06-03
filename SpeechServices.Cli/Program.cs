namespace SpeechServices.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var parseResult = CliOptionsParser.Parse(args);
        if (parseResult.ShowHelp)
        {
            Console.WriteLine(CliOptionsParser.HelpText);
            return 0;
        }

        if (parseResult.Error is not null)
        {
            Console.Error.WriteLine(parseResult.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliOptionsParser.HelpText);
            return 1;
        }

        var options = parseResult.Options!;
        var reporter = new ConsoleProgressReporter(options.Json);
        PreparedAudio? preparedAudio = null;

        try
        {
            // Reason: the CLI is the durable automation boundary, so each stage emits progress that the WinForms app can stream without linking to Azure SDK assemblies.
            reporter.Report(ProgressMessage.Status("startup", "Starting transcription request.", 0));

            var audioPreparation = new AudioPreparationService();
            preparedAudio = await audioPreparation.PrepareAsync(options, reporter, CancellationToken.None).ConfigureAwait(false);

            var transcription = new SpeechTranscriptionService();
            var segments = await transcription.TranscribeAsync(preparedAudio, options, reporter, CancellationToken.None).ConfigureAwait(false);
            var transcriptText = TranscriptFormatter.Format(segments, includeSpeakerLabels: options.Speakers > 1);

            if (!string.IsNullOrWhiteSpace(options.OutputPath))
            {
                // Reason: the CLI can be used independently of the GUI, so it owns an optional output file path as a first-class scenario.
                var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                await File.WriteAllTextAsync(options.OutputPath, transcriptText).ConfigureAwait(false);
            }

            reporter.Report(ProgressMessage.Completed("completed", "Transcription completed.", options.OutputPath, transcriptText));
            return 0;
        }
        catch (Exception ex)
        {
            reporter.Report(ProgressMessage.Error("failed", ex.Message, ex.ToString()));
            return 2;
        }
        finally
        {
            // Reason: downloaded and converted audio can be large, so temporary files are removed unless the caller explicitly asks to inspect them.
            preparedAudio?.Cleanup();
        }
    }
}
