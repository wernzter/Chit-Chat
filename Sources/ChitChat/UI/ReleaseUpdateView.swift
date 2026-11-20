import AppKit
import SwiftUI

struct ReleaseUpdateView: View {
    @State private var updates = ReleaseUpdates.shared

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Space.base) {
            if !updates.currentVersion.isEmpty {
                Text("Installed version \(updates.currentVersion)")
                    .foregroundStyle(DS.Color.inkSecondary)
            }
            HStack(spacing: DS.Space.base) {
                Button(updates.manualCheckFailed ? "Try again" : "Check for updates") {
                    Task { await updates.check() }
                }
                .disabled(updates.isChecking)

                if updates.isChecking {
                    ProgressView().controlSize(.small).accessibilityLabel("Checking for updates")
                }

                if let release = updates.latestRelease {
                    Link("Open GitHub release", destination: release.releasePage)
                }
            }
            if let status = updates.manualStatus {
                Text(status).foregroundStyle(DS.Color.inkSecondary)
            } else if let release = updates.latestRelease {
                Text("Chit-Chat \(release.tag) is available.").foregroundStyle(DS.Color.inkSecondary)
            }
        }
    }

}

@MainActor
final class ReleaseUpdatePanel: NSPanel {
    init(updates: ReleaseUpdates) {
        let size = CGSize(width: DS.Updates.width, height: DS.Updates.height)
        super.init(contentRect: CGRect(origin: .zero, size: size),
                   styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        title = "Chit-Chat update available"
        isFloatingPanel = true
        level = .floating
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        hidesOnDeactivate = false
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        contentView = UpdateNoticeHost(rootView: ReleaseNotice(updates: updates))
    }

    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    func showNotice() {
        if let screen = NSScreen.main ?? NSScreen.screens.first {
            let bounds = screen.visibleFrame
            let inset = DS.Overlay.edgeInset
            setFrameOrigin(CGPoint(x: bounds.maxX - frame.width - inset, y: bounds.minY + inset))
        }
        orderFrontRegardless()
    }
}

private final class UpdateNoticeHost: NSHostingView<ReleaseNotice> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

private struct ReleaseNotice: View {
    let updates: ReleaseUpdates

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Space.snug) {
            if let release = updates.notice {
                Text("Chit-Chat \(release.tag) is available").font(DS.Font.bodyEmphasis)
                HStack {
                    Button("Open GitHub release") {
                        if NSWorkspace.shared.open(release.releasePage) {
                            updates.dismissNotice(release)
                        }
                    }
                    Button("Dismiss") { updates.dismissNotice(release) }
                }
            }
        }
        .padding(DS.Space.roomy)
        .frame(width: DS.Updates.width, height: DS.Updates.height, alignment: .leading)
        .foregroundStyle(DS.Color.ink)
        .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.panel))
    }
}
