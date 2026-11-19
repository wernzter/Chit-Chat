import Foundation
import Testing
@testable import ChitChat

struct AppStorageTests {
    @Test func importsPreferencesOnceAndPreservesNewValues() {
        let source = "ChitChatMigrationSource.\(UUID())"
        let destination = "ChitChatMigrationDestination.\(UUID())"
        let defaults = UserDefaults(suiteName: destination)!
        defer {
            defaults.removePersistentDomain(forName: source)
            defaults.removePersistentDomain(forName: destination)
        }
        defaults.setPersistentDomain(["engine": "parakeet", "minimumAIWordCount": 5,
                                      "openAI.localKeyMigrationAttempted": true], forName: source)
        defaults.setPersistentDomain(["minimumAIWordCount": 8], forName: destination)
        AppStorage.migratePreferences(defaults: defaults, destinationDomain: destination, sourceDomain: source)
        #expect(defaults.string(forKey: "engine") == "parakeet")
        #expect(defaults.integer(forKey: "minimumAIWordCount") == 8)
        #expect(defaults.bool(forKey: "openAI.localKeyMigrationAttempted"))
        defaults.removeObject(forKey: "engine")
        AppStorage.migratePreferences(defaults: defaults, destinationDomain: destination, sourceDomain: source)
        #expect(defaults.object(forKey: "engine") == nil)
    }

    @Test func copiesLegacyFilesAndNeverOverwritesCurrentData() throws {
        let manager = FileManager.default
        let root = manager.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? manager.removeItem(at: root) }
        let legacy = root.appendingPathComponent(AppStorage.legacyDirectory)
        try manager.createDirectory(at: legacy, withIntermediateDirectories: true)
        for name in ["dictionary.txt", "runs.jsonl"] {
            let previous = legacy.appendingPathComponent(name)
            try Data("original".utf8).write(to: previous)
            let migrated = AppStorage.dataFile(name, root: root)
            #expect(migrated == root.appendingPathComponent("ChitChat/\(name)"))
            #expect(try Data(contentsOf: migrated) == Data("original".utf8))
            #expect(manager.fileExists(atPath: previous.path))
            try Data("current".utf8).write(to: migrated)
            #expect(try Data(contentsOf: AppStorage.dataFile(name, root: root)) == Data("current".utf8))
        }
    }

    @Test func failedMigrationKeepsOldDataAccessible() throws {
        let manager = FileManager.default
        let root = manager.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? manager.removeItem(at: root) }
        let legacy = root.appendingPathComponent(AppStorage.legacyDirectory)
        try manager.createDirectory(at: legacy, withIntermediateDirectories: true)
        let previous = legacy.appendingPathComponent("dictionary.txt")
        try Data("original".utf8).write(to: previous)
        // A file blocks creation of the destination directory.
        try Data().write(to: root.appendingPathComponent("ChitChat"))
        #expect(AppStorage.dataFile("dictionary.txt", root: root) == previous)
        #expect(try Data(contentsOf: previous) == Data("original".utf8))
    }

    @Test func freshInstallUsesCurrentDirectory() throws {
        let root = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        #expect(AppStorage.dataFile("runs.jsonl", root: root) == root.appendingPathComponent("ChitChat/runs.jsonl"))
        #expect(!FileManager.default.fileExists(atPath: root.path))
    }
}
