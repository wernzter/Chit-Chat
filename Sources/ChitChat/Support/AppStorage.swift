import Foundation

/// Current storage plus the exact old identifiers needed to upgrade existing installs.
/// Keep legacy names here only; they are lookup keys, never new storage destinations.
enum AppStorage {
    static let bundleID = "ai.pivotstudio.chit-chat"
    static let legacyBundleID = "ai.pivotstudio.murmur-youtube"
    static let legacyDirectory = "MurmurYouTube"
    static let preferencesMigrationKey = "chitChat.identityMigrationCompleted"
    static var supportDirectory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
    }

    /// Run before any settings singleton is constructed. Existing new-domain values win.
    static func migratePreferences(defaults: UserDefaults = .standard,
                                   destinationDomain: String = bundleID,
                                   sourceDomain: String = legacyBundleID) {
        var current = defaults.persistentDomain(forName: destinationDomain) ?? [:]
        guard current[preferencesMigrationKey] as? Bool != true else { return }
        let previous = defaults.persistentDomain(forName: sourceDomain) ?? [:]
        current = previous.merging(current) { _, new in new }
        current[preferencesMigrationKey] = true
        defaults.setPersistentDomain(current, forName: destinationDomain)
    }

    /// Copy on first use, keeping the original as a backup. A failed copy continues using
    /// the old file so an upgrade cannot silently replace history or corrections with defaults.
    static func dataFile(_ name: String, root: URL = supportDirectory) -> URL {
        let manager = FileManager.default
        let destination = root.appendingPathComponent("ChitChat", isDirectory: true)
            .appendingPathComponent(name)
        let previous = root.appendingPathComponent(legacyDirectory, isDirectory: true)
            .appendingPathComponent(name)
        guard !manager.fileExists(atPath: destination.path),
              manager.fileExists(atPath: previous.path) else { return destination }
        do {
            try manager.createDirectory(at: destination.deletingLastPathComponent(),
                                        withIntermediateDirectories: true)
            try manager.copyItem(at: previous, to: destination)
            return destination
        } catch {
            Log.app.error("Could not migrate app data; retaining the previous file: \(error.localizedDescription)")
            return previous
        }
    }
}
