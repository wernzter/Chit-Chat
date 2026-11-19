import Testing
@testable import ChitChat

@MainActor struct AppNavigationTests {
    @Test func settingsAndSetupUseTheSameWindowEntryPoint() {
        let navigation = AppNavigation()
        var opens = 0
        navigation.openMainWindow = { opens += 1 }
        #expect(navigation.page == .history)
        navigation.showSettings()
        #expect(navigation.page == .recording)
        navigation.select(.openAI)
        navigation.select(.history)
        navigation.showSettings()
        #expect(navigation.page == .openAI)
        navigation.showSetup()
        #expect(navigation.page == .setup)
        navigation.showSettings()
        #expect(navigation.page == .openAI)
        #expect(opens == 4)
    }
}
