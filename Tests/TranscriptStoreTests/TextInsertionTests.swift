import AppKit
import ApplicationServices
import Testing
@testable import ChitChat

@MainActor struct TextInsertionTests {
    private final class Target: TextInsertionTarget {
        private var unchanged = true
        var focus: InsertionFocus?
        var isUnchanged: Bool {
            get { unchanged && (focus.map { $0.allowsInsertion(comparedTo: $0) } ?? true) }
            set { unchanged = newValue }
        }
        var learningField: (any DictationTextTarget)? { nil }
        var pastes = 0
        var canPostPaste = true
        var inserted = false
        var exposesInsertion = true
        func containsInsertion(_ text: String) -> Bool { inserted }
        func paste() -> Bool {
            pastes += 1
            inserted = canPostPaste && exposesInsertion
            return canPostPaste
        }
    }
    private final class Clipboard: InsertionClipboard {
        var stages = 0
        var owned = true
        var restored = false
        func stage(_ text: String) -> Int { stages += 1; return 1 }
        func stillOwns(_ generation: Int) -> Bool { owned }
        func restore(ifOwned generation: Int) { if owned { restored = true } }
    }

    @Test func submissionReturnsBeforeEditorVerificationAndClipboardCleanup() async throws {
        let clipboard = Clipboard(), target = Target()
        target.exposesInsertion = false
        var resumeVerification: CheckedContinuation<Void, Never>?
        let verification = try #require(await TextInjector.submit("words", into: target,
            clipboard: clipboard, learn: false, pause: { duration in
                if duration == .milliseconds(500) {
                    await withCheckedContinuation { resumeVerification = $0 }
                }
            }))
        #expect(target.pastes == 1)
        #expect(!clipboard.restored)
        // A stalled editor is no longer on the foreground completion path.
        while resumeVerification == nil { await Task.yield() }
        target.inserted = true
        resumeVerification?.resume()
        #expect(await verification.value == .inserted)
        #expect(clipboard.restored)
        #expect(target.pastes == 1)
    }

    @Test func overlappingPastesRestoreOriginalClipboardInsteadOfPreviousTranscript() {
        let pasteboard = NSPasteboard.withUniqueName()
        defer { pasteboard.releaseGlobally() }
        pasteboard.setString("original user copy", forType: .string)
        let clipboard = SystemInsertionClipboard(pasteboard: pasteboard)
        let first = clipboard.stage("first transcript")
        let second = clipboard.stage("second transcript")
        clipboard.restore(ifOwned: first)
        #expect(pasteboard.string(forType: .string) == "second transcript")
        clipboard.restore(ifOwned: second)
        #expect(pasteboard.string(forType: .string) == "original user copy")
    }

    @Test func unreadableEditorStillAllowsPasteIntoOriginalAppAndField() {
        let element = AXUIElementCreateApplication(123)
        let unreadable = InsertionFocus(processID: 123, element: element, field: nil)
        #expect(unreadable.allowsInsertion(comparedTo: unreadable))
        let appOnly = InsertionFocus(processID: 123, element: nil, field: nil)
        #expect(appOnly.allowsInsertion(comparedTo: appOnly))
    }

    @Test func destinationStillRejectsKnownAppFieldCaretAndTextChanges() {
        let element = AXUIElementCreateApplication(123)
        let original = InsertionFocus(processID: 123, element: element,
            field: EditableTextField(element: element, before: "draft", selection: NSRange(location: 5, length: 0)))
        #expect(original.allowsInsertion(comparedTo: original))
        #expect(!original.allowsInsertion(comparedTo: nil))
        #expect(!original.allowsInsertion(comparedTo: InsertionFocus(processID: 456, element: nil, field: nil)))
        #expect(!original.allowsInsertion(comparedTo: InsertionFocus(processID: 123,
            element: AXUIElementCreateApplication(456), field: nil)))
        #expect(!original.allowsInsertion(comparedTo: InsertionFocus(processID: 123, element: element,
            field: EditableTextField(element: element, before: "draft", selection: NSRange(location: 0, length: 0)))))
        #expect(!original.allowsInsertion(comparedTo: InsertionFocus(processID: 123, element: element,
            field: EditableTextField(element: element, before: "other", selection: NSRange(location: 5, length: 0)))))
        #expect(!original.allowsInsertion(comparedTo: InsertionFocus(processID: 123, element: element, field: nil, secure: true)))
        // The same control can stop exposing its snapshot without changing focus.
        #expect(original.allowsInsertion(comparedTo: InsertionFocus(processID: 123, element: element, field: nil)))
    }

    @Test func unobservablePasteDoesNotShowRecoveryAfterEveryMessage() async {
        let clipboard = Clipboard(), target = Target()
        target.focus = InsertionFocus(processID: 123, element: AXUIElementCreateApplication(123), field: nil)
        target.exposesInsertion = false // The AX reader never reflects the editor's value.
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false, pause: { _ in })
        #expect(!result.requiresRecovery)
        #expect(target.pastes == 1)
        #expect(clipboard.restored)
    }

    @Test func missingOrChangedDestinationNeverWritesOrStagesClipboard() async {
        let clipboard = Clipboard(), target = Target()
        let missing = await TextInjector.insert("words", into: nil, clipboard: clipboard, learn: false)
        #expect(missing == .retained)
        target.isUnchanged = false
        let changed = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false)
        #expect(changed == .retained)
        #expect(target.pastes == 0)
        #expect(clipboard.stages == 0)
    }

    @Test func focusOrCaretChangeDuringPasteDelayRetainsText() async {
        let clipboard = Clipboard(), target = Target()
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false,
            pause: { _ in target.isUnchanged = false })
        #expect(result == .retained)
        #expect(target.pastes == 0)
        #expect(clipboard.restored)
    }

    @Test func unableToPostPasteStillOffersRecovery() async {
        let clipboard = Clipboard(), target = Target()
        target.canPostPaste = false
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false, pause: { _ in })
        #expect(result.requiresRecovery)
        #expect(clipboard.restored)
    }

    @Test func newClipboardContentIsNeverPastedOrOverwritten() async {
        let clipboard = Clipboard(), target = Target()
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false,
            pause: { _ in clipboard.owned = false })
        #expect(result == .retained)
        #expect(target.pastes == 0)
        #expect(!clipboard.restored)
    }

    @Test func successfulPasteIsVerifiedAndRestoresClipboard() async {
        let clipboard = Clipboard(), target = Target()
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false, pause: { _ in })
        #expect(result == .inserted)
        #expect(target.pastes == 1)
        #expect(clipboard.restored)
    }

    @Test func unverifiedPasteIsDistinguishedFromConfirmedInsertion() async {
        let clipboard = Clipboard(), target = Target()
        target.exposesInsertion = false
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false, pause: { _ in })
        #expect(result == .submitted)
        #expect(target.pastes == 1)
    }

    @Test func delayedEditorUpdateDoesNotRaiseFalseInsertionWarning() async {
        let clipboard = Clipboard(), target = Target()
        target.exposesInsertion = false
        var elapsed: Duration = .zero
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false,
            pause: { duration in
                elapsed += duration
                if elapsed >= .milliseconds(800) { target.inserted = true }
            })
        #expect(result == .inserted)
        #expect(target.pastes == 1)
        #expect(clipboard.restored)
    }

    @Test func cancellationBeforePostingNeverPastes() async {
        let clipboard = Clipboard(), target = Target()
        let result = await TextInjector.insert("words", into: target, clipboard: clipboard, learn: false,
            pause: { _ in throw CancellationError() })
        #expect(result == .retained)
        #expect(target.pastes == 0)
        #expect(clipboard.restored)
    }

    @Test func clipboardRestoresEmptyAndRichContentsButPreservesNewCopies() {
        let pasteboard = NSPasteboard.withUniqueName()
        defer { pasteboard.releaseGlobally() }
        let clipboard = SystemInsertionClipboard(pasteboard: pasteboard)
        pasteboard.clearContents()
        let empty = clipboard.stage("temporary")
        clipboard.restore(ifOwned: empty)
        #expect(pasteboard.string(forType: .string) == nil)
        pasteboard.setString("previous", forType: .string)
        pasteboard.setData(Data([1, 2, 3]), forType: .rtf)
        let rich = clipboard.stage("temporary")
        clipboard.restore(ifOwned: rich)
        #expect(pasteboard.string(forType: .string) == "previous")
        #expect(pasteboard.data(forType: .rtf) == Data([1, 2, 3]))
        let last = clipboard.stage("temporary")
        pasteboard.clearContents()
        pasteboard.setString("new user copy", forType: .string)
        clipboard.restore(ifOwned: last)
        #expect(pasteboard.string(forType: .string) == "new user copy")
    }

    @Test func recoveryKeepsMultipleDictationsUntilIndividuallyDismissed() {
        let recovery = DictationRecovery()
        recovery.retain("first", result: .retained)
        recovery.retain("second", result: .retained)
        let first = recovery.items[0]
        recovery.dismiss(first)
        #expect(recovery.items.map(\.text) == ["second"])
        #expect(recovery.items[0].message.contains("ready to copy"))
    }

    @Test func shortcutCheckRequiresBothRealEdgesAndCanReset() {
        var check = ShortcutCheckProgress()
        check.release()
        #expect(!check.passed)
        check.press()
        #expect(!check.passed)
        check.release()
        #expect(check.passed)
        check = ShortcutCheckProgress()
        #expect(!check.pressed && !check.passed)
    }
}
