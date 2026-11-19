import AppKit
import FoundationModels
import Observation

/// Shared, refreshed status for the menu and Settings. Availability is not a preference:
/// users may enable AI in advance, or disable it even while Apple's model is unavailable.
@MainActor
@Observable
final class SettingsStatus {
    static let shared = SettingsStatus()
    private(set) var aiMessage = "Checking Apple Intelligence…"
    private(set) var aiReady = false
    private(set) var accessibility = false
    private(set) var microphone = false
    private(set) var parakeetInstalled = ParakeetModels.isDownloaded
    private(set) var preparingParakeet = false
    private(set) var modelError: String?

    func refresh() {
        let availability = SystemLanguageModel(useCase: .general).availability
        aiReady = availability == .available
        switch availability {
        case .available: aiMessage = "Apple Intelligence is ready."
        case .unavailable(let reason):
            switch reason {
            case .appleIntelligenceNotEnabled: aiMessage = "Enable Apple Intelligence in System Settings. Basic cleanup is used until it’s ready."
            case .modelNotReady: aiMessage = "Apple’s model is preparing or downloading. Basic cleanup is used until it’s ready."
            case .deviceNotEligible: aiMessage = "Apple Intelligence is unavailable on this Mac. Basic cleanup is still available."
            @unknown default: aiMessage = "Apple Intelligence is temporarily unavailable. Basic cleanup is used instead."
            }
        @unknown default: aiMessage = "Apple Intelligence is temporarily unavailable."
        }
        accessibility = Permissions.hasAccessibility
        microphone = Permissions.hasMicrophone
        parakeetInstalled = ParakeetModels.isDownloaded
    }

    func prepareParakeet() {
        guard !preparingParakeet else { return }
        preparingParakeet = true
        modelError = nil
        Task {
            do { _ = try await ParakeetModels.shared.manager() }
            catch { modelError = error.localizedDescription }
            preparingParakeet = false
            refresh()
        }
    }

    static func openAppleIntelligence() {
        NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.Siri-Settings.extension")!)
    }
}
