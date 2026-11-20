# Chit-Chat

On-device dictation for macOS 26 and Windows. Record using a custom keyboard shortcut or the app's
Record button. Chit-Chat transcribes with the selected speech engine, cleans up the text,
optionally enhances it through OpenAI, applies your dictionary, and inserts the result into the focused text field.

See [Chit-Chat on Windows](windows/README.md) for the Windows setup and settings. The
instructions below describe the macOS build.

## Run locally

Requires a Swift 6.2 or newer toolchain and macOS 26.

```sh
make install
```

This builds, signs, and launches `/Applications/Chit-Chat.app`. Build products stay in
`~/Library/Caches/ChitChatBuild` to avoid file-provider interference during compilation
and signing. Use `make app` to build without installing, and `make test` to run the tests.

Grant **Accessibility** in System Settings → Privacy & Security so the global shortcut
works. Allow **Microphone** access when prompted on the first recording.

The build uses a stable local signing certificate, or an available Developer ID certificate.
It stops if neither is available so a rebuild cannot silently invalidate permissions. See
[local signing](docs/LOCAL-SIGNING.md) for setup and the one-time permission renewal.

## Recording

The default shortcut is **Control + Shift**, using either side of the keyboard.
Open **Settings…** from the menu-bar dropdown or **Chit-Chat → Settings** (⌘,), then click the shortcut in **Recording** to choose a single key, a
modifier-only chord, or modifiers plus a key. Release the keys to save. Escape or Cancel
keeps the previous shortcut; dictation is suspended while choosing a shortcut.

- **Quick tap:** start continuous recording; tap again to stop.
- **Hold for at least 0.35 seconds:** record until you release.
- **Record / Stop button:** start or stop directly in the app.

A single-key shortcut is reserved while Chit-Chat runs. Choose a shortcut that other apps
do not need. macOS may intercept reserved shortcuts and hardware media controls.

The microphone starts after the speech engine is ready. The HUD displays “Preparing speech
engine…” during startup. Releasing a hold during preparation cancels that recording attempt.

Chit-Chat remembers the original text field and cursor **when recording starts**. Before
inserting, it checks that the same app, field, text, and selection are still in place. If you
switch fields or edit while processing, a non-activating **Dictation ready to copy** notice
keeps the result available. Copy it from the notice, main window, or menu-bar dropdown.
Completed text is also saved in transcription history; dismissing the notice keeps history.
Unreadable/secure fields and recordings started with the main window’s Record button use
this copy workflow instead of guessing a destination. Multiple waiting results are retained.

Clipboard insertion checks focus again immediately before posting to the original app.
An uncertain insertion is never automatically retried, preventing duplicate paragraphs.
Your previous clipboard is restored only if nothing else has copied new content meanwhile.

## Check my setup

Choose **Check my setup…** in the menu-bar dropdown, the Chit-Chat menu, or **Settings →
Access & data**. Click **Start check**, then press and release your normal recording shortcut.
The result comes from the real global event listener, not a local key capture or permission
toggle. A missing event gets a timeout message with permission/conflict troubleshooting.

**Test microphone** captures five seconds and shows the input level; it reports an audible
signal or an open-but-silent microphone. Audio is discarded without transcription or AI.
**Test insertion here** runs the keyboard-paste fallback into a dedicated native test field
and checks the result. It never pastes into another app or saves test text to history or the
dictionary. Passing this test does not certify every other editor’s Accessibility support.
Dictation is paused until you click **Finish check** or close the check window; closing also
stops the microphone and cancels unfinished tests. Tests can be retried independently.

## Speech and text

Choose one engine in Settings or the menu bar:

- **Apple:** local transcription through `SpeechAnalyzer` / `SpeechTranscriber`, with live
  text while speaking. macOS downloads and manages the language models.
- **Parakeet v3:** local transcription through FluidAudio and CoreML, processing the
  recording when you stop. Its model requires a one-time download of roughly 470 MB.

Basic cleanup removes fillers, fixes spacing and punctuation, and supports spoken line
breaks. Optional smart cleanup uses Apple's on-device Foundation Models when available.
The personal dictionary applies spelling corrections after cleanup and supplies recognition
hints to the Apple engine.

After dictation is inserted, correcting a word in the same text field proposes a dictionary
rule such as `Acme Lab -> Acme Labs`. Once you pause typing for a second, a small popup shows
both spellings and a **10-second countdown around an X**. Click X to cancel; otherwise the
rule is saved and applied to future dictations. The popup leaves your cursor focused in
the editor. Undoing or revising the correction cancels the old suggestion.

