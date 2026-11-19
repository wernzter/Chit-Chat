import AppKit
import ShortcutKit

@MainActor
final class HotkeyMonitor {
    var shortcut: RecordingShortcut = .default
    var onPress: (() -> Void)?
    var onRelease: (() -> Void)?
    var onCancel: (() -> Void)?

    private var tap: AttachedTap?
    private var matcher = ShortcutMatcher()

    @discardableResult
    func start() -> Bool {
        stop()
        let events: [CGEventType] = [.keyDown, .keyUp, .flagsChanged]
        let mask = events.reduce(CGEventMask.zero) { $0 | (1 << $1.rawValue) }
        let userData = Unmanaged.passUnretained(self).toOpaque()

        guard let port = CGEvent.tapCreate(tap: .cgSessionEventTap, place: .headInsertEventTap,
            options: .defaultTap, eventsOfInterest: mask, callback: Self.receive, userInfo: userData)
        else {
            Log.hotkey.error("Could not attach the system keyboard listener.")
            return false
        }
        guard let source = CFMachPortCreateRunLoopSource(nil, port, 0) else {
            CFMachPortInvalidate(port)
            return false
        }

        tap = AttachedTap(port: port, source: source)
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: port, enable: true)
        return true
    }

    func stop() {
        if let attached = tap {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), attached.source, .commonModes)
            CFMachPortInvalidate(attached.port)
        }
        tap = nil
        matcher = ShortcutMatcher()
    }

    // The event decision is synchronous. Only the main run loop owns this source.
    private nonisolated static let receive: CGEventTapCallBack = { _, type, event, data in
        guard let data, Thread.isMainThread else { return Unmanaged.passUnretained(event) }
        let listener = Unmanaged<HotkeyMonitor>.fromOpaque(data).takeUnretainedValue()
        let key = KeySample(type: type, flags: event.flags.rawValue,
                            code: UInt16(truncatingIfNeeded: event.getIntegerValueField(.keyboardEventKeycode)),
                            repeated: event.getIntegerValueField(.keyboardEventAutorepeat) != 0)
        let consume = MainActor.assumeIsolated { listener.dispatch(key) }
        return consume ? nil : Unmanaged.passUnretained(event)
    }

    private func dispatch(_ key: KeySample) -> Bool {
        let kind: ShortcutMatcher.Kind
        switch key.type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            matcher = ShortcutMatcher()
            onCancel?()
            if let port = tap?.port { CGEvent.tapEnable(tap: port, enable: true) }
            return false
        case .flagsChanged:
            kind = .modifiers
        case .keyDown:
            kind = .down
        case .keyUp:
            kind = .up
        default:
            return false
        }

        let match = matcher.handle(kind, keyCode: key.code, flags: key.flags,
                                   isRepeat: key.repeated, shortcut: shortcut)
        if match.edge == .press { onPress?() }
        if match.edge == .release { onRelease?() }
        return match.consume
    }

    private struct AttachedTap {
        let port: CFMachPort
        let source: CFRunLoopSource
    }

    private struct KeySample: Sendable {
        let type: CGEventType
        let flags: UInt64
        let code: UInt16
        let repeated: Bool
    }
}
