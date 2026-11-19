import AppKit
import AVFoundation
import Observation

struct ShortcutCheckProgress {
    private(set) var pressed = false
    private(set) var passed = false
    mutating func press() { pressed = true }
    mutating func release() { if pressed { passed = true } }
}

@MainActor @Observable
final class SetupCheck {
    private let controller: DictationController
    private(set) var active = false
    private(set) var shortcut = ShortcutCheckProgress()
    private(set) var shortcutMessage = "Press Start check, then press and release your recording shortcut."
    private(set) var microphoneMessage = "Not tested."
    private(set) var insertionMessage = "Not tested."
    private(set) var microphoneRunning = false
    private(set) var insertionRunning = false
    private(set) var level: Float = 0
    private var signalDetected = false
    private var timeoutTask: Task<Void, Never>?
    private var microphoneTask: Task<Void, Never>?
    private var insertionTask: Task<Void, Never>?
    private var capture: AudioCapture?
    private var generation = UUID()
    @ObservationIgnored weak var textView: NSTextView?

    init(controller: DictationController) { self.controller = controller }

    func start() {
        guard !controller.state.isActive, !controller.isCheckingSetup else {
            shortcutMessage = "Finish recording or the current check first."; return
        }
        generation = UUID()
        let installed = controller.beginSetupCheck(pressed: { [weak self] in self?.pressed() },
                                                  released: { [weak self] in self?.released() })
        active = controller.isCheckingSetup
        guard active else { shortcutMessage = "Finish changing the shortcut first."; return }
        microphoneMessage = "Not tested."
        insertionMessage = "Not tested."
        shortcut = ShortcutCheckProgress()
        if installed { waitForShortcut() }
        else { shortcutMessage = "Shortcut listener could not start. Open Accessibility settings, check Chit-Chat’s access, then retry." }
    }

    func retryShortcut() {
        guard active else { return }
        shortcut = ShortcutCheckProgress()
        if controller.reloadHotkey() { waitForShortcut() }
        else { shortcutMessage = "Shortcut listener could not start. Check Accessibility permission, then retry." }
    }

    private func waitForShortcut() {
        timeoutTask?.cancel()
        shortcutMessage = "Waiting for \(Settings.shared.shortcut.displayName). Press and release it within 15 seconds."
        timeoutTask = Task { @MainActor in
            do { try await Task.sleep(for: .seconds(15)) } catch { return }
            guard active, !shortcut.passed else { return }
            shortcutMessage = "No complete shortcut press received. Check Accessibility, release all keys, and retry. Another app or macOS may reserve this combination."
        }
    }

    private func pressed() {
        guard active else { return }
        shortcut.press()
        shortcutMessage = "Press received. Release the shortcut to finish the check."
    }

    private func released() {
        guard active else { return }
        shortcut.release()
        if shortcut.passed {
            timeoutTask?.cancel()
            shortcutMessage = "Passed — the global shortcut listener received both press and release."
        }
    }

    func testMicrophone() {
        guard active, !microphoneRunning else { return }
        microphoneRunning = true
        signalDetected = false
        level = 0
        microphoneMessage = "Checking access…"
        let token = generation
        microphoneTask = Task { @MainActor in
            let allowed = await Permissions.requestMicrophone()
            guard active, token == generation, !Task.isCancelled else { return }
            guard allowed else {
                microphoneMessage = "Microphone access is off. Open Microphone settings and allow Chit-Chat."
                microphoneRunning = false
                return
            }
            let capture = AudioCapture()
            self.capture = capture
            defer {
                capture.stop()
                if token == generation { self.capture = nil; microphoneRunning = false; level = 0 }
            }
            do {
                guard let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16_000, channels: 1, interleaved: false) else { return }
                try capture.start(outputFormat: format, onBuffer: { _ in }, onLevel: { [weak self] value in
                    Task { @MainActor in
                        guard let self, self.active, self.microphoneRunning, self.generation == token else { return }
                        self.level = value
                        self.signalDetected = self.signalDetected || value > 0.08
                    }
                })
                microphoneMessage = "Speak for five seconds. The meter should move. Audio is discarded and never sent to AI."
                try await Task.sleep(for: .seconds(5))
                guard active, token == generation else { return }
                microphoneMessage = signalDetected
                    ? "Passed — microphone capture received an audible signal."
                    : "The microphone opened, but no audible signal was detected. Check the input device and level in macOS Sound settings, then retry."
            } catch {
                if !Task.isCancelled, token == generation { microphoneMessage = "Couldn’t capture microphone audio: \(error.localizedDescription)" }
            }
        }
    }

    func testInsertion() {
        guard active, !insertionRunning, let textView, let window = textView.window else { return }
        guard NSApp.isActive else { insertionMessage = "Bring this check window to the front and try again."; return }
        textView.string = ""
        textView.setSelectedRange(NSRange(location: 0, length: 0))
        guard window.makeFirstResponder(textView) else { insertionMessage = "Couldn’t focus the test field."; return }
        let target = SetupInsertionTarget(view: textView)
        let token = generation
        insertionRunning = true
        insertionMessage = "Pasting a sample into this test field…"
        insertionTask = Task { @MainActor in
            let result = await TextInjector.insert(Self.sample, into: target, learn: false)
            guard active, token == generation else { return }
            insertionRunning = false
            insertionMessage = result == .inserted
                ? "Passed — keyboard paste inserted the sample into the test field. Other apps may expose different text controls."
                : "Insertion wasn’t verified. Keep this window focused, check Accessibility permission, and retry."
        }
    }

    func stop() {
        generation = UUID()
        timeoutTask?.cancel()
        microphoneTask?.cancel()
        insertionTask?.cancel()
        capture?.stop()
        capture = nil
        microphoneRunning = false
        insertionRunning = false
        level = 0
        if active { controller.endSetupCheck() }
        active = false
    }

    static let sample = "Chit-Chat can insert text here."
}

/// Test the actual OS keyboard-paste path in a field owned by this window. Native
/// reads avoid an Accessibility request to our own blocked main thread.
@MainActor
private struct SetupInsertionTarget: TextInsertionTarget {
    let view: NSTextView
    var learningField: (any DictationTextTarget)? { nil }
    var isUnchanged: Bool {
        NSWorkspace.shared.frontmostApplication?.processIdentifier == ProcessInfo.processInfo.processIdentifier
            && view.window?.isKeyWindow == true && view.window?.firstResponder === view
            && view.string.isEmpty && view.selectedRange() == NSRange(location: 0, length: 0)
    }
    func containsInsertion(_ text: String) -> Bool { view.string == text }
    func paste() -> Bool { isUnchanged && TextInjector.postPaste(to: ProcessInfo.processInfo.processIdentifier) }
}
