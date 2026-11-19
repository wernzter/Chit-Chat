import Foundation
import Observation
import ShortcutKit

enum SpeechEngineChoice: String, CaseIterable, Sendable {
    case apple, parakeet

    var showsLiveText: Bool { self == .apple }
    var displayName: String { showsLiveText ? "Apple (streaming)" : "Parakeet (batch)" }
}

@MainActor
@Observable
final class Settings {
    static let shared = Settings()
    static let editWatchSecondsRange = 15...600
    static let defaultEditWatchSeconds = 120

    @ObservationIgnored private let disk: UserDefaults

    var shortcut: RecordingShortcut {
        didSet { persist(try? JSONEncoder().encode(shortcut), .shortcut) }
    }

    var engine: SpeechEngineChoice {
        didSet { persist(engine.rawValue, .engine) }
    }

    var cleanupEnabled: Bool {
        didSet { persist(cleanupEnabled, .cleanupEnabled) }
    }

    var smartCleanup: Bool {
        didSet { persist(smartCleanup, .smartCleanup) }
    }

    var soundEnabled: Bool {
        didSet { persist(soundEnabled, .soundEnabled) }
    }

    var muteAudioWhileListening: Bool {
        didSet { persist(muteAudioWhileListening, .muteAudioWhileListening) }
    }

    var listeningIndicatorPosition: OverlayPosition {
        didSet { persist(listeningIndicatorPosition.rawValue, .listeningIndicatorPosition) }
    }

    var correctionPopupPosition: OverlayPosition {
        didSet { persist(correctionPopupPosition.rawValue, .correctionPopupPosition) }
    }

    private var editWindow: Int
    var editWatchSeconds: Int {
        get { editWindow }
        set {
            editWindow = Self.editWindowWithinBounds(newValue)
            persist(editWindow, .editWatchSeconds)
        }
    }

    private var aiThreshold: Int
    var minimumAIWordCount: Int {
        get { aiThreshold }
        set {
            aiThreshold = max(0, newValue)
            persist(aiThreshold, .minimumAIWordCount)
        }
    }

    init(defaults: UserDefaults = .standard) {
        disk = defaults
        func value<Value>(_ key: PreferenceKey, fallback: Value) -> Value {
            defaults.object(forKey: key.rawValue) as? Value ?? fallback
        }

        engine = SpeechEngineChoice(rawValue: value(.engine, fallback: "")) ?? .apple
        listeningIndicatorPosition = OverlayPosition(rawValue: value(.listeningIndicatorPosition, fallback: "")) ?? .bottom
        correctionPopupPosition = OverlayPosition(rawValue: value(.correctionPopupPosition, fallback: "")) ?? .bottom

        smartCleanup = value(.smartCleanup, fallback: false)
        cleanupEnabled = value(.cleanupEnabled, fallback: true)
        soundEnabled = value(.soundEnabled, fallback: true)
        muteAudioWhileListening = value(.muteAudioWhileListening, fallback: false)
        editWindow = Self.editWindowWithinBounds(value(.editWatchSeconds, fallback: Self.defaultEditWatchSeconds))
        aiThreshold = max(0, value(.minimumAIWordCount, fallback: 5))

        let savedShortcut = defaults.data(forKey: PreferenceKey.shortcut.rawValue).flatMap {
            try? JSONDecoder().decode(RecordingShortcut.self, from: $0)
        }
        shortcut = savedShortcut.flatMap { $0.isValid ? $0 : nil } ?? .default
    }

    private static func editWindowWithinBounds(_ seconds: Int) -> Int {
        let bounds = editWatchSecondsRange
        return max(bounds.lowerBound, min(seconds, bounds.upperBound))
    }

    private func persist(_ value: Any?, _ key: PreferenceKey) {
        disk.set(value, forKey: key.rawValue)
    }

    private enum PreferenceKey: String {
        case shortcut = "recordingShortcut"
        case engine
        case cleanupEnabled, smartCleanup
        case soundEnabled, muteAudioWhileListening
        case editWatchSeconds, minimumAIWordCount
        case listeningIndicatorPosition, correctionPopupPosition
    }
}
