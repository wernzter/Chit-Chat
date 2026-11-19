import AppKit
import ApplicationServices

enum InsertionResult: Equatable {
    case inserted, retained, submitted
    var requiresRecovery: Bool { self == .retained }
    var message: String {
        switch self {
        case .inserted: "Text insertion verified."
        case .retained: "The original field or cursor changed, or couldn’t be verified. Your text is ready to copy."
        case .submitted: "Paste sent. This editor’s inserted text could not be verified."
        }
    }
}

@MainActor
protocol TextInsertionTarget {
    var isUnchanged: Bool { get }
    var learningField: (any DictationTextTarget)? { get }
    func containsInsertion(_ text: String) -> Bool
    func paste() -> Bool
}

/// The app and field observed at recording start. Text/selection data is optional:
/// some editors support keyboard paste without exposing a readable AX snapshot.
@MainActor
struct InsertionFocus {
    let processID: pid_t
    let element: AXUIElement?
    let field: EditableTextField?
    var secure = false

    static func capture() -> InsertionFocus? {
        guard let app = NSWorkspace.shared.frontmostApplication else { return nil }
        let element = EditableTextField.focusedElement()
        if let element {
            var pid: pid_t = 0
            guard AXUIElementGetPid(element, &pid) == .success,
                  pid == app.processIdentifier else { return nil }
        }
        return InsertionFocus(processID: app.processIdentifier, element: element,
                              field: element.flatMap { EditableTextField.capture(element: $0) },
                              secure: element.map { EditableTextField.isSecure($0) } ?? false)
    }

    func allowsInsertion(comparedTo current: InsertionFocus?) -> Bool {
        guard let current, current.processID == processID, !secure, !current.secure else { return false }
        if let element {
            guard let focused = current.element, CFEqual(element, focused) else { return false }
        }
        // Missing AX data is a capability limitation, not evidence of a changed destination.
        guard let field, let now = current.field else { return true }
        return now.before == field.before && now.selection == field.selection
    }
}

/// Never redirect a finished dictation into another app or a known changed field.
@MainActor
struct InsertionDestination: TextInsertionTarget {
    let focus: InsertionFocus

    static func capture(allowOwnApp: Bool = false) -> InsertionDestination? {
        guard let focus = InsertionFocus.capture(), !focus.secure,
              allowOwnApp || focus.processID != ProcessInfo.processInfo.processIdentifier else { return nil }
        return InsertionDestination(focus: focus)
    }

    var isUnchanged: Bool { focus.allowsInsertion(comparedTo: InsertionFocus.capture()) }
    var learningField: (any DictationTextTarget)? { focus.field }

    func containsInsertion(_ text: String) -> Bool {
        guard let field = focus.field else { return false }
        return field.read() == field.expectedValue(inserting: text)
    }

    func paste() -> Bool {
        guard isUnchanged else { return false }
        return TextInjector.postPaste(to: focus.processID)
    }
}

@MainActor
protocol InsertionClipboard {
    func stage(_ text: String) -> Int
    func stillOwns(_ generation: Int) -> Bool
    func restore(ifOwned generation: Int)
}

@MainActor
final class SystemInsertionClipboard: InsertionClipboard {
    static let shared = SystemInsertionClipboard()
    private let pasteboard: NSPasteboard
    private var saved: [[NSPasteboard.PasteboardType: Data]] = []
    private var stagedGeneration: Int?
    init(pasteboard: NSPasteboard = .general) { self.pasteboard = pasteboard }
    func stage(_ text: String) -> Int {
        // A second dictation can arrive before the first clipboard grace period ends.
        // Preserve the user's original clipboard, never the previous temporary transcript.
        if stagedGeneration.map({ stillOwns($0) }) != true {
            saved = pasteboard.pasteboardItems?.map { item in
                Dictionary(uniqueKeysWithValues: item.types.compactMap { type in item.data(forType: type).map { (type, $0) } })
            } ?? []
        }
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)
        stagedGeneration = pasteboard.changeCount
        return pasteboard.changeCount
    }
    func stillOwns(_ generation: Int) -> Bool { pasteboard.changeCount == generation }
    func restore(ifOwned generation: Int) {
        guard stillOwns(generation) else { return }
        stagedGeneration = nil
        pasteboard.clearContents()
        let items = saved.map { values in
            let item = NSPasteboardItem()
            for (type, data) in values { item.setData(data, forType: type) }
            return item
        }
        if !items.isEmpty { pasteboard.writeObjects(items) }
    }
}

@MainActor
enum TextInjector {
    static func postPaste(to processID: pid_t) -> Bool {
        guard NSWorkspace.shared.frontmostApplication?.processIdentifier == processID,
              AXIsProcessTrusted() else { return false }
        guard let source = CGEventSource(stateID: .privateState),
              let down = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: true),
              let up = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: false) else { return false }
        down.flags = .maskCommand
        up.flags = .maskCommand
        // Use the same system keyboard-event route as a physical ⌘V. Some editors
        // do not handle process-directed events through their normal paste command.
        down.post(tap: .cghidEventTap)
        up.post(tap: .cghidEventTap)
        return true
    }
    /// Returns as soon as the paste is posted. Readback and clipboard restoration belong
    /// to the returned background task, so dictation feedback never waits on the editor.
    /// Nil means nothing was pasted and the text needs recovery.
    static func submit(_ text: String, into target: (any TextInsertionTarget)?,
                       clipboard: any InsertionClipboard = SystemInsertionClipboard.shared,
                       learn: Bool = true,
                       pause: @escaping @MainActor (Duration) async throws -> Void = { try await Task.sleep(for: $0) }) async -> Task<InsertionResult, Never>? {
        guard !Task.isCancelled, !text.isEmpty, let target, target.isUnchanged else { return nil }
        let generation = clipboard.stage(text)
        do { try await pause(.milliseconds(40)) } catch {
            clipboard.restore(ifOwned: generation)
            return nil
        }
        guard !Task.isCancelled, clipboard.stillOwns(generation), target.isUnchanged, target.paste() else {
            clipboard.restore(ifOwned: generation)
            return nil
        }
        // Start confirmation immediately, before the clipboard grace period, so quick
        // corrections are observable too. This task has its own lifetime and watch window.
        if learn { CorrectionLearner.shared.watchInsertion(text, into: target.learningField) }
        return Task { @MainActor in
            defer { clipboard.restore(ifOwned: generation) }
            // Editors can expose their AX value later than the visible paste. Never retry.
            do {
                try await pause(.milliseconds(500))
                for _ in 0..<15 {
                    if target.containsInsertion(text) { break }
                    try Task.checkCancellation()
                    try await pause(.milliseconds(100))
                }
            } catch {
                // Once posted, a paste must retain its payload even if this task is canceled.
                await Task.detached { try? await Task.sleep(for: .milliseconds(500)) }.value
                return .submitted
            }
            return target.containsInsertion(text) ? .inserted : .submitted
        }
    }

    /// Setup diagnostics explicitly need readback; normal dictation uses submit instead.
    static func insert(_ text: String, into target: (any TextInsertionTarget)?,
                       clipboard: any InsertionClipboard = SystemInsertionClipboard.shared,
                       learn: Bool = true,
                       pause: @escaping @MainActor (Duration) async throws -> Void = { try await Task.sleep(for: $0) }) async -> InsertionResult {
        guard let verification = await submit(text, into: target, clipboard: clipboard, learn: learn, pause: pause) else {
            return .retained
        }
        return await verification.value
    }
}
