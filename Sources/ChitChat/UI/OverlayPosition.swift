import Foundation

enum OverlayPosition: String, CaseIterable {
    case top, bottom
    var label: String { self == .top ? "Top" : "Bottom" }

    func origin(for size: CGSize, in visibleFrame: CGRect) -> CGPoint {
        let lowest = visibleFrame.minY
        let highest = max(lowest, visibleFrame.maxY - size.height)
        let y = self == .top ? highest - DS.Overlay.edgeInset : lowest + DS.Overlay.edgeInset
        return CGPoint(x: max(visibleFrame.minX, visibleFrame.midX - size.width / 2),
                       y: min(highest, max(lowest, y)))
    }
}
