# Chit-Chat on Windows

Chit-Chat uses Right Ctrl for push-to-talk by default. Hold to record and release to transcribe.
Transcription runs locally with Parakeet. The app inserts the result into the focused editor
and offers a searchable history. Configure the shortcut, text insertion, history, and model
location in Settings. The dictionary accepts preferred terms and spelling corrections.

Install the [Parakeet model](../docs/PARAKEET-WINDOWS.md) before the first dictation. Chit-Chat
looks under `%LOCALAPPDATA%\ChitChat` for settings, history, the dictionary, and model files.

## Build from source

Use the .NET 10 SDK and run the commands from the `windows` directory.

On Windows, build and test the full solution.

```powershell
dotnet build ChitChat.sln --configuration Release --no-incremental -warnaserror
dotnet test ChitChat.sln --configuration Release --no-build
```

The cross-platform solution filter runs the core, dictionary, and headless UI tests on
macOS or Linux.

```sh
dotnet test ChitChat.CrossPlatform.slnf --configuration Release
```

## Package and verify

Run the following in PowerShell on Windows.

```powershell
dotnet publish src/ChitChat.App/ChitChat.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish
$check = Start-Process artifacts/publish/ChitChat.App.exe -ArgumentList '--selftest' -Wait -PassThru
if ($check.ExitCode -ne 0) { throw "Chit-Chat self-test failed ($($check.ExitCode))" }
```

Keep the Windows platform assembly with the published app if it appears as a separate file.
The self-test checks the published layout. To validate actual dictation, use a Windows
machine with a microphone and try the shortcut in a foreground editor such as Notepad.

## Project structure

- `ChitChat.App` hosts the Avalonia interface and loads the Windows services.
- `ChitChat.Core` handles dictation state and local data.
- `ChitChat.Speech` loads Parakeet using sherpa-onnx.
- `ChitChat.Platform.Windows` supplies microphone capture, the keyboard hook, and text input.
- `ChitChat.Dictionary` uses the [same replacement examples](../shared/dictionary-test-vectors.json)
  as macOS.

Package versions are in `Directory.Packages.props`. Test the packaged executable after changes
to native dependencies or the Windows platform assembly path.
