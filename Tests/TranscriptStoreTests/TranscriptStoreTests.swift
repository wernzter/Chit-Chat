import Foundation
import ChitChatDictionary
import Testing
@testable import ChitChat

@MainActor
struct TranscriptStoreTests {
    private func fixture() throws -> URL {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        return directory.appendingPathComponent("runs.jsonl")
    }

    @Test func legacyHistoryRemainsReadableAndIndividuallyDeletable() throws {
        let file = try fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        let legacy = """
        {"date":"2026-09-06T10:00:00Z","engine":"Apple","audioSeconds":4,"processSeconds":0.2,"text":"First entry","group":"old-group"}
        {"date":"2026-09-06T10:00:01Z","engine":"Parakeet","audioSeconds":4,"processSeconds":0.1,"text":"Second entry","group":"old-group","corrections":[{"from":"chat","to":"Chit-Chat","count":1}]}
        """
        try (legacy + "\n").write(to: file, atomically: true, encoding: .utf8)
        let store = TranscriptStore(fileURL: file)
        #expect(store.runs.map(\.text) == ["First entry", "Second entry"])
        #expect(store.runs.last?.corrections?.first?.to == "Chit-Chat")
        let remainingID = try #require(store.runs.last?.id)
        #expect(store.delete(try #require(store.runs.first)))
        let reloaded = TranscriptStore(fileURL: file)
        #expect(reloaded.runs.count == 1)
        #expect(reloaded.runs.first?.id == remainingID)
        #expect(reloaded.runs.first?.text == "Second entry")
        #expect(!((try String(contentsOf: file, encoding: .utf8)).contains("\"group\"")))
    }

    @Test func recordsPersistWithStableIdentityAndClearTogether() throws {
        let file = try fixture().appendingPathComponent("nested/runs.jsonl")
        let root = file.deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
        defer { try? FileManager.default.removeItem(at: root) }
        let store = TranscriptStore(fileURL: file)
        let first = DictationRun(date: Date(), engine: "Apple", processSeconds: 0.1, text: "Hello\nworld")
        let second = DictationRun(date: Date(), engine: "Parakeet", processSeconds: 0.2, text: "Another recording")
        #expect(store.record(first))
        #expect(store.record(second))
        let reloaded = TranscriptStore(fileURL: file)
        #expect(reloaded.runs.map(\.id) == [first.id, second.id])
        #expect(reloaded.runs.first?.text == first.text)
        #expect(reloaded.delete(first))
        #expect(TranscriptStore(fileURL: file).runs.map(\.id) == [second.id])
        #expect(reloaded.clear())
        #expect(reloaded.runs.isEmpty)
        #expect(TranscriptStore(fileURL: file).runs.isEmpty)
    }

    @Test func malformedLineDoesNotHideOtherHistory() throws {
        let file = try fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        try "broken line\n".write(to: file, atomically: true, encoding: .utf8)
        let store = TranscriptStore(fileURL: file)
        #expect(store.record(DictationRun(date: Date(), engine: "Apple", processSeconds: 0, text: "Saved")))
        #expect(TranscriptStore(fileURL: file).runs.map(\.text) == ["Saved"])
    }

    @Test func historyEditsPersistMetadataAndLearnFromLatestSavedText() throws {
        let file = try fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        var learned: [(String, String)] = []
        let store = TranscriptStore(fileURL: file, learnEdit: { learned.append(($0, $1)) })
        let corrections = DictionaryCorrector(entries: [.correction(hear: "chat", write: "Chit-Chat")])
            .apply(to: "chat").applied
        let run = DictationRun(date: Date(timeIntervalSince1970: 1_000), engine: "Apple", processSeconds: 0.2,
            text: "Hello Wurner.", corrections: corrections)
        #expect(store.record(run))
        #expect(store.updateText(id: run.id, text: "Hello Werner."))
        #expect(store.updateText(id: run.id, text: "Hello Werner!"))
        #expect(learned.map { $0.0 } == ["Hello Wurner.", "Hello Werner."])
        #expect(learned.map { $0.1 } == ["Hello Werner.", "Hello Werner!"])
        let saved = try #require(TranscriptStore(fileURL: file).runs.first)
        #expect(saved.id == run.id)
        #expect(saved.date == run.date)
        #expect(saved.engine == run.engine)
        #expect(saved.processSeconds == run.processSeconds)
        #expect(saved.corrections == run.corrections)
        #expect(saved.text == "Hello Werner!")
        #expect(store.updateText(id: run.id, text: saved.text))
        #expect(!store.updateText(id: run.id, text: " \n"))
        #expect(!store.updateText(id: UUID(), text: "missing"))
        #expect(learned.count == 2)
    }

    @Test func failedHistoryEditKeepsOriginalAndDoesNotTeachDictionary() throws {
        let file = try fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        let store = TranscriptStore(fileURL: file, learnEdit: { _, _ in Issue.record("Unsaved edits must not be learned") })
        let run = DictationRun(date: Date(), engine: "Apple", processSeconds: 0.1, text: "Hello Wurner.")
        #expect(store.record(run))
        try FileManager.default.removeItem(at: file)
        try FileManager.default.createDirectory(at: file, withIntermediateDirectories: true)
        #expect(!store.updateText(id: run.id, text: "Hello Werner."))
        #expect(store.runs.first?.text == run.text)
    }

    @Test func failedWriteDoesNotCreatePhantomHistory() throws {
        let file = try fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        try Data().write(to: file)
        let store = TranscriptStore(fileURL: file.appendingPathComponent("impossible.jsonl"))
        #expect(!store.record(DictationRun(date: Date(), engine: "Apple", processSeconds: 0, text: "Unsaved")))
        #expect(store.runs.isEmpty)
    }
}
