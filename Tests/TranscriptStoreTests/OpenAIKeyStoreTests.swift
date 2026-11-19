import Foundation
import Testing
@testable import ChitChat

struct OpenAIKeyStoreTests {
    private func store() -> OpenAIKeyStore {
        OpenAIKeyStore(fileURL: FileManager.default.temporaryDirectory
            .appendingPathComponent("ChitChatKeyTests-\(UUID())/openai-api-key"))
    }

    @Test func localKeyPersistsReplacesAndRemovesWithPrivatePermissions() throws {
        let store = store(), manager = FileManager.default
        defer { try? manager.removeItem(at: store.fileURL.deletingLastPathComponent()) }
        #expect(try store.read() == nil)
        for value in ["test-only-first", "test-only-replacement"] {
            try store.save(value)
            let reloaded = OpenAIKeyStore(fileURL: store.fileURL)
            #expect(try reloaded.read() == value)
            let file = try manager.attributesOfItem(atPath: store.fileURL.path)
            let directory = try manager.attributesOfItem(atPath: store.fileURL.deletingLastPathComponent().path)
            #expect((file[.posixPermissions] as? NSNumber)?.intValue == 0o600)
            #expect((directory[.posixPermissions] as? NSNumber)?.intValue == 0o700)
        }
        try store.remove()
        #expect(try store.read() == nil)
        try store.remove()
    }

    @Test func migrationPersistsBeforeDeletingOldKeyAndRunsOnlyOnce() throws {
        let store = store(), name = "ChitChatKeyTests.\(UUID())"
        let defaults = UserDefaults(suiteName: name)!
        defer {
            defaults.removePersistentDomain(forName: name)
            try? FileManager.default.removeItem(at: store.fileURL.deletingLastPathComponent())
        }
        var reads = 0, deletes = 0
        for _ in 0..<2 {
            _ = OpenAIKeyMigration.run(store: store, defaults: defaults, readLegacy: {
                reads += 1; return "test-only-migrated"
            }, removeLegacy: { () throws -> Void in
                #expect(try store.read() == "test-only-migrated")
                deletes += 1
            })
        }
        #expect(reads == 1 && deletes == 1)
        try store.remove()
        _ = OpenAIKeyMigration.run(store: store, defaults: defaults,
            readLegacy: { Issue.record("Restored a deliberately removed key"); return nil }, removeLegacy: {})
        #expect(try store.read() == nil)
    }

    @Test func deniedMigrationNeverRetriesOrDeletesExistingCredential() {
        let store = store(), name = "ChitChatKeyTests.\(UUID())"
        let defaults = UserDefaults(suiteName: name)!
        defer { defaults.removePersistentDomain(forName: name) }
        var reads = 0
        for _ in 0..<2 {
            _ = OpenAIKeyMigration.run(store: store, defaults: defaults, readLegacy: {
                reads += 1; throw OpenAIError.missingKey
            }, removeLegacy: { Issue.record("Deleted a key without migrating it") })
        }
        #expect(reads == 1)
    }

    @MainActor @Test func settingsSaveFeedsRepeatedEnhancementsAndRemovalStopsUsingKey() async throws {
        let store = store(), name = "ChitChatKeyTests.\(UUID())"
        let defaults = UserDefaults(suiteName: name)!
        defer {
            defaults.removePersistentDomain(forName: name)
            try? FileManager.default.removeItem(at: store.fileURL.deletingLastPathComponent())
        }
        let client = OpenAIClient(transport: { request in
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer test-only-local")
            return (OpenAIClientTests.completed,
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!)
        })
        let ai = OpenAIEnhancement(defaults: defaults, client: client, keyStore: store)
        #expect(ai.saveKey(" test-only-local\n"))
        ai.enabled = true
        let reloaded = OpenAIEnhancement(defaults: defaults, client: client, keyStore: store)
        for _ in 0..<3 { #expect(try await reloaded.enhance("Draft") == "Polished text.") }
        reloaded.removeKey()
        #expect(!reloaded.enabled)
        #expect(try store.read() == nil)
        reloaded.enabled = true
        #expect(try await reloaded.enhance("Keep this") == "Keep this")
        #expect(!defaults.dictionaryRepresentation().values.contains { ($0 as? String) == "test-only-local" })
    }
}
