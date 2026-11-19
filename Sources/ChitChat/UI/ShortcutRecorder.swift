import AppKit
import ShortcutKit
import SwiftUI

struct ShortcutRecorder: View {
    @Bindable var controller: DictationController
    @State private var settings = Settings.shared
    @State private var recorder = ShortcutRecordingSession()

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Space.snug) {
            HStack(spacing: DS.Space.snug) {
                Button {
                    controller.setShortcutCapture(true)
                    recorder.start { shortcut in
                        if let shortcut { settings.shortcut = shortcut }
                        controller.setShortcutCapture(false)
                    }
                } label: {
                    Label(recorder.isRecording ? "Press your shortcut…" : settings.shortcut.displayName,
                          systemImage: "keyboard")
                        .font(DS.Font.bodyEmphasis)
                        .padding(DS.Space.tight)
                }
                .buttonStyle(.bordered)
                .disabled(controller.state.isActive || controller.isCheckingSetup || recorder.isRecording)
                .accessibilityLabel("Recording shortcut: \(settings.shortcut.displayName)")
                if recorder.isRecording {
                    Button("Cancel") { recorder.finish(nil) }
                }
            }
            Text(recorder.isRecording
                 ? "Press and release a key or combination. Escape cancels."
                 : "Click the shortcut to change it. Single keys are reserved for recording while Chit-Chat runs.")
                .font(DS.Font.label)
                .foregroundStyle(DS.Color.inkSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
        .onDisappear { recorder.finish(nil) }
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.willCloseNotification)) { _ in
            recorder.finish(nil)
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didResignActiveNotification)) { _ in
            recorder.finish(nil)
        }
    }
}

/// A local monitor captures shortcuts before menu commands or text fields handle them.
/// Global dictation is suspended for the entire capture, including cancellation.
@MainActor
@Observable
private final class ShortcutRecordingSession {
    private(set) var isRecording = false
    private var monitor: Any?
    private var capture = ShortcutCapture()
    private var modifiers = ShortcutModifiers()
    private var pending: RecordingShortcut?
    private var completion: ((RecordingShortcut?) -> Void)?

    func start(completion: @escaping (RecordingShortcut?) -> Void) {
        guard !isRecording else { return }
        self.completion = completion
        capture = ShortcutCapture()
        modifiers = ShortcutModifiers()
        pending = nil
        isRecording = true
        monitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .keyUp, .flagsChanged]) { [weak self] event in
            let consumed = MainActor.assumeIsolated { self?.handle(event) ?? false }
            return consumed ? nil : event
        }
    }

    func finish(_ shortcut: RecordingShortcut?) {
        guard isRecording else { return }
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        isRecording = false
        let callback = completion
        completion = nil
        pending = nil
        callback?(shortcut)
    }

    private func handle(_ event: NSEvent) -> Bool {
        let flags = modifiers.normalize(UInt64(event.modifierFlags.rawValue),
                                        modifierEvent: event.type == .flagsChanged)
        if event.type == .keyDown {
            guard !event.isARepeat else { return true }
            if event.keyCode == 53 { finish(nil); return true }
            pending = RecordingShortcut(keyCode: event.keyCode,
                                        modifiers: flags,
                                        keyLabel: Self.label(for: event))
            return true
        }
        if event.type == .keyUp {
            if let pending, pending.keyCode == event.keyCode { finish(pending) }
            return true
        }
        if pending == nil, let shortcut = capture.modifiersChanged(flags) {
            finish(shortcut)
        }
        return false
    }

    private static func label(for event: NSEvent) -> String {
        let names: [UInt16: String] = [36: "Return", 48: "Tab", 49: "Space", 51: "Delete",
            53: "Escape", 76: "Enter", 117: "Forward Delete", 115: "Home", 119: "End",
            116: "Page Up", 121: "Page Down", 123: "←", 124: "→", 125: "↓", 126: "↑",
            122: "F1", 120: "F2", 99: "F3", 118: "F4", 96: "F5", 97: "F6",
            98: "F7", 100: "F8", 101: "F9", 109: "F10", 103: "F11", 111: "F12",
            105: "F13", 107: "F14", 113: "F15", 106: "F16", 64: "F17", 79: "F18",
            80: "F19", 90: "F20"]
        return names[event.keyCode] ?? event.charactersIgnoringModifiers?.uppercased() ?? "Key \(event.keyCode)"
    }
}
