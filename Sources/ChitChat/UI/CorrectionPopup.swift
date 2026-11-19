import AppKit
import SwiftUI

/// Clickable without becoming key: cancelling must leave the user's text field focused.
@MainActor
final class CorrectionPopupPanel: NSPanel {
    init(learner: CorrectionLearner) {
        super.init(contentRect: NSRect(x: 0, y: 0, width: DS.Learning.width, height: DS.Learning.height),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        title = "Dictionary learning"
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary]
        isFloatingPanel = true
        hidesOnDeactivate = false
        isMovableByWindowBackground = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        contentView = ClickableHostingView(rootView: CorrectionPopup(learner: learner))
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    func reposition() {
        guard let screen = NSScreen.main ?? NSScreen.screens.first else { return }
        setFrameOrigin(Settings.shared.correctionPopupPosition.origin(for: frame.size, in: screen.visibleFrame))
    }

    func present() {
        reposition()
        guard !isVisible else { return }
        orderFrontRegardless()
    }
}

private final class ClickableHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

private struct CorrectionPopup: View {
    @Bindable var learner: CorrectionLearner

    var body: some View {
        TimelineView(.periodic(from: .now, by: DS.Learning.refresh)) { _ in
            let remaining = max(0, (learner.pending?.deadline ?? 0) - ProcessInfo.processInfo.systemUptime)
            HStack(spacing: DS.Space.base) {
                VStack(alignment: .leading, spacing: DS.Space.snug) {
                    if let pending = learner.pending, !learner.saveError {
                        Text("“\(pending.correction.heard)” → “\(pending.correction.corrected)”")
                            .font(DS.Font.bodyEmphasis)
                            .lineLimit(DS.Learning.textLines)
                        Text("Adding to your dictionary in \(Int(ceil(remaining)))s")
                            .font(DS.Font.label)
                            .foregroundStyle(DS.Color.inkSecondary)
                    } else if learner.saveError {
                        Text("Couldn't save this correction. Check your dictionary file and try again.")
                            .font(DS.Font.body)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                Button { learner.cancelPending() } label: {
                    ZStack {
                        Circle().stroke(DS.Color.seam, lineWidth: DS.Learning.ringWidth)
                        Circle()
                            .trim(from: 0, to: min(1, remaining / CorrectionLearningState.countdown))
                            .stroke(DS.Color.ink, style: StrokeStyle(lineWidth: DS.Learning.ringWidth, lineCap: .round))
                            .rotationEffect(.degrees(DS.Learning.countdownStartAngle))
                        Image(systemName: "xmark").font(DS.Learning.cancelFont)
                    }
                    .frame(width: DS.Learning.cancelDiameter, height: DS.Learning.cancelDiameter)
                    .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(learner.saveError ? "Dismiss dictionary error" : "Cancel dictionary addition")
                .help("Cancel — don't add this correction")
            }
            .foregroundStyle(DS.Color.ink)
            .padding(DS.Space.base)
            .frame(width: DS.Learning.width, height: DS.Learning.height)
            .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.window))
        }
    }
}
