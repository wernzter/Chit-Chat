import AppKit
import Testing
@testable import ChitChat

@MainActor struct RecoveryPanelTests {
    @Test func emptyPanelDoesNotCollapseWhenFirstNoticeArrives() async throws {
        _ = NSApplication.shared
        let recovery = DictationRecovery.shared
        let panel = RecoveryPanel()
        defer {
            panel.close()
            for item in recovery.items { recovery.dismiss(item) }
        }
        panel.contentView?.layoutSubtreeIfNeeded()
        try await Task.sleep(for: .milliseconds(100))
        recovery.retain("A sample dictation.", result: .retained)
        try await Task.sleep(for: .milliseconds(100))
        panel.contentView?.layoutSubtreeIfNeeded()
        #expect(panel.frame.width >= DS.Setup.recoveryWidth)
        #expect(panel.frame.height >= DS.Setup.recoveryHeight)
    }
}
