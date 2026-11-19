import AVFoundation
import Speech

actor AppleSpeechEngine: TranscriptionEngine {
    private let requestedLocale: Locale
    private var active: StreamingSession?
    private var preparing = false

    init(locale: Locale = .current) { requestedLocale = locale }

    func preferredInputFormat() async -> AVAudioFormat? { active?.format }

    func start() async throws -> AsyncThrowingStream<TranscriptionChunk, Error> {
        guard active == nil, !preparing else { throw TranscriptionError.sessionActive }
        preparing = true
        defer { preparing = false }

        guard SpeechTranscriber.isAvailable else {
            throw TranscriptionError.localeUnsupported(requestedLocale)
        }
        let fallback = Locale(identifier: "en-US")
        let supported = await SpeechTranscriber.supportedLocale(equivalentTo: requestedLocale)
        let resolved: Locale
        if let supported {
            resolved = supported
        } else if let english = await SpeechTranscriber.supportedLocale(equivalentTo: fallback) {
            resolved = english
        } else {
            throw TranscriptionError.localeUnsupported(requestedLocale)
        }

        var configuration = SpeechTranscriber.Preset.progressiveTranscription
        configuration.reportingOptions = [.volatileResults]
        let module = SpeechTranscriber(locale: resolved, preset: configuration)
        do {
            if await AssetInventory.status(forModules: [module]) != .installed,
               let download = try await AssetInventory.assetInstallationRequest(supporting: [module]) {
                try await download.downloadAndInstall()
            }
        } catch {
            throw TranscriptionError.modelInstallFailed(error.localizedDescription)
        }

        guard let format = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [module]) else {
            throw TranscriptionError.noAudioFormat
        }
        let audio = AsyncStream<AnalyzerInput>.makeStream()
        let context = AnalysisContext()
        context.contextualStrings[.general] = await MainActor.run { DictionaryStore.shared.biasPhrases }
        let analyzer = SpeechAnalyzer(inputSequence: audio.stream, modules: [module], analysisContext: context)

        do {
            try Task.checkCancellation()
            try await analyzer.prepareToAnalyze(in: format)
            try Task.checkCancellation()
        } catch {
            audio.continuation.finish()
            await analyzer.cancelAndFinishNow()
            throw error
        }

        let output = AsyncThrowingStream<TranscriptionChunk, Error>.makeStream(bufferingPolicy: .bufferingNewest(1))
        let relay = Task {
            await Self.relayResults(from: module, to: output.continuation)
        }
        active = StreamingSession(analyzer: analyzer, format: format, audio: audio.continuation, relay: relay)
        return output.stream
    }

    func feed(_ chunk: AudioChunk) async {
        active?.audio.yield(AnalyzerInput(buffer: chunk.buffer))
    }

    func finish() async {
        guard let session = active else { return }
        active = nil
        session.audio.finish()

        do {
            try await session.analyzer.finalizeAndFinishThroughEndOfInput()
        } catch {
            await session.analyzer.cancelAndFinishNow()
        }
        await session.relay.value
    }

    private static func relayResults(from module: SpeechTranscriber,
                                     to output: AsyncThrowingStream<TranscriptionChunk, Error>.Continuation) async {
        var committed = ""
        do {
            for try await result in module.results {
                let words = String(result.text.characters)
                if result.isFinal { committed.append(words) }
                let display = result.isFinal ? committed : committed + words
                output.yield(.init(text: display.trimmingCharacters(in: .whitespaces), isFinal: false))
            }
            output.yield(.init(text: committed.trimmingCharacters(in: .whitespaces), isFinal: true))
            output.finish()
        } catch {
            output.finish(throwing: error)
        }
    }

    private struct StreamingSession {
        let analyzer: SpeechAnalyzer
        let format: AVAudioFormat
        let audio: AsyncStream<AnalyzerInput>.Continuation
        let relay: Task<Void, Never>
    }
}
