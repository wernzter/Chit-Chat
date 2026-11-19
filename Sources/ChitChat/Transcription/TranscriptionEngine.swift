import AVFoundation
import Foundation

// Capture transfers a private buffer to a single engine and never mutates it again.
struct AudioChunk: @unchecked Sendable {
    let buffer: AVAudioPCMBuffer
}

struct TranscriptionChunk: Sendable {
    // This is a complete snapshot, including any provisional words.
    let text: String
    let isFinal: Bool
}

protocol TranscriptionEngine: Actor {
    func start() async throws -> AsyncThrowingStream<TranscriptionChunk, Error>
    func preferredInputFormat() async -> AVAudioFormat?
    func feed(_ chunk: AudioChunk) async
    func finish() async
}

enum TranscriptionError: LocalizedError {
    case localeUnsupported(Locale)
    case modelInstallFailed(String)
    case noAudioFormat
    case sessionActive

    var errorDescription: String? {
        switch self {
        case let .localeUnsupported(locale):
            "Speech recognition is unavailable for \(locale.identifier) on this Mac."
        case let .modelInstallFailed(detail):
            "The speech model could not be installed. \(detail)"
        case .noAudioFormat:
            "The microphone and speech recognizer could not agree on an audio format."
        case .sessionActive:
            "A speech recognition session is already active."
        }
    }
}
