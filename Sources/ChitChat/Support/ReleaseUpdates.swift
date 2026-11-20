import Foundation
import Observation

struct ReleaseUpdate: Equatable, Sendable {
    let tag: String

    var releasePage: URL {
        URL(string: "https://github.com/wernzter/Chit-Chat/releases/tag")!.appending(path: tag)
    }
}

enum ReleaseCheckResult: Equatable, Sendable {
    case upToDate
    case noRelease
    case updateAvailable(ReleaseUpdate)
}

enum ReleaseCheck {
    static let endpoint = URL(string: "https://api.github.com/repos/wernzter/Chit-Chat/releases/latest")!

    static func check(currentVersion: String) async throws -> ReleaseCheckResult {
        guard let installed = Version(currentVersion) else { throw CheckError.invalidVersion }
        let config = URLSessionConfiguration.ephemeral
        config.urlCredentialStorage = nil
        config.httpCookieStorage = nil
        config.httpShouldSetCookies = false
        config.urlCache = nil
        config.timeoutIntervalForRequest = 10
        config.timeoutIntervalForResource = 12

        let session = URLSession(configuration: config, delegate: NoRedirects(), delegateQueue: nil)
        defer { session.invalidateAndCancel() }

        var request = URLRequest(url: endpoint, cachePolicy: .reloadIgnoringLocalCacheData)
        request.httpMethod = "GET"
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("Chit-Chat/\(currentVersion)", forHTTPHeaderField: "User-Agent")

        let (data, response) = try await session.data(for: request)
        guard let response = response as? HTTPURLResponse else { throw CheckError.invalidResponse }
        return try result(data: data, statusCode: response.statusCode, installed: installed)
    }

    static func result(data: Data, statusCode: Int, currentVersion: String) throws -> ReleaseCheckResult {
        guard let installed = Version(currentVersion) else { throw CheckError.invalidVersion }
        return try result(data: data, statusCode: statusCode, installed: installed)
    }

    private static func result(data: Data, statusCode: Int, installed: Version) throws -> ReleaseCheckResult {
        if statusCode == 404 { return .noRelease }
        guard statusCode == 200 else { throw CheckError.invalidResponse }

        let release = try JSONDecoder().decode(GitHubRelease.self, from: data)
        guard !release.draft, !release.prerelease, let published = Version(release.tag) else {
            throw CheckError.invalidVersion
        }
        return published > installed ? .updateAvailable(ReleaseUpdate(tag: release.tag)) : .upToDate
    }

    private struct GitHubRelease: Decodable {
        let tag: String
        let draft: Bool
        let prerelease: Bool

        private enum CodingKeys: String, CodingKey {
            case tag = "tag_name"
            case draft, prerelease
        }
    }

    private struct Version: Comparable {
        let major: UInt
        let minor: UInt
        let patch: UInt

        init?(_ spelling: String) {
            let digits = spelling.first == "v" || spelling.first == "V" ? spelling.dropFirst() : spelling[...]
            let pieces = digits.split(separator: ".", omittingEmptySubsequences: false)
            guard pieces.count == 3 else { return nil }
            let numbers = pieces.compactMap { piece -> UInt? in
                guard !piece.isEmpty, piece.utf8.allSatisfy({ (48...57).contains($0) }) else { return nil }
                return UInt(piece)
            }
            guard numbers.count == 3 else { return nil }
            (major, minor, patch) = (numbers[0], numbers[1], numbers[2])
        }

        static func < (lhs: Self, rhs: Self) -> Bool {
            (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
        }
    }

    private enum CheckError: Error {
        case invalidVersion
        case invalidResponse
    }

    private final class NoRedirects: NSObject, URLSessionTaskDelegate {
        func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                        newRequest request: URLRequest) async -> URLRequest? { nil }
    }
}

@MainActor
@Observable
final class ReleaseUpdates {
    static let shared = ReleaseUpdates()

    private static let dismissedKey = "lastDismissedReleaseUpdate"
    private let preferences: UserDefaults
    private let checkRelease: @Sendable (String) async throws -> ReleaseCheckResult
    let currentVersion: String

    private(set) var latestRelease: ReleaseUpdate?
    private(set) var notice: ReleaseUpdate?
    private(set) var isChecking = false
    private(set) var manualStatus: String?
    private(set) var manualCheckFailed = false
    @ObservationIgnored var onNoticeChange: (() -> Void)?

    init(currentVersion: String = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "",
         preferences: UserDefaults = .standard,
         checkRelease: @escaping @Sendable (String) async throws -> ReleaseCheckResult = ReleaseCheck.check) {
        self.currentVersion = currentVersion
        self.preferences = preferences
        self.checkRelease = checkRelease
    }

    func runAutomaticChecks() async {
        while !Task.isCancelled {
            await check(manually: false)
            do { try await Task.sleep(for: .seconds(86_400)) }
            catch { return }
        }
    }

    func check(manually: Bool = true) async {
        guard !isChecking else { return }
        isChecking = true
        if manually {
            manualStatus = nil
            manualCheckFailed = false
        }
        defer { isChecking = false }

        do {
            let result = try await checkRelease(currentVersion)
            try Task.checkCancellation()
            if !manually {
                manualStatus = nil
                manualCheckFailed = false
            }
            switch result {
            case .upToDate:
                if manually { manualStatus = "You’re up to date." }
                latestRelease = nil
                updateNotice(nil)
            case .noRelease:
                if manually { manualStatus = "No public release is available yet." }
                latestRelease = nil
                updateNotice(nil)
            case .updateAvailable(let release):
                if manually { manualStatus = "Chit-Chat \(release.tag) is available." }
                latestRelease = release
                let dismissed = preferences.string(forKey: Self.dismissedKey) == release.tag
                updateNotice(dismissed ? nil : release)
            }
        } catch is CancellationError {
            return
        } catch {
            if manually, !Task.isCancelled {
                manualCheckFailed = true
                manualStatus = "Couldn’t check for updates. Try again."
            }
        }
    }

    func dismissNotice(_ release: ReleaseUpdate) {
        guard notice == release else { return }
        preferences.set(release.tag, forKey: Self.dismissedKey)
        updateNotice(nil)
    }

    private func updateNotice(_ release: ReleaseUpdate?) {
        guard notice != release else { return }
        notice = release
        onNoticeChange?()
    }
}
