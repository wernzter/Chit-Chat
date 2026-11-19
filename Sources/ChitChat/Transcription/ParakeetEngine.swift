import AVFoundation
import FluidAudio
import Foundation

actor ParakeetEngine: TranscriptionEngine {
    private static let samplesPerSecond: Double = 16_000
    private static let minimumSamples = 1_600
    private var clip: Clip?
    private var opening = false

    func preferredInputFormat() async -> AVAudioFormat? {
        AVAudioFormat(standardFormatWithSampleRate: Self.samplesPerSecond, channels: 1)
    }

    func start() async throws -> AsyncThrowingStream<TranscriptionChunk, Error> {
        guard clip == nil, !opening else { throw TranscriptionError.sessionActive }
        opening = true
        defer { opening = false }

        let recognizer = try await ParakeetModels.shared.manager()
        try Task.checkCancellation()

        let output = AsyncThrowingStream<TranscriptionChunk, Error>.makeStream()
        clip = Clip(recognizer: recognizer, output: output.continuation)
        return output.stream
    }

    func feed(_ chunk: AudioChunk) async {
        // AudioCapture provides the standard mono Float32 format requested by this engine.
        let buffer = chunk.buffer
        guard let samples = buffer.floatChannelData?.pointee else { return }
        clip?.audio.append(contentsOf: UnsafeBufferPointer(start: samples, count: Int(buffer.frameLength)))
    }

    func finish() async {
        guard let completedClip = clip else { return }
        clip = nil

        guard completedClip.audio.count >= Self.minimumSamples else {
            completedClip.output.finish()
            return
        }
        do {
            var decoder = try TdtDecoderState()
            let recognition = try await completedClip.recognizer.transcribe(completedClip.audio, decoderState: &decoder)
            let text = recognition.text.trimmingCharacters(in: .whitespacesAndNewlines)
            completedClip.output.yield(.init(text: text, isFinal: true))
            completedClip.output.finish()
        } catch {
            completedClip.output.finish(throwing: error)
        }
    }

    private struct Clip {
        let recognizer: AsrManager
        let output: AsyncThrowingStream<TranscriptionChunk, Error>.Continuation
        var audio: [Float] = []
    }
}

actor ParakeetModels {
    static let shared = ParakeetModels()

    nonisolated static var isDownloaded: Bool {
        AsrModels.modelsExist(at: AsrModels.defaultCacheDirectory(for: .v3), version: .v3, encoderPrecision: .int8)
    }

    private enum Cache {
        case empty
        case inFlight(Task<AsrManager, Error>)
        case available(AsrManager)
    }

    private var cache: Cache = .empty

    func manager() async throws -> AsrManager {
        switch cache {
        case .available(let result):
            return result
        case .inFlight(let load):
            return try await load.value
        case .empty:
            let load = Task {
                let data = try await AsrModels.downloadAndLoad(version: .v3)
                return AsrManager(models: data)
            }
            cache = .inFlight(load)
            let outcome = await load.result
            switch outcome {
            case .success(let recognizer): cache = .available(recognizer)
            case .failure: cache = .empty
            }
            return try outcome.get()
        }
    }
}
