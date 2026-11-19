import Foundation
import Testing
@testable import ChitChat

@MainActor
struct EditWatchSettingsTests {
    @Test func defaultsToTwoMinutesPersistsAndBoundsInvalidValues() throws {
        let name = "ChitChatEditWatchTests.\(UUID())"
        let defaults = try #require(UserDefaults(suiteName: name))
        defer { defaults.removePersistentDomain(forName: name) }
        let settings = Settings(defaults: defaults)
        #expect(settings.editWatchSeconds == 120)
        for seconds in [15, 45, 120, 600] {
            settings.editWatchSeconds = seconds
            #expect(Settings(defaults: defaults).editWatchSeconds == seconds)
        }
        settings.editWatchSeconds = -1
        #expect(Settings(defaults: defaults).editWatchSeconds == 15)
        settings.editWatchSeconds = 601
        #expect(Settings(defaults: defaults).editWatchSeconds == 600)
        defaults.set(0, forKey: "editWatchSeconds")
        #expect(Settings(defaults: defaults).editWatchSeconds == 15)
        defaults.set(Int.max, forKey: "editWatchSeconds")
        #expect(Settings(defaults: defaults).editWatchSeconds == 600)
    }
}
