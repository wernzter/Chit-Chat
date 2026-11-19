import Foundation

/// Modifier bits shared by NSEvent and CGEvent. Lock state is deliberately excluded.
public struct RecordingShortcut: Codable, Equatable, Sendable {
    public static let shift: UInt64 = 1 << 17
    public static let control: UInt64 = 1 << 18
    public static let option: UInt64 = 1 << 19
    public static let command: UInt64 = 1 << 20
    public static let function: UInt64 = 1 << 23
    public static let modifierMask = shift | control | option | command | function
    public static let `default` = RecordingShortcut(modifiers: control | shift)

    public let keyCode: UInt16?
    public let modifiers: UInt64
    public let keyLabel: String

    public init(keyCode: UInt16? = nil, modifiers: UInt64, keyLabel: String = "") {
        self.keyCode = keyCode
        self.modifiers = modifiers & Self.modifierMask
        self.keyLabel = keyLabel
    }

    public var isValid: Bool {
        modifiers & ~Self.modifierMask == 0 && (keyCode != nil || modifiers != 0)
    }

    public var displayName: String {
        var parts: [String] = []
        for (flag, name) in [(Self.control, "Control"), (Self.option, "Option"),
                             (Self.shift, "Shift"), (Self.command, "Command"),
                             (Self.function, "Fn")] where modifiers & flag != 0 {
            parts.append(name)
        }
        if let keyCode { parts.append(keyLabel.isEmpty ? "Key \(keyCode)" : keyLabel) }
        return parts.joined(separator: " + ")
    }
}

/// Arrow and function key events carry the Fn bit even without a physical Fn press.
/// Track physical modifier changes so F1 can be recorded and matched as F1, not Fn + F1.
public struct ShortcutModifiers: Sendable {
    private var functionDown = false
    public init() {}
    public mutating func normalize(_ flags: UInt64, modifierEvent: Bool) -> UInt64 {
        if modifierEvent { functionDown = flags & RecordingShortcut.function != 0 }
        let base = flags & RecordingShortcut.modifierMask & ~RecordingShortcut.function
        return base | (functionDown ? RecordingShortcut.function : 0)
    }
}

/// Pure event matching, used by the actual event tap and the regression tests.
public struct ShortcutMatcher: Sendable {
    public enum Kind: Sendable { case down, up, modifiers }
    public enum Edge: Equatable, Sendable { case press, release }
    public struct Result: Equatable, Sendable {
        public let edge: Edge?
        public let consume: Bool
    }

    private var pressed = false
    private var consumedKey: UInt16?
    private var modifiersState = ShortcutModifiers()
    public init() {}

    public mutating func handle(_ kind: Kind, keyCode: UInt16, flags: UInt64,
                                isRepeat: Bool = false, shortcut: RecordingShortcut) -> Result {
        let modifiers = modifiersState.normalize(flags, modifierEvent: kind == .modifiers)
        var edge: Edge?
        var consume = false

        // Balance the key-down even if a modifier was released before the main key.
        if consumedKey == keyCode && kind != .modifiers {
            consume = true
            if kind == .up { consumedKey = nil }
        }

        if pressed {
            let releasedKey = shortcut.keyCode == keyCode && kind == .up
            let releasedModifier = modifiers & shortcut.modifiers != shortcut.modifiers
            if releasedKey || releasedModifier {
                pressed = false
                edge = .release
            }
        } else if !isRepeat && shortcut.isValid {
            let matchingKey = shortcut.keyCode.map { $0 == keyCode && kind == .down }
                ?? (kind == .modifiers)
            if matchingKey && modifiers == shortcut.modifiers {
                pressed = true
                edge = .press
                if let key = shortcut.keyCode {
                    consumedKey = key
                    consume = true
                }
            }
        }
        // Modifier events always pass through so other applications never lose key-ups.
        return Result(edge: edge, consume: consume)
    }
}

/// A tap latches recording; a hold releases it. Time is supplied for deterministic tests.
public struct RecordingGesture: Sendable {
    public enum Action: Equatable, Sendable { case start, stop }
    public static let holdThreshold: TimeInterval = 0.35
    private var pressedAt: TimeInterval?
    public init() {}

    public mutating func press(at time: TimeInterval, recording: Bool, busy: Bool) -> Action? {
        guard pressedAt == nil, !busy else { return nil }
        if recording { return .stop }
        pressedAt = time
        return .start
    }

    public mutating func release(at time: TimeInterval) -> Action? {
        guard let start = pressedAt else { return nil }
        pressedAt = nil
        return time - start >= Self.holdThreshold ? .stop : nil
    }

    public mutating func reset() { pressedAt = nil }
}

/// Accumulates a modifier-only chord until all modifiers are released.
public struct ShortcutCapture: Sendable {
    private var peak: UInt64 = 0
    public init() {}
    public mutating func modifiersChanged(_ flags: UInt64) -> RecordingShortcut? {
        let flags = flags & RecordingShortcut.modifierMask
        if flags != 0 {
            peak |= flags
            return nil
        }
        guard peak != 0 else { return nil }
        defer { peak = 0 }
        return RecordingShortcut(modifiers: peak)
    }
}
