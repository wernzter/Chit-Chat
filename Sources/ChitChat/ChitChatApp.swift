import AppKit
import SwiftUI

@main
struct ChitChatApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate

    var body: some Scene {
        // One scene for history, dictionary, settings, and setup checks.
        Window("Chit-Chat", id: "main") {
            MainWindow(controller: delegate.controller, navigation: delegate.navigation, setup: delegate.setupCheck)
        }
        .defaultSize(width: DS.Layout.windowWidth, height: DS.Layout.windowHeight)
        .windowResizability(.contentMinSize)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(replacing: .appSettings) {
                Button("Settings…") { delegate.showSettings() }
                    .keyboardShortcut(",", modifiers: .command)
                Button("Check my setup…") { delegate.showSetupCheck() }
            }
            CommandGroup(after: .appInfo) {
                Button("Reveal Dictionary File") {
                    NSWorkspace.shared.activateFileViewerSelecting([DictionaryStore.fileURL])
                }
            }
        }

        // Secondary now: status and the hotkey while you're working in another app.
        MenuBarExtra {
            MenuContent(openSettings: { delegate.showSettings() }, openSetup: { delegate.showSetupCheck() })
        } label: {
            Image(systemName: delegate.controller.state.isActive ? "waveform.circle.fill" : "waveform")
        }
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    let controller: DictationController
    private var hud: HUDPanel?
    private var correctionPopup: CorrectionPopupPanel?
    let navigation = AppNavigation()
    lazy var setupCheck = SetupCheck(controller: controller)
    private var recoveryPanel: RecoveryPanel?
    private var statusTask: Task<Void, Never>?
    private var updatePanel: ReleaseUpdatePanel?
    private var updateTask: Task<Void, Never>?

    override init() {
        AppStorage.migratePreferences()
        controller = DictationController()
        super.init()
    }

    func showSettings() {
        SettingsStatus.shared.refresh()
        navigation.showSettings()
        NSApp.activate(ignoringOtherApps: true)
    }

    func showSetupCheck() {
        navigation.showSetup()
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // A regular app now: dock icon, app menu, standard windows. The HUD is still a
        // non-activating panel, so dictating into another app never steals its focus — that
        // property belongs to the panel, not to the activation policy.
        NSApp.setActivationPolicy(.regular)
        // Complete the noninteractive legacy-key migration before recording is enabled.
        _ = OpenAIEnhancement.shared

        let listeningPanel = HUDPanel(controller: controller)
        hud = listeningPanel
        controller.onStateChange = { [weak listeningPanel] state in
            guard let listeningPanel else { return }
            if state.isActive { listeningPanel.present() } else { listeningPanel.dismiss() }
        }
        recoveryPanel = RecoveryPanel()
        observeRecovery()
        let popup = CorrectionPopupPanel(learner: .shared)
        correctionPopup = popup
        observeOverlayPlacement()
        CorrectionLearner.shared.onVisibilityChange = { [weak popup] visible in
            if visible { popup?.present() } else { popup?.orderOut(nil) }
        }

        let needsShortcut = !controller.activate()
        if needsShortcut { Permissions.promptForAccessibility() }

        // Parakeet's models take ~20s to load from disk, and that cost lands on whichever
        // dictation touches them first — so the first hold after every launch would stall
        // with the HUD showing nothing. Warm them in the background instead, but only when
        // they're actually going to be used and are already downloaded.
        if Settings.shared.engine == .parakeet, ParakeetModels.isDownloaded {
            Task.detached(priority: .utility) {
                _ = try? await ParakeetModels.shared.manager()
            }
        }

        beginStatusUpdates(recoverShortcut: needsShortcut)
        ReleaseUpdates.shared.onNoticeChange = { [weak self] in self?.refreshUpdateNotice() }
        updateTask = Task { await ReleaseUpdates.shared.runAutomaticChecks() }
        Log.app.info("Chit-Chat ready — hold \(Settings.shared.shortcut.displayName) to dictate")
    }

    func applicationWillTerminate(_ notification: Notification) {
        setupCheck.stop()
        statusTask?.cancel()
        updateTask?.cancel()
        updatePanel?.orderOut(nil)
        controller.deactivate()
    }

    private func refreshUpdateNotice() {
        let updates = ReleaseUpdates.shared
        guard updates.notice != nil else {
            updatePanel?.orderOut(nil)
            return
        }
        let panel = updatePanel ?? ReleaseUpdatePanel(updates: updates)
        updatePanel = panel
        panel.showNotice()
    }

    private func observeOverlayPlacement() {
        withObservationTracking {
            _ = Settings.shared.listeningIndicatorPosition
            _ = Settings.shared.correctionPopupPosition
        } onChange: { [weak self] in
            Task { @MainActor in
                self?.hud?.reposition()
                self?.correctionPopup?.reposition()
                self?.observeOverlayPlacement()
            }
        }
    }

    private func observeRecovery() {
        withObservationTracking {
            _ = DictationRecovery.shared.items.count
        } onChange: { [weak self] in
            Task { @MainActor in
                self?.recoveryPanel?.refresh()
                self?.observeRecovery()
            }
        }
    }

    private func beginStatusUpdates(recoverShortcut: Bool) {
        statusTask = Task { [weak controller] in
            var pending = recoverShortcut
            let status = SettingsStatus.shared
            while !Task.isCancelled, let controller {
                status.refresh()
                if pending, status.accessibility, controller.activate() {
                    pending = false
                    Log.app.info("Recording shortcut is ready.")
                }
                let nextCheck = pending ? Duration.seconds(1) : DS.Preferences.refreshInterval
                do { try await Task.sleep(for: nextCheck) }
                catch { return }
            }
        }
    }
}

