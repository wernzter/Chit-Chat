# Chit-Chat on Windows

Open **Speech engine** in the sidebar and download a model before recording.

- **Parakeet** transcribes after you stop. Choose English or multilingual, or
  [use an existing model folder](../docs/PARAKEET-WINDOWS.md).
- **Live speech** shows provisional text in the main window as you speak. Download the
  English Zipformer model for this mode. Both modes run on this computer.

Hold **Right Ctrl** to record and release to finish, or tap once to start and again to stop.
**Recording** lets you change the shortcut, choose indicator positions, control the two
recording cues, mute audio outputs during capture, and set how long to
watch the inserted text for corrections. **Check my setup** tests the actual shortcut,
microphone level, and insertion into a dedicated test field.

Chit-Chat sends text to the editor that had focus when recording began. When the destination
is no longer available or cannot accept safe insertion, **Copy waiting dictation** keeps
the text available in the notification area. In supported editors, correcting an inserted
word can offer a dictionary entry with a 10-second cancel countdown. You can also edit,
copy, and delete saved transcripts from **History**.

**Text & AI** includes basic local cleanup and optional on-device smart cleanup with a
local Ollama model. **OpenAI** lets you save and test an API key, choose a model, compare
measured speed, and test your prompt. Enabling OpenAI sends the transcript and prompt
to OpenAI. The key is stored in Windows Credential Manager. **Access & data** opens
local files under `%LOCALAPPDATA%\ChitChat` and Windows microphone privacy settings.

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
