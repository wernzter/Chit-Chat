import AppKit
import SwiftUI

struct RecoveryNotice: View {
    @State private var recovery = DictationRecovery.shared
    var body: some View {
        if let item = recovery.items.last {
            VStack(alignment: .leading, spacing: DS.Space.snug) {
                Text("Dictation ready to copy").font(DS.Font.bodyEmphasis)
                Text(item.message).font(DS.Font.label).fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Copy text") { recovery.copy(item) }
                    Button("Dismiss") { recovery.dismiss(item) }
                    Spacer()
                    Text("\(recovery.items.count) waiting").font(DS.Font.label)
                }
            }
            .padding(DS.Space.roomy)
            .foregroundStyle(DS.Color.ink)
            .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.panel))
        }
    }
}

@MainActor
final class RecoveryPanel: NSPanel {
    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: DS.Setup.recoveryWidth, height: DS.Setup.recoveryHeight),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        contentView = NSHostingView(rootView: RecoveryNotice()
            .frame(width: DS.Setup.recoveryWidth, height: DS.Setup.recoveryHeight, alignment: .topLeading))
        isFloatingPanel = true
        level = .statusBar
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        hidesOnDeactivate = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
    }
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
    func refresh() {
        guard !DictationRecovery.shared.items.isEmpty else { orderOut(nil); return }
        if let screen = NSScreen.main ?? NSScreen.screens.first {
            let frame = screen.visibleFrame
            setFrameOrigin(NSPoint(x: frame.maxX - DS.Setup.recoveryWidth - DS.Setup.screenInset,
                                   y: frame.maxY - DS.Setup.recoveryHeight - DS.Setup.screenInset))
        }
        orderFrontRegardless()
    }
}