The recording indicator clears and the completion sound plays as soon as the paste is sent.
Clipboard restoration, insertion verification, history saving, and edit watching run in the
background. They do not hold up the next recording. Transcription and your selected cleanup
steps still finish before the paste, so the editor receives the final text once.

Learning watches the most recent inserted text for **120 seconds** by default, while that
field stays focused. **Settings → Recording → Learn from edits → Watch for edits (seconds)**
accepts **15–600 seconds** and applies to the next dictation. Editing the inserted span
restarts this timer, allowing corrections near expiry to finish their countdown. Starting
another recording or switching fields cancels pending suggestions. Existing rules, including
disabled ones, are never overwritten. Punctuation-only changes, deletions, and paragraph
rewrites are ignored. Learning requires a field that exposes its text and selection through
macOS Accessibility; password fields and editors without that support are skipped. Document
snapshots stay in memory, and only accepted word pairs are written to the dictionary.

The History page supports search, copy, editing, individual deletion, and clearing history.
Choose **Edit**, correct the transcript, then **Save & learn**. The saved edit teaches word
corrections in the background without needing the original text field or a watch timer.
Because saving is explicit, history edits do not use the cancel countdown. Existing rules
and disabled rules are preserved, and failed saves never teach the dictionary. Editing
history does not change text already pasted into another app.
History and the dictionary are saved as plain text under `~/Library/Application Support/ChitChat`.
Dictation paste temporarily places the transcript on the macOS clipboard.
The bundle ID and preferences domain are `ai.pivotstudio.chit-chat`. On first launch after
the rename, existing preferences are imported and history/dictionary files are copied to
the new location without overwriting current data. Original files remain as backups.
The local API key and downloaded Parakeet models are reused. macOS permissions belong to
the bundle identity, so the renamed app needs its own Accessibility and microphone grants.

## Settings

Chit-Chat uses one resizable window with a sidebar for **History**, **Dictionary**, and all
settings. **Settings…** from the app/menu-bar menu or **⌘,** opens the last settings page in
that same window, even if you closed it. **Check my setup** also lives in the sidebar.

The interface uses system typography, clean light/dark surfaces, and a blue accent.
Record remains available in the header while browsing pages.

Settings include:

- **Recording:** custom shortcut, tap/hold instructions, indicator placement, recording sounds, mute audio while listening, and the background edit-watch duration.
- **Speech engine:** Apple or Parakeet, model status, and Parakeet download/preparation.
- **Text & AI:** basic cleanup, smart cleanup, live Apple Intelligence status, and a link to its system settings.
- **OpenAI:** optional cloud enhancement, API key, model selection, measured speed sorting, and an editable prompt with a sample preview.
- **Dictionary:** automatic-learning rules and the full dictionary editor.
- **Access & data:** current microphone/Accessibility permissions and local data locations.

The dropdown and main window share the same preferences. Smart cleanup can always be
switched on or off, including while Apple's model is downloading. If cleanup is off, the
smart preference is remembered. If AI is unavailable, ordinary cleanup remains the fallback.
Apple Intelligence and permission status refresh automatically while Chit-Chat runs.

### Mute audio while listening (macOS)

Enable **Settings → Recording → Sound → Mute audio while listening** (also available in
the menu-bar dropdown). It is off by default. Recording mutes the Mac's audio outputs;
YouTube, Spotify, Apple Music, and other apps continue playing silently. Stopping,
cancelling, startup failure, or quitting normally restores the saved output settings
as soon as the microphone stops, without waiting for transcription or AI cleanup.
Already-muted outputs stay muted. Changing the setting applies to the next recording.

This uses Core Audio directly and needs no browser settings or Automation permissions.
Outputs connected or selected during recording are muted too. Devices without a mute
switch use their volume controls, restoring the previous volume afterward. A message
appears under the setting for devices that cannot be controlled by macOS. Force-quitting
Chit-Chat can leave outputs muted; unmute them in the system Sound controls.

## OpenAI enhancement (macOS)

Open **Settings → OpenAI**, enter your API key, and choose **Save key**. **Refresh models**
checks the connection and lists likely text models available to your account. Choose a model
(or enter a custom model ID), edit the prompt, and use **Test my prompt** to preview a sample.
Switch on **Enhance dictation with OpenAI** when ready. It is off by default.

**Minimum words for AI** defaults to **5**, and can be changed under **Text & AI** or
**OpenAI**. Dictations below this count skip both Apple AI cleanup and OpenAI enhancement;
basic local cleanup (if enabled) and dictionary corrections still apply. The original
transcript is counted before cleanup: whitespace-separated words and numbers count;
punctuation-only tokens do not. Exactly 5 words qualifies at the default. Set 0 to remove
the minimum. Manual prompt previews and speed tests still run on demand.

