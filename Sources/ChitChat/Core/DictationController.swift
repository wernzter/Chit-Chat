import AppKit
import ChitChatDictionary
import Observation
import ShortcutKit

@MainActor
@Observable
final class DictationController {
    enum State: Equatable {
        case idle, starting, listening, finishing
        case error(String)

        var isActive: Bool { self == .starting || self == .listening || self == .finishing }
    }

    private(set) var state = State.idle {
        didSet {
            if state != oldValue { onStateChange?(state) }
        }
    }
    private(set) var transcript = ""
    private(set) var level: Float = 0
    private(set) var enhancing = false
    private(set) var isCheckingSetup = false

    @ObservationIgnored private let hotkey = HotkeyMonitor()
    @ObservationIgnored var onStateChange: ((State) -> Void)?
    @ObservationIgnored private let microphone = AudioCapture()
    @ObservationIgnored private let formatter: (any TextFormatter)?
    @ObservationIgnored private let makeEngine: @MainActor () -> any TranscriptionEngine
    @ObservationIgnored private var gesture = RecordingGesture()
    @ObservationIgnored private var current: Recording?
    @ObservationIgnored private var shortcutCapturedBySettings = false
    @ObservationIgnored private var diagnostics: ShortcutDiagnostics?
    @ObservationIgnored private var clearError: Task<Void, Never>?

    init(formatter: (any TextFormatter)? = nil,
         makeEngine: @escaping @MainActor () -> any TranscriptionEngine = {
             switch Settings.shared.engine {
             case .apple: AppleSpeechEngine()
             case .parakeet: ParakeetEngine()
             }
         }) {
        self.formatter = formatter
        self.makeEngine = makeEngine
    }

    @discardableResult
    func activate() -> Bool {
        guard !shortcutCapturedBySettings else { return false }
        hotkey.shortcut = Settings.shared.shortcut
        hotkey.onPress = { [weak self] in self?.handlePress() }
        hotkey.onRelease = { [weak self] in self?.handleRelease() }
        hotkey.onCancel = { [weak self] in self?.abandon() }
        return hotkey.start()
    }

    func deactivate() {
        hotkey.stop()
        CorrectionLearner.shared.stop()
        abandon()
    }

    @discardableResult
    func reloadHotkey() -> Bool { activate() }

    func setShortcutCapture(_ enabled: Bool) {
        guard diagnostics == nil else { return }
        shortcutCapturedBySettings = enabled
        gesture.reset()
        if enabled {
            hotkey.stop()
        } else {
            activate()
        }
    }

    func beginSetupCheck(pressed: @escaping () -> Void, released: @escaping () -> Void) -> Bool {
        guard current == nil, diagnostics == nil, !shortcutCapturedBySettings else { return false }
        CorrectionLearner.shared.stop()
        gesture.reset()
        diagnostics = ShortcutDiagnostics(pressed: pressed, released: released)
        isCheckingSetup = true
        return reloadHotkey()
    }

    func endSetupCheck() {
        diagnostics = nil
        isCheckingSetup = false
        gesture.reset()
    }

    func startButtonRecording() { startRecording() }
    func stopButtonRecording() { stopRecording() }

    private func handlePress() {
        if let diagnostics {
            diagnostics.pressed()
            return
        }
        let command = gesture.press(at: ProcessInfo.processInfo.systemUptime,
                                    recording: state == .starting || state == .listening,
                                    busy: state == .finishing)
        if command == .start { startRecording() }
        if command == .stop { stopRecording() }
    }

    private func handleRelease() {
        if let diagnostics {
            diagnostics.released()
        } else if gesture.release(at: ProcessInfo.processInfo.systemUptime) == .stop {
            stopRecording()
        }
    }

    private func startRecording() {
        guard state == .idle, diagnostics == nil else { return }
        let recording = Recording(target: InsertionDestination.capture(),
                                  engineName: Settings.shared.engine.displayName)
        CorrectionLearner.shared.stop()
        current = recording
        transcript = ""
        state = .starting
        AudioOutputMuteController.shared.begin(enabled: Settings.shared.muteAudioWhileListening)

        recording.lifecycle = Task { [weak self, weak recording] in
            guard let recording else { return }
            await self?.connectMicrophone(to: recording)
        }
    }

    private func connectMicrophone(to recording: Recording) async {
        let granted = await Permissions.requestMicrophone()
        guard owns(recording) else { return }
        guard granted else {
            fail(recording, message: "Allow Chit-Chat to use the microphone in System Settings under Privacy & Security.")
            return
        }

        let recognizer = makeEngine()
        recording.recognizer = recognizer
        do {
            let snapshots = try await recognizer.start()
            guard owns(recording) else {
                await recognizer.finish()
                return
            }
            let format = await recognizer.preferredInputFormat()
            guard owns(recording) else {
                await recognizer.finish()
                return
            }
            guard let format else { throw TranscriptionError.noAudioFormat }

            let frames = AsyncStream<AudioChunk>.makeStream(bufferingPolicy: .bufferingNewest(64))
            recording.frames = frames.continuation
            recording.forwardAudio = Task.detached(priority: .userInitiated) {
                for await frame in frames.stream {
                    await recognizer.feed(frame)
                }
            }

            try microphone.start(outputFormat: format, onBuffer: { frame in
                frames.continuation.yield(frame)
            }, onLevel: { [weak self, weak recording] sample in
                Task { @MainActor [weak self, weak recording] in
                    guard let self, let recording, current === recording, state == .listening else { return }
                    level = 0.65 * level + 0.35 * sample
                }
            })

            state = .listening
            playSound("Tink")
            recording.readTranscript = Task { [weak self, weak recording] in
                guard let recording else { return }
                await self?.display(snapshots, for: recording)
            }
        } catch {
            guard owns(recording) else { return }
            fail(recording, message: error.localizedDescription)
        }
    }

