using NAudio.Wave;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace SpeechServices.Cli;

internal sealed class AudioPreparationService
{
    private const int SpeechSampleRate = 16000;
    private const int SpeechBitsPerSample = 16;
    private const int SpeechChannels = 1;

    public async Task<PreparedAudio> PrepareAsync(CliOptions options, IProgressReporter reporter, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.WorkingDirectory);

        var temporaryFiles = new List<string>();
        var inputKind = ResolveInputKind(options);
        var sourceAudioPath = inputKind switch
        {
            InputKind.YouTube => await DownloadYouTubeAudioAsync(options, reporter, temporaryFiles, cancellationToken).ConfigureAwait(false),
            InputKind.File => ResolveLocalFile(options.Input),
            _ => throw new InvalidOperationException("Unable to resolve input type."),
        };

        var wavPath = Path.Combine(options.WorkingDirectory, $"speech-input-{Guid.NewGuid():N}.wav");
        temporaryFiles.Add(wavPath);

        // Reason: Azure Speech SDK file recognition works reliably with WAV input, so every source is normalized before transcription.
        await Task.Run(() => ConvertToSpeechWav(sourceAudioPath, wavPath, inputKind, reporter, cancellationToken), cancellationToken).ConfigureAwait(false);

        var duration = GetWavDuration(wavPath);
        reporter.Report(ProgressMessage.Status("audio", $"Prepared {duration:g} of audio for transcription.", inputKind == InputKind.YouTube ? 35 : 25));

        return new PreparedAudio(wavPath, duration, temporaryFiles, options.KeepTemp);
    }

    private static InputKind ResolveInputKind(CliOptions options)
    {
        if (options.InputKind is InputKind.File or InputKind.YouTube)
        {
            return options.InputKind;
        }

        return IsYouTubeUrl(options.Input) ? InputKind.YouTube : InputKind.File;
    }

    private static string ResolveLocalFile(string input)
    {
        var fullPath = Path.GetFullPath(input);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The input audio file was not found.", fullPath);
        }

        return fullPath;
    }

    private static async Task<string> DownloadYouTubeAudioAsync(
        CliOptions options,
        IProgressReporter reporter,
        List<string> temporaryFiles,
        CancellationToken cancellationToken)
    {
        reporter.Report(ProgressMessage.Status("download", "Reading YouTube metadata.", 1));

        var youtube = new YoutubeClient();
        var video = await youtube.Videos.GetAsync(options.Input, cancellationToken).ConfigureAwait(false);
        var manifest = await youtube.Videos.Streams.GetManifestAsync(options.Input, cancellationToken).ConfigureAwait(false);

        // Reason: Media Foundation on Windows commonly decodes MP4/M4A, so prefer that stream before falling back to any audio-only stream.
        var audioStreams = manifest.GetAudioOnlyStreams().ToArray();
        var streamInfo = audioStreams
            .Where(stream => stream.Container == Container.Mp4)
            .TryGetWithHighestBitrate() ?? audioStreams.TryGetWithHighestBitrate();

        if (streamInfo is null)
        {
            throw new InvalidOperationException("No downloadable audio-only stream was found for the YouTube URL.");
        }

        var downloadPath = Path.Combine(options.WorkingDirectory, $"youtube-audio-{Guid.NewGuid():N}.{streamInfo.Container.Name}");
        temporaryFiles.Add(downloadPath);

        reporter.Report(ProgressMessage.Status("download", $"Downloading audio for '{video.Title}'.", 2));

        var progress = new Progress<double>(value =>
        {
            var percent = ScalePercent(value, start: 2, range: 18);
            reporter.Report(ProgressMessage.Status("download", $"Downloading YouTube audio ({streamInfo.Container.Name}, {streamInfo.Bitrate}).", percent));
        });

        await youtube.Videos.Streams.DownloadAsync(streamInfo, downloadPath, progress, cancellationToken).ConfigureAwait(false);
        reporter.Report(ProgressMessage.Status("download", "YouTube audio download completed.", 20));

        return downloadPath;
    }

    private static void ConvertToSpeechWav(
        string sourceAudioPath,
        string wavPath,
        InputKind inputKind,
        IProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var startPercent = inputKind == InputKind.YouTube ? 20 : 0;
        var range = inputKind == InputKind.YouTube ? 15 : 25;

        reporter.Report(ProgressMessage.Status("convert", "Converting audio to 16 kHz mono PCM WAV.", startPercent));

        try
        {
            using var reader = new MediaFoundationReader(sourceAudioPath);
            var outputFormat = new WaveFormat(SpeechSampleRate, SpeechBitsPerSample, SpeechChannels);
            using var resampler = new MediaFoundationResampler(reader, outputFormat)
            {
                ResamplerQuality = 60,
            };
            using var writer = new WaveFileWriter(wavPath, outputFormat);

            var buffer = new byte[outputFormat.AverageBytesPerSecond * 4];
            var lastReported = DateTimeOffset.MinValue;
            int bytesRead;

            while ((bytesRead = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, bytesRead);

                if (DateTimeOffset.UtcNow - lastReported > TimeSpan.FromMilliseconds(500))
                {
                    lastReported = DateTimeOffset.UtcNow;
                    var ratio = reader.Length <= 0 ? 0 : reader.Position / (double)reader.Length;
                    reporter.Report(ProgressMessage.Status("convert", "Converting audio to Speech SDK WAV format.", ScalePercent(ratio, startPercent, range)));
                }
            }
        }
        catch (Exception ex) when (inputKind == InputKind.YouTube)
        {
            throw new InvalidOperationException(
                "Downloaded YouTube audio could not be decoded by Windows Media Foundation. Try a local MP3/WAV/M4A file, or use a YouTube video with an MP4/M4A audio stream.",
                ex);
        }
    }

    private static TimeSpan GetWavDuration(string wavPath)
    {
        using var waveReader = new WaveFileReader(wavPath);
        return waveReader.TotalTime;
    }

    private static bool IsYouTubeUrl(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host == "youtu.be" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host == "youtube.com";
    }

    private static double ScalePercent(double ratio, double start, double range) =>
        Math.Clamp(start + Math.Clamp(ratio, 0, 1) * range, 0, 100);
}