**Reasoning effort** offers **Off** where a model supports disabling reasoning, plus its
documented effort levels. **Model default** omits the parameter, preserving the model’s
normal behavior. Choices are remembered per model and apply to dictation and both types of
sample test. Models that require reasoning do not offer Off; unknown/custom models use
the API default until their capabilities are verified. Higher effort may exceed the
dictation request timeout, in which case the local-text fallback still applies.

The capability table uses the official [reasoning guide](https://developers.openai.com/api/docs/guides/reasoning)
and model pages, including [GPT-5.4 mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini),
[GPT-5.6 Sol](https://developers.openai.com/api/docs/models/gpt-5.6-sol), and
[GPT-6 Astra](https://developers.openai.com/api/docs/models/gpt-6-astra). It matches exact
model aliases and dated snapshots rather than guessing support from a family prefix.

The key is stored locally in `~/Library/Application Support/ChitChat/Private/openai-api-key`
with owner-only permissions (directory 0700, file 0600), outside the app bundle and repository.
It is not encrypted by Chit-Chat and is never logged. Existing Keychain keys are migrated
once when macOS allows access without a password prompt; otherwise, paste the key into
Settings once. Dictation never accesses Keychain. The prompt, model choice,
and speed measurements are saved locally. With enhancement on, transcripts and your prompt
are sent to OpenAI; microphone audio stays local. API usage requires API billing, separate
from a ChatGPT subscription. Each sample test also uses API tokens.

**Test connection** beside the API key sends a short sample using the selected model and
reasoning effort. It reports success or a missing/rejected key, model access, quota, or
network failure. With a newly entered key, **Save & test connection** saves it first.
Changing the key, model, or effort clears the previous result. Testing does not enable
enhancement, insert text, or send your recordings; each test uses paid API tokens.

**Measure selected model’s speed** times the same short sample and default prompt. Test the
models you want to compare, then sort by **Fastest tested**. Untested models appear last.
Measurements are stored separately for each model and effort level. The ranking uses each
model’s currently remembered effort; changing effort shows an untested result until that
combination is measured. Existing measurements belong to Model default.
These are last measured round-trip times, including the network, not promised latency;
longer dictations and different prompts will take different amounts of time. The models
endpoint supplies no speed or Responses-compatibility metadata, so test a model before use.

Processing order: speech transcription → optional local cleanup (basic or Apple AI) →
optional OpenAI enhancement → dictionary corrections → insertion. OpenAI enhancement works
even when local cleanup is off. A failed, refused, incomplete, or timed-out API response
preserves the local text; the last result or fallback reason appears in Settings and the
menu bar. Canceling a recording session also cancels any enhancement request.

The client uses the [Responses API](https://developers.openai.com/api/docs/guides/text),
with the editable prompt as `instructions`, the transcript as `input`, and `store: false`.
This disables response storage for retrieval; it is not a claim of zero data retention.
Model discovery uses [List models](https://developers.openai.com/api/reference/resources/models/methods/list).

## Code layout

- `Sources/ChitChat/Core`: recording lifecycle, microphone capture, shortcuts, and insertion.
- `Sources/ChitChat/Transcription`: Apple and Parakeet implementations of `TranscriptionEngine`.
- `Sources/ChitChat/Formatting`: deterministic/on-device cleanup and the OpenAI API client.
- `Sources/ChitChat/Support/OpenAIEnhancement.swift`: enhancement preferences, speed measurements, and fallback handling.
- `Sources/ChitChat/Support/OpenAIKeyStore.swift`: local API-key storage and one-time legacy migration.
- `Sources/ChitChat/Support/TranscriptStore.swift`: persisted, observable transcription history.
- `Sources/ChitChat/UI`: main window, settings, dictionary, and recording HUD.
- `Sources/ShortcutKit`: shortcut matching, shortcut capture, and tap/hold behavior.
- `Sources/ChitChatDictionary`: dictionary correction logic.
- `Tests`: shortcut behavior, correction learning and timing, persistence, and shared dictionary vectors.

The Windows implementation lives under [windows/](windows/README.md) and shares the
dictionary behavior with macOS.

Read [AGENTS.md](AGENTS.md) before changing platform behavior or the dictionary contract.

The listening indicator is a compact 80 × 36-point display with voice bars only. Under
**Settings → Recording → On-screen indicators**, choose **Top** or **Bottom** independently
for it and the smaller correction popup. Both default to Bottom and respect the menu bar
and Dock. The correction popup retains its 10-second cancel countdown.