struct MenuContent: View {
    @State private var transcripts = TranscriptStore.shared
    @State private var recovery = DictationRecovery.shared
    @State private var openAI = OpenAIEnhancement.shared
    @State private var settings = Settings.shared
    @State private var status = SettingsStatus.shared
    @State private var updates = ReleaseUpdates.shared
    let openSettings: () -> Void
    var openSetup: () -> Void = {}

    var body: some View {
        Button("Copy last chat") {
            guard let latest = transcripts.runs.last else { return }
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(latest.text, forType: .string)
        }
        .disabled(transcripts.runs.isEmpty)
        Divider()
        Text("Shortcut: \(settings.shortcut.displayName)")
        Text("Tap to toggle · Hold to dictate")
        Divider()
        Button("Settings…", action: openSettings)
            .keyboardShortcut(",", modifiers: .command)
        Button("Check my setup…", action: openSetup)
        if let release = updates.latestRelease {
            Link("View Chit-Chat \(release.tag) release…", destination: release.releasePage)
        }
        if let item = recovery.items.last {
            Button("Copy waiting dictation (\(recovery.items.count))") { recovery.copy(item) }
        }
        Picker("Speech engine", selection: $settings.engine) {
            ForEach(SpeechEngineChoice.allCases, id: \.self) { choice in
                Text(choice.displayName).tag(choice)
            }
        }
        Toggle("Clean up text", isOn: $settings.cleanupEnabled)
        Toggle("Smart cleanup (on-device AI)", isOn: $settings.smartCleanup)
        if !settings.cleanupEnabled {
            Text("Turn on Clean up text to apply cleanup.")
        } else if settings.smartCleanup && !status.aiReady {
            Text(status.aiMessage)
        }
        Toggle("Recording sounds", isOn: $settings.soundEnabled)
        Toggle("Mute audio while listening", isOn: $settings.muteAudioWhileListening)
        Toggle("OpenAI enhancement", isOn: $openAI.enabled)
        if openAI.enabled, let result = openAI.lastDictationStatus { Text(result) }
        Divider()
        Button("Quit Chit-Chat") { NSApp.terminate(nil) }
            .keyboardShortcut("q")
    }
}
