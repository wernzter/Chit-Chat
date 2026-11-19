import AppKit
import SwiftUI

enum PreferencesSection: String, CaseIterable, Identifiable {
    case recording = "Recording", speech = "Speech engine", cleanup = "Text & AI", openAI = "OpenAI", access = "Access & data"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .recording: "keyboard"
        case .speech: "waveform"
        case .cleanup: "text.badge.checkmark"
        case .openAI: "sparkles"
        case .access: "lock.shield"
        }
    }
    var detail: String {
        switch self {
        case .recording: "Choose how you start and stop dictation."
        case .speech: "Choose the on-device engine that turns your voice into text."
        case .cleanup: "Choose how Chit-Chat polishes your transcript before inserting it."
        case .openAI: "Enhance your words with your choice of OpenAI model and prompt."
        case .access: "Check permissions and find your locally stored data."
        }
    }
}

struct SettingsPage: View {
    @Bindable var controller: DictationController
    var openSetup: () -> Void = {}
    @State private var settings = Settings.shared
    @State private var status = SettingsStatus.shared
    let section: PreferencesSection

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: DS.Space.wide) { page }
                .frame(maxWidth: DS.Layout.settingsContentWidth, alignment: .leading)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(DS.Space.panel)
        }
        .onAppear { status.refresh() }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in status.refresh() }
    }

    @ViewBuilder private var page: some View {
        switch section {
        case .recording:
            card("Recording shortcut") {
                ShortcutRecorder(controller: controller)
                Divider()
                explanation("Quick tap", "Start continuous recording. Tap the same shortcut again to stop.")
                explanation("Hold and release", "Hold for at least 0.35 seconds; release to finish recording.")
                if controller.state.isActive { note("Finish recording before changing your shortcut.") }
            }
            card("On-screen indicators") {
                Picker("Listening indicator", selection: $settings.listeningIndicatorPosition) {
                    ForEach(OverlayPosition.allCases, id: \.self) { Text($0.label).tag($0) }
                }.pickerStyle(.segmented)
                Picker("Correction popup", selection: $settings.correctionPopupPosition) {
                    ForEach(OverlayPosition.allCases, id: \.self) { Text($0.label).tag($0) }
                }.pickerStyle(.segmented)
                note("Choose each position independently. The compact voice bars show recording and processing; correction popups keep the cancel countdown. Placement stays clear of the menu bar and Dock.")
            }
            card("Sound") {
                settingToggle("Recording sounds", detail: "Play a sound when listening starts and as soon as your text is sent to the editor.", value: $settings.soundEnabled)
                Divider()
                settingToggle("Mute audio while listening", detail: "Mute system audio when you activate the mic. Restore the previous mute and volume settings when you stop or cancel. Media keeps playing silently.", value: $settings.muteAudioWhileListening)
                if let status = AudioOutputMuteController.shared.status { note(status) }
            }
            card("Learn from edits") {
                LabeledContent("Watch for edits (seconds)") {
                    TextField("Seconds", value: $settings.editWatchSeconds, format: .number.grouping(.never))
                        .textFieldStyle(.roundedBorder)
                        .frame(width: DS.Preferences.watchSecondsFieldWidth)
                        .accessibilityLabel("Watch for edits in seconds")
                }
                note("Watch the inserted text in the background for 15–600 seconds (default: 120). The timer restarts when you edit that text. Changes apply to your next dictation. Switching fields or starting another recording ends the watch, including any pending countdown.")
                note("The recording indicator and completion sound finish as soon as the paste is sent. You can also edit a transcript in History and choose Save & learn at any time.")
            }
        case .speech:
            card("Transcription engine") {
                Picker("Speech engine", selection: $settings.engine) {
                    Text("Apple").tag(SpeechEngineChoice.apple)
                    Text("Parakeet v3").tag(SpeechEngineChoice.parakeet)
                }.pickerStyle(.segmented)
                explanation("Apple", "Shows live text while you speak. Uses your dictionary’s recognition hints. macOS manages its speech models.")
                explanation("Parakeet v3", "Transcribes when you stop recording. Runs locally on the Neural Engine and needs a one-time model download.")
                note("Engine changes apply to your next recording. Both engines apply your dictionary corrections.")
            }
            card("Parakeet model") {
                Text(status.preparingParakeet ? "Preparing Parakeet…" : status.parakeetInstalled ? "Model installed" : "Model not downloaded")
                    .font(DS.Font.bodyEmphasis)
                note("Approximately 470 MB. Download once, then transcribe offline.")
                if status.preparingParakeet { ProgressView().controlSize(.small) }
                Button(status.parakeetInstalled ? "Prepare model" : "Download model") { status.prepareParakeet() }
                    .disabled(status.preparingParakeet)
                if let error = status.modelError { note("Couldn’t prepare the model: \(error)") }
            }
        case .cleanup:
            card("Short dictations") { AIWordLimitSetting() }
            card("Transcript cleanup") {
                settingToggle("Clean up text", detail: "Remove fillers and tidy spacing and punctuation before inserting text.", value: $settings.cleanupEnabled)
                Divider()
                settingToggle("Smart cleanup (on-device AI)", detail: "Use Apple Intelligence for punctuation, spoken lists, and mid-sentence corrections. Your transcript stays on this Mac.", value: $settings.smartCleanup)
                note(!settings.cleanupEnabled
                     ? "Cleanup is off. Your smart-cleanup preference is remembered; enable Clean up text to use it."
                     : settings.smartCleanup ? "AI cleanup is selected. Basic cleanup is the fallback if AI is unavailable or cannot finish."
                     : "Basic cleanup is selected. No AI model is used for cleanup.")
            }
            card("Apple Intelligence") {
                Label(status.aiReady ? "Ready on this Mac" : "Not ready yet", systemImage: status.aiReady ? "checkmark.circle" : "info.circle")
                    .font(DS.Font.bodyEmphasis)
                note(status.aiMessage)
                HStack {
                    Button("Open Apple Intelligence settings") { SettingsStatus.openAppleIntelligence() }
                    Button("Check again") { status.refresh() }
                }
                note("The availability status refreshes automatically. You can turn smart cleanup on or off at any time.")
            }
            note("OpenAI enhancement is a separate optional step, configured in OpenAI. Dictionary corrections are always applied last.")
        case .openAI:
            OpenAISettingsView()
        case .access:
            card("Check my setup") {
                note("Test a real shortcut press, microphone signal, and insertion into a safe test field.")
                Button("Check my setup…", action: openSetup)
            }
            card("Permissions") {
                permission("Accessibility", granted: status.accessibility,
                           detail: "Required for the global shortcut, text insertion, and learning from edits.", action: Permissions.openAccessibilitySettings)
                Divider()
                permission("Microphone", granted: status.microphone,
                           detail: "Required to record your voice.", action: Permissions.openMicrophoneSettings)
                Button("Refresh permissions") { status.refresh() }
            }
            card("Your data") {
                note("Transcription history and your dictionary are stored locally on this Mac.")
                Button("Show data folder in Finder") {
                    NSWorkspace.shared.open(DictionaryStore.fileURL.deletingLastPathComponent())
                }
                Button("Show dictionary file in Finder") {
                    NSWorkspace.shared.activateFileViewerSelecting([DictionaryStore.fileURL])
                }
                note("Edit, copy, or delete transcripts from History in the sidebar. Saving an edit also teaches your dictionary.")
            }
        }
    }

    private func card<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: DS.Space.roomy) {
            Text(title).font(DS.Font.title)
            content()
        }
        .padding(DS.Space.wide)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.window))
    }

    private func note(_ text: String) -> some View {
        Text(text).foregroundStyle(DS.Color.inkSecondary).fixedSize(horizontal: false, vertical: true)
    }

    private func explanation(_ title: String, _ detail: String) -> some View {
        VStack(alignment: .leading, spacing: DS.Space.tight) {
            Text(title).font(DS.Font.bodyEmphasis)
            note(detail)
        }
    }

    private func settingToggle(_ title: String, detail: String, value: Binding<Bool>) -> some View {
        Toggle(isOn: value) {
            explanation(title, detail).frame(maxWidth: .infinity, alignment: .leading)
        }.toggleStyle(.switch)
            .accessibilityLabel(title)
            .accessibilityHint(detail)
    }

    private func permission(_ title: String, granted: Bool, detail: String, action: @escaping () -> Void) -> some View {
        VStack(alignment: .leading, spacing: DS.Space.snug) {
            HStack {
                Text(title).font(DS.Font.bodyEmphasis)
                Spacer()
                Text(granted ? "Allowed" : "Not allowed")
            }
            note(detail)
            Button("Open \(title) settings", action: action)
        }
    }
}
