import Foundation
import Testing
@testable import ChitChat

struct ReleaseUpdateTests {
    @Test func comparesStableVersionsNumericallyAndUsesTheOfficialReleasePage() throws {
        let newest = try ReleaseCheck.result(data: release("v0.10.0"), statusCode: 200, currentVersion: "0.9.9")
        guard case let .updateAvailable(update) = newest else {
            Issue.record("Expected a newer stable release.")
            return
        }
        #expect(update.tag == "v0.10.0")
        #expect(update.releasePage.absoluteString == "https://github.com/wernzter/Chit-Chat/releases/tag/v0.10.0")
        #expect(try ReleaseCheck.result(data: release("v0.2.0"), statusCode: 200, currentVersion: "0.2.0") == .upToDate)
        #expect(try ReleaseCheck.result(data: release("0.1.9"), statusCode: 200, currentVersion: "0.2.0") == .upToDate)
    }

    @Test(arguments: ["v1.2.3-rc.1", "v1.2..3", "v1.2.3/../../", "1.2.３", "1.2.18446744073709551616"])
    func rejectsNonStableOrMalformedTags(_ tag: String) {
        #expect(throws: (any Error).self) {
            try ReleaseCheck.result(data: release(tag), statusCode: 200, currentVersion: "0.2.0")
        }
    }

    @Test func handlesMissingReleaseAndRejectsDraftsPrereleasesAndServiceErrors() throws {
        #expect(try ReleaseCheck.result(data: Data(), statusCode: 404, currentVersion: "0.2.0") == .noRelease)
        for payload in [release("v1.0.0", draft: true), release("v1.0.0", prerelease: true)] {
            #expect(throws: (any Error).self) {
                try ReleaseCheck.result(data: payload, statusCode: 200, currentVersion: "0.2.0")
            }
        }
        #expect(throws: (any Error).self) {
            try ReleaseCheck.result(data: release("v1.0.0"), statusCode: 403, currentVersion: "0.2.0")
        }
        #expect(throws: (any Error).self) {
            try ReleaseCheck.result(data: release("v1.0.0"), statusCode: 200, currentVersion: "")
        }
    }

    @MainActor
    @Test func automaticChecksAreSilentOnFailureAndManualChecksCanRetry() async {
        let updates = ReleaseUpdates(currentVersion: "0.2.0", checkRelease: { _ in
            throw URLError(.notConnectedToInternet)
        })
        await updates.check(manually: false)
        #expect(updates.notice == nil)
        #expect(updates.manualStatus == nil)
        #expect(!updates.manualCheckFailed)

        await updates.check()
        #expect(updates.manualCheckFailed)
        #expect(updates.manualStatus != nil)
        #expect(!updates.isChecking)
    }

    @MainActor
    @Test func dismissingAnAutomaticNoticeKeepsTheReleaseAvailableWithoutRepeatAlerts() async throws {
        let suite = "ChitChat.ReleaseUpdateTests.\(UUID())"
        let preferences = try #require(UserDefaults(suiteName: suite))
        defer { preferences.removePersistentDomain(forName: suite) }

        let update = ReleaseUpdate(tag: "v0.3.0")
        let first = ReleaseUpdates(currentVersion: "0.2.0", preferences: preferences,
                                   checkRelease: { _ in .updateAvailable(update) })
        await first.check(manually: false)
        #expect(first.notice == update)
        #expect(first.manualStatus == nil)
        first.dismissNotice(update)
        #expect(first.notice == nil)
        #expect(first.latestRelease == update)

        let relaunched = ReleaseUpdates(currentVersion: "0.2.0", preferences: preferences,
                                        checkRelease: { _ in .updateAvailable(update) })
        await relaunched.check(manually: false)
        #expect(relaunched.notice == nil)
        #expect(relaunched.latestRelease == update)
    }

    private func release(_ tag: String, draft: Bool = false, prerelease: Bool = false) -> Data {
        try! JSONSerialization.data(withJSONObject: [
            "tag_name": tag, "draft": draft, "prerelease": prerelease, "html_url": "https://example.invalid/download"
        ])
    }
}