    private func display(_ updates: AsyncThrowingStream<TranscriptionChunk, Error>,
                         for recording: Recording) async {
        do {
            for try await snapshot in updates {
                guard owns(recording) else { return }
                transcript = snapshot.text
            }
        } catch {
            if owns(recording) { fail(recording, message: error.localizedDescription) }
        }
    }

    private func stopRecording() {
        guard let recording = current else { return }
        switch state {
        case .starting:
            abandon()
        case .listening:
            let stoppedAt = Date.now
            state = .finishing
            closeInput(recording)
            recording.lifecycle = Task { [weak self, weak recording] in
                guard let recording else { return }
                await self?.complete(recording, stoppedAt: stoppedAt)
            }
        default:
            break
        }
    }

    private func complete(_ recording: Recording, stoppedAt: Date) async {
        await recording.forwardAudio?.value
        guard owns(recording) else { return }
        await recording.recognizer?.finish()
        await recording.readTranscript?.value
        guard owns(recording) else { return }

        let raw = transcript
        guard raw.contains(where: { !$0.isWhitespace }) else {
            resetDisplay()
            return
        }

        let minimum = Settings.shared.minimumAIWordCount
        var text = await DictationCleanup.format(raw, enabled: Settings.shared.cleanupEnabled,
            smartCleanup: Settings.shared.smartCleanup, minimumWords: minimum,
            basicFormatter: formatter ?? RuleBasedFormatter(), smartFormatter: formatter ?? FoundationModelFormatter())
        guard owns(recording) else { return }

        let enhancement = OpenAIEnhancement.shared
        if enhancement.enabled {
            enhancing = DictationCleanup.allowsAI(raw, minimumWords: minimum)
            do {
                text = try await enhancement.enhance(text, originalText: raw, minimumWords: minimum)
            } catch {
                if owns(recording) { resetDisplay() }
                return
            }
            guard owns(recording) else { return }
            enhancing = false
        }

        let compiled = DictionaryStore.shared.corrector.apply(to: text)
        let verification = await TextInjector.submit(compiled.text, into: recording.target)
        guard owns(recording) else { return }
        if verification == nil { DictationRecovery.shared.retain(compiled.text, result: .retained) }

        let event = DictationRun(date: stoppedAt, engine: recording.engineName,
            processSeconds: Date.now.timeIntervalSince(stoppedAt), text: compiled.text,
            corrections: compiled.applied.isEmpty ? nil : compiled.applied)
        resetDisplay()
        playSound("Pop")
        Task { @MainActor in TranscriptStore.shared.record(event) }
    }

    private func owns(_ recording: Recording) -> Bool {
        current === recording && !Task.isCancelled
    }

    private func closeInput(_ recording: Recording) {
        microphone.stop()
        AudioOutputMuteController.shared.end()
        recording.frames?.finish()
        recording.frames = nil
        gesture.reset()
        level = 0
    }

    private func abandon() {
        let recording = current
        resetDisplay()
        guard let recording else { return }

        closeInput(recording)
        recording.lifecycle?.cancel()
        recording.forwardAudio?.cancel()
        recording.readTranscript?.cancel()
        if let recognizer = recording.recognizer {
            Task { await recognizer.finish() }
        }
    }

    private func fail(_ recording: Recording, message: String) {
        guard current === recording else { return }
        abandon()
        Log.app.error("Recording failed: \(message)")
        state = .error(message)
        clearError = Task { [weak self] in
            do { try await Task.sleep(for: .seconds(3)) }
            catch { return }
            guard let self, state == .error(message) else { return }
            resetDisplay()
        }
    }

    private func resetDisplay() {
        clearError?.cancel()
        clearError = nil
        gesture.reset()
        current = nil
        state = .idle
        transcript = ""
        level = 0
        enhancing = false
    }

    private func playSound(_ name: String) {
        if Settings.shared.soundEnabled { NSSound(named: NSSound.Name(name))?.play() }
    }

    private struct ShortcutDiagnostics {
        let pressed: () -> Void
        let released: () -> Void
    }

    @MainActor
    private final class Recording {
        let target: InsertionDestination?
        let engineName: String

        var lifecycle: Task<Void, Never>?
        var frames: AsyncStream<AudioChunk>.Continuation?
        var forwardAudio: Task<Void, Never>?
        var readTranscript: Task<Void, Never>?
        var recognizer: (any TranscriptionEngine)?

        init(target: InsertionDestination?, engineName: String) {
            self.target = target
            self.engineName = engineName
        }
    }
}
