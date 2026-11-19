import Foundation
import Security
import LocalAuthentication

/// User-requested local storage. Kept outside the signed bundle, preferences, and repo.
struct OpenAIKeyStore {
    let fileURL: URL

    init(fileURL: URL = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        .appendingPathComponent("ChitChat/Private/openai-api-key")) {
        self.fileURL = fileURL
    }

    func read() throws -> String? {
        guard FileManager.default.fileExists(atPath: fileURL.path) else { return nil }
        return try String(contentsOf: fileURL, encoding: .utf8)
    }

    func save(_ key: String) throws {
        let manager = FileManager.default
        let directory = fileURL.deletingLastPathComponent()
        try manager.createDirectory(at: directory, withIntermediateDirectories: true,
                                    attributes: [.posixPermissions: 0o700])
        try manager.setAttributes([.posixPermissions: 0o700], ofItemAtPath: directory.path)
        // The private directory also protects the temporary file during atomic replacement.
        try Data(key.utf8).write(to: fileURL, options: .atomic)
        try manager.setAttributes([.posixPermissions: 0o600], ofItemAtPath: fileURL.path)
    }

    func remove() throws {
        if FileManager.default.fileExists(atPath: fileURL.path) {
            try FileManager.default.removeItem(at: fileURL)
        }
    }
}

/// Only called once at app startup. Normal key reads/saves/deletes are file-only.
enum OpenAIKeyMigration {
    static let attemptedKey = "openAI.localKeyMigrationAttempted"

    static func run(store: OpenAIKeyStore, defaults: UserDefaults,
                    readLegacy: () throws -> String? = LegacyOpenAIKeychain.readWithoutPrompt,
                    removeLegacy: () throws -> Void = LegacyOpenAIKeychain.removeWithoutPrompt) -> String? {
        guard !defaults.bool(forKey: attemptedKey) else { return nil }
        // A denied migration must never cause another access request at the next launch.
        defaults.set(true, forKey: attemptedKey)
        do {
            if try store.read() != nil { return nil }
            guard let key = try readLegacy(), !key.isEmpty else { return nil }
            try store.save(key)
            try removeLegacy()
            return "Your API key was moved to local app storage. No Keychain password is needed."
        } catch {
            if (try? store.read()) != nil {
                return "Your API key is saved locally. The unused old Keychain entry could not be removed."
            }
            return "Paste your API key here once to use local storage. macOS did not allow a password-free transfer of the old key."
        }
    }
}

/// Legacy access is noninteractive and restricted to this app's old API-key item.
enum LegacyOpenAIKeychain {
    private static var query: [String: Any] {
        let context = LAContext()
        context.interactionNotAllowed = true
        return [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: AppStorage.legacyBundleID + ".openai",
         kSecAttrAccount as String: "api-key",
         kSecUseAuthenticationContext as String: context]
    }

    static func readWithoutPrompt() throws -> String? {
        var request = query
        request[kSecReturnData as String] = true
        request[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = try withoutInteraction { SecItemCopyMatching(request as CFDictionary, &result) }
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw OpenAIError.missingKey }
        return (result as? Data).flatMap { String(data: $0, encoding: .utf8) }
    }

    static func removeWithoutPrompt() throws {
        let status = try withoutInteraction { SecItemDelete(query as CFDictionary) }
        guard status == errSecSuccess || status == errSecItemNotFound else { throw OpenAIError.missingKey }
    }

    private static func withoutInteraction<T>(_ operation: () -> T) throws -> T {
        // The old item lives in the login keychain. LAContext alone only controls
        // modern authentication; legacy ACL/password dialogs need this legacy switch.
        var allowed: DarwinBoolean = true
        guard SecKeychainGetUserInteractionAllowed(&allowed) == errSecSuccess,
              SecKeychainSetUserInteractionAllowed(false) == errSecSuccess else { throw OpenAIError.missingKey }
        defer { SecKeychainSetUserInteractionAllowed(allowed.boolValue) }
        return operation()
    }

}
