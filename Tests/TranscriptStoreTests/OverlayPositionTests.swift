import Foundation
import Testing
@testable import ChitChat

@MainActor struct OverlayPositionTests {
    @Test func positionsPersistIndependentlyAndDefaultToBottom() {
        let name = "ChitChatOverlayTests.\(UUID())"
        let isolated = UserDefaults(suiteName: name)!
        defer { isolated.removePersistentDomain(forName: name) }
        let settings = Settings(defaults: isolated)
        #expect(settings.listeningIndicatorPosition == .bottom)
        #expect(settings.correctionPopupPosition == .bottom)
        settings.listeningIndicatorPosition = .top
        let reloaded = Settings(defaults: isolated)
        #expect(reloaded.listeningIndicatorPosition == .top)
        #expect(reloaded.correctionPopupPosition == .bottom)
        reloaded.correctionPopupPosition = .top
        reloaded.listeningIndicatorPosition = .bottom
        let final = Settings(defaults: isolated)
        #expect(final.listeningIndicatorPosition == .bottom)
        #expect(final.correctionPopupPosition == .top)
    }

    @Test func bothOverlaysStayInsideUsableAreaOnOffsetScreens() {
        let screens = [CGRect(x: 0, y: 80, width: 1440, height: 790),
                       CGRect(x: -1920, y: -300, width: 1920, height: 1000)]
        let sizes = [CGSize(width: DS.ListeningIndicator.width, height: DS.ListeningIndicator.height),
                     CGSize(width: DS.Learning.width, height: DS.Learning.height)]
        for screen in screens {
            for size in sizes {
                let top = CGRect(origin: OverlayPosition.top.origin(for: size, in: screen), size: size)
                let bottom = CGRect(origin: OverlayPosition.bottom.origin(for: size, in: screen), size: size)
                #expect(screen.contains(top) && screen.contains(bottom))
                #expect(top.midX == screen.midX && bottom.midX == screen.midX)
                #expect(top.maxY == screen.maxY - DS.Overlay.edgeInset)
                #expect(bottom.minY == screen.minY + DS.Overlay.edgeInset)
            }
        }
    }
}
