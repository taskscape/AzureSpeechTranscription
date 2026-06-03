using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Transcription;

namespace SpeechServices.Cli;

internal sealed class SpeechTranscriptionService
{
    public Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        PreparedAudio audio,
        CliOptions options,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        // Reason: ConversationTranscriber provides speaker identifiers; SpeechRecognizer is simpler and faster for single-speaker files.
        return options.Speakers > 1
            ? TranscribeConversationAsync(audio, options, reporter, cancellationToken)
            : TranscribeSingleSpeakerAsync(audio, options, reporter, cancellationToken);
    }

    private static async Task<IReadOnlyList<TranscriptSegment>> TranscribeSingleSpeakerAsync(
        PreparedAudio audio,
        CliOptions options,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var speechConfig = CreateSpeechConfig(options, enableSpeakerLabels: false);
        using var audioConfig = AudioConfig.FromWavFileInput(audio.WavPath);
        using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);

        var segments = new List<TranscriptSegment>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;

        recognizer.SessionStarted += (_, _) =>
            reporter.Report(ProgressMessage.Status("transcribe", "Azure Speech recognition session started.", 25));

        recognizer.Recognizing += (_, args) =>
        {
            if (args.Result.Reason == ResultReason.RecognizingSpeech && !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                reporter.Report(ProgressMessage.Partial(
                    "transcribe",
                    args.Result.Text,
                    EstimateTranscriptionPercent(args.Result, audio.Duration),
                    speakerId: null,
                    Offset(args.Result),
                    args.Result.Duration));
            }
        };

        recognizer.Recognized += (_, args) =>
        {
            if (args.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                var segment = new TranscriptSegment(Offset(args.Result), args.Result.Duration, args.Result.Text, SpeakerId: null);
                segments.Add(segment);
                reporter.Report(ProgressMessage.Segment(
                    "transcribe",
                    segment.Text,
                    EstimateTranscriptionPercent(args.Result, audio.Duration),
                    segment.SpeakerId,
                    segment.Offset,
                    segment.Duration));
            }
            else if (args.Result.Reason == ResultReason.NoMatch)
            {
                reporter.Report(ProgressMessage.Status("transcribe", "Azure Speech could not recognize one audio segment.", EstimateTranscriptionPercent(args.Result, audio.Duration)));
            }
        };

        recognizer.Canceled += (_, args) =>
        {
            if (args.Reason == CancellationReason.Error)
            {
                failure = new InvalidOperationException($"Azure Speech recognition canceled: {args.ErrorCode}. {args.ErrorDetails}");
            }

            completion.TrySetResult();
        };

        recognizer.SessionStopped += (_, _) =>
        {
            reporter.Report(ProgressMessage.Status("transcribe", "Azure Speech recognition session stopped.", 99));
            completion.TrySetResult();
        };

        await RunRecognitionAsync(
            start: () => recognizer.StartContinuousRecognitionAsync(),
            stop: () => recognizer.StopContinuousRecognitionAsync(),
            completion,
            cancellationToken).ConfigureAwait(false);

        if (failure is not null)
        {
            throw failure;
        }

        return segments;
    }

    private static async Task<IReadOnlyList<TranscriptSegment>> TranscribeConversationAsync(
        PreparedAudio audio,
        CliOptions options,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var speechConfig = CreateSpeechConfig(options, enableSpeakerLabels: true);
        using var audioConfig = AudioConfig.FromWavFileInput(audio.WavPath);
        using var transcriber = new ConversationTranscriber(speechConfig, audioConfig);

        var segments = new List<TranscriptSegment>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;

        transcriber.SessionStarted += (_, _) =>
            reporter.Report(ProgressMessage.Status("transcribe", "Azure conversation transcription session started.", 25));

        transcriber.Transcribing += (_, args) =>
        {
            if (args.Result.Reason == ResultReason.RecognizingSpeech && !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                reporter.Report(ProgressMessage.Partial(
                    "transcribe",
                    args.Result.Text,
                    EstimateTranscriptionPercent(args.Result, audio.Duration),
                    NormalizeSpeakerId(args.Result.SpeakerId),
                    Offset(args.Result),
                    args.Result.Duration));
            }
        };

        transcriber.Transcribed += (_, args) =>
        {
            if (args.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(args.Result.Text))
            {
                var segment = new TranscriptSegment(
                    Offset(args.Result),
                    args.Result.Duration,
                    args.Result.Text,
                    NormalizeSpeakerId(args.Result.SpeakerId));

                segments.Add(segment);
                reporter.Report(ProgressMessage.Segment(
                    "transcribe",
                    segment.Text,
                    EstimateTranscriptionPercent(args.Result, audio.Duration),
                    segment.SpeakerId,
                    segment.Offset,
                    segment.Duration));
            }
            else if (args.Result.Reason == ResultReason.NoMatch)
            {
                reporter.Report(ProgressMessage.Status("transcribe", "Azure Speech could not transcribe one conversation segment.", EstimateTranscriptionPercent(args.Result, audio.Duration)));
            }
        };

        transcriber.Canceled += (_, args) =>
        {
            if (args.Reason == CancellationReason.Error)
            {
                failure = new InvalidOperationException($"Azure conversation transcription canceled: {args.ErrorCode}. {args.ErrorDetails}");
            }

            completion.TrySetResult();
        };

        transcriber.SessionStopped += (_, _) =>
        {
            reporter.Report(ProgressMessage.Status("transcribe", "Azure conversation transcription session stopped.", 99));
            completion.TrySetResult();
        };

        await RunRecognitionAsync(
            start: () => transcriber.StartTranscribingAsync(),
            stop: () => transcriber.StopTranscribingAsync(),
            completion,
            cancellationToken).ConfigureAwait(false);

        if (failure is not null)
        {
            throw failure;
        }

        return segments;
    }

    private static SpeechConfig CreateSpeechConfig(CliOptions options, bool enableSpeakerLabels)
    {
        var speechConfig = SpeechConfig.FromSubscription(options.SpeechKey, options.SpeechRegion);
        speechConfig.SpeechRecognitionLanguage = options.Language;
        speechConfig.OutputFormat = OutputFormat.Detailed;

        if (enableSpeakerLabels)
        {
            // Reason: the SDK assigns speaker identifiers automatically; the GUI speaker count is used to choose this diarization-capable path.
            speechConfig.SetProperty(PropertyId.SpeechServiceResponse_DiarizeIntermediateResults, "true");
        }

        return speechConfig;
    }

    private static async Task RunRecognitionAsync(
        Func<Task> start,
        Func<Task> stop,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        await start().ConfigureAwait(false);

        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            await stop().ConfigureAwait(false);
        }
    }

    private static TimeSpan Offset(RecognitionResult result) => TimeSpan.FromTicks(result.OffsetInTicks);

    private static double EstimateTranscriptionPercent(RecognitionResult result, TimeSpan totalDuration)
    {
        if (totalDuration <= TimeSpan.Zero)
        {
            return 25;
        }

        var recognizedThrough = TimeSpan.FromTicks(result.OffsetInTicks + result.Duration.Ticks);
        var audioRatio = recognizedThrough.TotalMilliseconds / totalDuration.TotalMilliseconds;
        return Math.Clamp(25 + audioRatio * 74, 25, 99);
    }

    private static string? NormalizeSpeakerId(string? speakerId)
    {
        if (string.IsNullOrWhiteSpace(speakerId) || string.Equals(speakerId, "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return speakerId.StartsWith("Speaker", StringComparison.OrdinalIgnoreCase) ? speakerId : $"Speaker {speakerId}";
    }
}
