# SpeechServices

Two .NET 8 applications work together:

- `SpeechServices.Cli`: downloads or reads audio, normalizes it to WAV, and transcribes it with Azure AI Speech.
- `SpeechServices.WinForms`: desktop GUI that launches the CLI in the background, streams progress, previews transcript text, and saves the text file.

## Requirements

- Windows with .NET 8 SDK/runtime.
- Azure AI Speech resource key and region.
- Internet access for Azure Speech and YouTube URL input.
- Local audio must be decodable by Windows Media Foundation. WAV, MP3, and M4A are the safest choices.

## Build

```powershell
dotnet build .\SpeechServices.sln
```

The WinForms build copies the CLI output into:

```text
SpeechServices.WinForms\bin\Debug\net8.0-windows\cli\
```

## Required Configuration

| Setting | CLI argument | Environment variable | GUI field | Example |
| --- | --- | --- | --- | --- |
| Azure Speech key | `--speech-key` | `AZURE_SPEECH_KEY` | Key | `xxxxxxxx` |
| Azure Speech region | `--speech-region` | `AZURE_SPEECH_REGION` | Region | `eastus` |
| Input source | `--input` | none | Source | `sample.mp3` or YouTube URL |
| Input type | `--input-type` | none | Input type | `auto`, `file`, `youtube` |
| Speech language | `--language` | none | Language | `en-US`, `pl-PL` |
| Speaker count | `--speakers` | none | Speakers | `1`, `2` |

The WinForms application can preload GUI fields from a JSON file. The CLI does not read this file; when the GUI starts transcription, it passes the current GUI values to the CLI as normal command-line arguments and environment variables. The default filename is `transcription.config.json`; a sample is provided in [transcription.config.sample.json](C:/Projects/SpeechServices/transcription.config.sample.json).

```json
{
  "videoUrl": "https://www.youtube.com/watch?v=VIDEO_ID",
  "inputType": "youtube",
  "speechRegion": "eastus",
  "speechKey": "<your-azure-speech-key>",
  "language": "en-US",
  "speakers": 1
}
```

Supported aliases:

- `input` or `videoUrl` for the source.
- `speechKey` or `azureKey` for the Azure Speech key.
- `speechRegion` or `region` for the Azure Speech region.

When speaker count is greater than `1`, the CLI uses Azure `ConversationTranscriber` so transcript segments can include speaker labels. Azure assigns speaker identifiers automatically; the value does not force an exact number of speakers.

## CLI Usage

Set credentials:

```powershell
$env:AZURE_SPEECH_KEY = "<your-speech-key>"
$env:AZURE_SPEECH_REGION = "eastus"
```

Transcribe a local file:

```powershell
dotnet run --project .\SpeechServices.Cli -- --input .\sample.mp3 --language en-US --output .\transcript.txt
```

Transcribe a YouTube URL with JSON progress:

```powershell
.\SpeechServices.Cli\bin\Debug\net8.0\SpeechServices.Cli.exe `
  --input "https://www.youtube.com/watch?v=VIDEO_ID" `
  --input-type youtube `
  --language en-US `
  --speakers 2 `
  --json
```

Useful CLI options:

```text
--output <path>      Save final transcript from the CLI.
--work-dir <path>    Directory for downloaded and converted audio.
--keep-temp          Keep temporary audio files for troubleshooting.
--json               Emit newline-delimited JSON events for GUI/process integration.
```

## GUI Usage

1. Run `SpeechServices.WinForms`.
2. Provide a local audio file or YouTube URL, or browse to a JSON configuration file.
3. Enter Azure Speech key and region, set `AZURE_SPEECH_KEY` and `AZURE_SPEECH_REGION`, or load them from the configuration file.
4. Set language and speaker count.
5. Click `Start`.
6. Review progress and transcript preview.
7. Click `Save Text...` to save the previewed transcript.

If processing fails, right-click the progress log and choose `Copy Error Output` to copy the run output and diagnostic details to the clipboard.

The GUI passes credentials to the CLI through process environment variables so the Speech key is not placed on the command line.

## Visual Studio Designer

The WinForms project sets `ApplicationHighDpiMode` for runtime and `ForceDesignerDPIUnaware` for Visual Studio 2022 17.8+ designer tabs. Visual Studio reads the designer setting when the project is loaded, so unload/reload the WinForms project or restart Visual Studio after changing it.

## Progress Protocol

With `--json`, the CLI writes newline-delimited JSON to stdout. Event `type` values are:

- `status`: stage progress such as download, conversion, and recognition session state.
- `partial`: intermediate recognition text.
- `segment`: final transcript segment with optional `speakerId`, `offsetMs`, and `durationMs`.
- `completed`: final transcript text and optional output path.
- `error`: failure message and details.

## Notes

- YouTube input uses `YoutubeExplode` to download the best audio-only stream, preferring MP4/M4A because Windows Media Foundation usually decodes it reliably.
- Audio conversion uses `NAudio` to create 16 kHz, 16-bit, mono PCM WAV before calling the Azure Speech SDK.
- Azure Speech usage may incur costs in the Azure subscription that owns the Speech resource.
- Product and SDK documentation: [Azure AI Speech](https://azure.microsoft.com/en-us/products/ai-foundry/tools/speech), [Azure Speech SDK samples](https://github.com/Azure-Samples/cognitive-services-speech-sdk), [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode), [NAudio](https://github.com/naudio/NAudio).
