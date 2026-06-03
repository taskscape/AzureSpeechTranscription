namespace SpeechServices.Cli;

internal sealed class PreparedAudio(
    string wavPath,
    TimeSpan duration,
    IReadOnlyCollection<string> temporaryFiles,
    bool keepTemporaryFiles)
{
    public string WavPath { get; } = wavPath;

    public TimeSpan Duration { get; } = duration;

    public void Cleanup()
    {
        if (keepTemporaryFiles)
        {
            return;
        }

        foreach (var file in temporaryFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Reason: cleanup must never hide the transcription result or the real failure reported earlier.
            }
        }
    }
}
