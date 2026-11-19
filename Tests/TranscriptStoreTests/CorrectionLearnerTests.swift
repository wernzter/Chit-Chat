import Foundation
import Testing
@testable import ChitChat

@MainActor
struct CorrectionLearnerTests {
    @MainActor
    private final class Field: DictationTextTarget {
        let before = "Before: "
        let selection = NSRange(location: 8, length: 0)
        var isFocused = true
        var value: String? = "Before: "
        var reads = 0
        func read() -> String? { reads += 1; return value }
    }

    @MainActor
    private final class Fixture {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).appendingPathComponent("dictionary.txt")
        let field = Field()
        var time: TimeInterval = 0
        lazy var store = DictionaryStore(fileURL: file, watchChanges: false)
        lazy var learner = CorrectionLearner(dictionary: store, clock: { [unowned self] in self.time })

        func finish() {
            learner.stop()
            try? FileManager.default.removeItem(at: file.deletingLastPathComponent())
        }

        func proposal() async throws {
            learner.watchInsertion("Hello Wurner.", into: field)
            field.value = "Before: Hello Wurner."
            try await nextRead()
            time = 1
            field.value = "Before: Hello Werner."
            try await nextRead()
            time = 2
            try await waitUntil { self.learner.pending != nil }
        }

        func nextRead() async throws {
            let count = field.reads
            try await waitUntil { self.field.reads > count }
        }
    }

    private static func waitUntil(_ condition: () -> Bool) async throws {
        let deadline = ContinuousClock.now.advanced(by: .seconds(3))
        while !condition(), ContinuousClock.now < deadline {
            try await Task.sleep(for: .milliseconds(20))
        }
        #expect(condition())
    }

    @Test func confirmedInsertionEditCountdownAndPersistence() async throws {
        let f = Fixture()
        defer { f.finish() }
        var visibility: [Bool] = []
        f.learner.onVisibilityChange = { visibility.append($0) }
        try await f.proposal()
        #expect(f.store.entries.isEmpty)
        #expect(f.learner.pending?.deadline == 12)
        f.time = 12
        try await Self.waitUntil { !f.store.entries.isEmpty }
        #expect(f.store.entries.first?.hear == "Wurner")
        #expect(f.store.entries.first?.write == "Werner")
        #expect(f.learner.pending == nil)
        #expect(visibility.contains(true))
        #expect(visibility.last == false)
    }

    @Test func learnsAnEditMadeWellAfterTheCompletionSound() async throws {
        let f = Fixture()
        defer { f.finish() }
        f.learner.watchInsertion("Hello Wurner.", into: f.field, duration: 120)
        f.field.value = "Before: Hello Wurner."
        try await f.nextRead()
        f.time = 60
        f.field.value = "Before: Hello Werner."
        try await f.nextRead()
        f.time = 61
        try await Self.waitUntil { f.learner.pending != nil }
        f.time = 71
        try await Self.waitUntil { !f.store.entries.isEmpty }
        #expect(f.store.corrector.apply(to: "Hello Wurner.").text == "Hello Werner.")
    }

    @Test func selectedWatchDurationStopsReadingAndIgnoresLaterEdits() async throws {
        let f = Fixture()
        defer { f.finish() }
        f.learner.watchInsertion("Hello Wurner.", into: f.field, duration: 30)
        f.field.value = "Before: Hello Wurner."
        try await f.nextRead()
        f.time = 30
        try await Self.waitUntil { !f.learner.isWatching }
        let reads = f.field.reads
        #expect(f.learner.pending == nil)
        f.field.value = "Before: Hello Werner."
        f.time = 60
        try await Task.sleep(for: .milliseconds(250))
        #expect(f.field.reads == reads)
        #expect(f.store.entries.isEmpty)
    }

    @Test func editNearExpiryRenewsWindowAndKeepsFullCancelCountdown() async throws {
        let f = Fixture()
        defer { f.finish() }
        f.learner.watchInsertion("Hello Wurner.", into: f.field, duration: 15)
        f.field.value = "Before: Hello Wurner."
        try await f.nextRead()
        f.time = 14
        f.field.value = "Before: Hello Werner."
        try await f.nextRead()
        f.time = 15
        try await Self.waitUntil { f.learner.pending != nil }
        #expect(f.learner.pending?.deadline == 25)
        f.time = 25
        try await Self.waitUntil { !f.store.entries.isEmpty }
        f.time = 29
        try await Self.waitUntil { !f.learner.isWatching }
    }

    @Test func savedHistoryEditsLearnWithoutFocusAndSurviveNewDictation() async throws {
        let f = Fixture()
        defer { f.finish() }
        let history = TranscriptStore(fileURL: f.file.deletingLastPathComponent().appendingPathComponent("runs.jsonl"),
            learnEdit: { f.learner.learnTranscriptEdit(from: $0, to: $1) })
        let run = DictationRun(date: Date(), engine: "Apple", processSeconds: 0.2, text: "Hello Wurner and Annah.")
        #expect(history.record(run))
        f.field.isFocused = false
        #expect(history.updateText(id: run.id, text: "Hello Werner and Anna."))
        f.learner.stop()
        try await Self.waitUntil { f.store.entries.count == 2 }
        #expect(f.store.corrector.apply(to: run.text).text == "Hello Werner and Anna.")
        #expect(!f.learner.isWatching)
    }

    @Test func historyLearningPreservesDisabledRules() async throws {
        let f = Fixture()
        defer { f.finish() }
        f.store.add(.init(kind: .correction, write: "Warner", hear: "Wurner", isEnabled: false))
        f.learner.learnTranscriptEdit(from: "Hello Wurner and Annah.", to: "Hello Werner and Anna.")
        try await Self.waitUntil { f.store.entries.count == 2 }
        #expect(f.store.entries.first?.write == "Warner")
        #expect(f.store.entries.first?.isEnabled == false)
        #expect(f.store.entries.last?.write == "Anna")
    }

    @Test func popupCancelDoesNotSaveOrChangeFocus() async throws {
        let f = Fixture()
        defer { f.finish() }
        try await f.proposal()
        f.learner.cancelPending()
        f.time = 30
        try await f.nextRead()
        #expect(f.field.isFocused)
        #expect(f.learner.pending == nil)
        #expect(f.store.entries.isEmpty)
    }

    @Test func focusLossAndUnreadableFieldsCancelLearning() async throws {
        for losesFocus in [true, false] {
            let f = Fixture()
            defer { f.finish() }
            try await f.proposal()
            if losesFocus { f.field.isFocused = false } else { f.field.value = nil }
            f.time = 30
            try await Self.waitUntil { f.learner.pending == nil }
            #expect(f.store.entries.isEmpty)
        }
    }

    @Test func newDictationStopsOldPendingAndStartsClean() async throws {
        let f = Fixture()
        defer { f.finish() }
        try await f.proposal()
        f.learner.stop()
        #expect(f.learner.pending == nil)
        f.time = 0
        f.field.value = f.field.before
        try await f.proposal()
        #expect(f.learner.pending?.correction.corrected == "Werner")
        f.learner.watchInsertion("Unverifiable", into: nil)
        f.time = 30
        try await Task.sleep(for: .milliseconds(250))
        #expect(f.learner.pending == nil)
        #expect(f.store.entries.isEmpty)
    }

    @Test func failedInsertionNeverLearnsSubsequentUnrelatedEdits() async throws {
        let f = Fixture()
        defer { f.finish() }
        f.learner.watchInsertion("Hello Wurner.", into: f.field)
        try await f.nextRead()
        f.time = 3
        try await f.nextRead()
        let readsWhenStopped = f.field.reads
        f.field.value = "Before: Hello Wurner."
        try await Task.sleep(for: .milliseconds(250))
        #expect(f.field.reads == readsWhenStopped)
        #expect(f.learner.pending == nil)
        #expect(f.store.entries.isEmpty)
    }
}
