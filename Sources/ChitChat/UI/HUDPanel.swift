import AppKit
import SwiftUI

@MainActor
final class HUDPanel: NSPanel {
    private var requestedVisible = false

    override var canBecomeMain: Bool { false }
    override var canBecomeKey: Bool { false }

    init(controller: DictationController) {
        let size = CGSize(width: DS.ListeningIndicator.width, height: DS.ListeningIndicator.height)
        super.init(contentRect: CGRect(origin: .zero, size: size),
                   styleMask: [.nonactivatingPanel, .borderless], backing: .buffered, defer: false)

        contentView = NSHostingView(rootView: HUDView(controller: controller))
        backgroundColor = .clear
        isOpaque = false
        hasShadow = false
        ignoresMouseEvents = true
        hidesOnDeactivate = false
        isFloatingPanel = true
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
    }

    func reposition() {
        guard let display = NSScreen.main ?? NSScreen.screens.first else { return }
        let position = Settings.shared.listeningIndicatorPosition
        setFrameOrigin(position.origin(for: frame.size, in: display.visibleFrame))
    }

    func present() {
        reposition()
        guard !requestedVisible else { return }
        requestedVisible = true
        if !isVisible {
            alphaValue = 0
            orderFrontRegardless()
        }
        NSAnimationContext.runAnimationGroup { animation in
            animation.duration = DS.Overlay.fadeDuration
            animator().alphaValue = 1
        }
    }

    func dismiss() {
        guard requestedVisible else { return }
        requestedVisible = false
        NSAnimationContext.runAnimationGroup { animation in
            animation.duration = DS.Overlay.fadeDuration
            animator().alphaValue = 0
        } completionHandler: { [weak self] in
            Task { @MainActor [weak self] in
                guard let self, !requestedVisible else { return }
                orderOut(nil)
            }
        }
    }
}
