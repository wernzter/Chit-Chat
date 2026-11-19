import Observation

enum AppPage: String, CaseIterable {
    case history, dictionary, recording, speech, cleanup, openAI, access, setup

    var title: String {
        switch self {
        case .history: "History"
        case .dictionary: "Dictionary"
        case .recording: "Recording"
        case .speech: "Speech engine"
        case .cleanup: "Text & AI"
        case .openAI: "OpenAI"
        case .access: "Access & data"
        case .setup: "Check my setup"
        }
    }
    var icon: String {
        switch self {
        case .history: "clock.arrow.circlepath"
        case .dictionary: "character.book.closed"
        case .recording: "waveform"
        case .speech: "mic"
        case .cleanup: "text.badge.checkmark"
        case .openAI: "sparkles"
        case .access: "lock.shield"
        case .setup: "checkmark.circle"
        }
    }
    var settingsSection: PreferencesSection? {
        switch self {
        case .recording: .recording
        case .speech: .speech
        case .cleanup: .cleanup
        case .openAI: .openAI
        case .access: .access
        default: nil
        }
    }
    var detail: String {
        switch self {
        case .history: "Everything you’ve said, in one place."
        case .dictionary: "Your words. Your preferred spelling."
        case .setup: "Make sure your shortcut, microphone, and text insertion are ready."
        default: settingsSection?.detail ?? ""
        }
    }
}

/// Every entry point targets the same SwiftUI Window scene, including after it closes.
@MainActor @Observable
final class AppNavigation {
    private(set) var page: AppPage = .history
    private var lastSettingsPage: AppPage = .recording
    @ObservationIgnored var openMainWindow: (() -> Void)?

    func select(_ page: AppPage) {
        self.page = page
        if page.settingsSection != nil { lastSettingsPage = page }
    }
    func showSettings() {
        select(lastSettingsPage)
        openMainWindow?()
    }
    func showSetup() {
        select(.setup)
        openMainWindow?()
    }
}
