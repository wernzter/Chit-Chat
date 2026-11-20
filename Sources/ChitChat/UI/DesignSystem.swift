import SwiftUI

/// Shared tokens for a clean, modern macOS interface. Layout values live here.
enum DS {
    enum Layout {
        static let windowWidth: CGFloat = 1060
        static let windowHeight: CGFloat = 760
        static let minWidth: CGFloat = 880
        static let minHeight: CGFloat = 640
        static let sidebarWidth: CGFloat = 212
        static let settingsContentWidth: CGFloat = 800
        static let brandIconSize: CGFloat = 36
        static let navigationIconWidth: CGFloat = 20
        static let recordButtonWidth: CGFloat = 110
        static let editorWidth: CGFloat = 480
        static let transcriptEditorWidth: CGFloat = 640
        static let transcriptEditorHeight: CGFloat = 260
        static let rowDisabledOpacity: Double = 0.5
        static let correctionBadgeLines = 2
        static let timerInterval: TimeInterval = 1
        static let copyFeedback: Duration = .milliseconds(1400)
    }
    enum Overlay {
        static let edgeInset: CGFloat = 24
        static let fadeDuration: TimeInterval = 0.16
    }
    enum Updates {
        static let width: CGFloat = 334
        static let height: CGFloat = 106
    }
    enum ListeningIndicator {
        static let width: CGFloat = 80
        static let height: CGFloat = 36
        static let waveformWidth: CGFloat = 52
        static let waveformHeight: CGFloat = 20
        static let barCount = 9
        static let barWidth: CGFloat = 3
        static let barGap: CGFloat = 3
        static let minimumBarHeight: CGFloat = 3
        static let minimumLevel: Float = 0.04
        static let workingLevel: Float = 0.25
        static let frameInterval: TimeInterval = 1.0 / 30.0
        static let phaseOffset = 0.618
        static let waveSpeed = 6.0
        static let waveBase: CGFloat = 0.55
        static let waveAmplitude: CGFloat = 0.45
    }
    enum Setup {
        static let width: CGFloat = 660
        static let height: CGFloat = 720
        static let fieldHeight: CGFloat = 90
        static let recoveryWidth: CGFloat = 430
        static let recoveryHeight: CGFloat = 170
        static let screenInset: CGFloat = 24
    }
    enum Preferences {
        static let wordCountFieldWidth: CGFloat = 80
        static let watchSecondsFieldWidth: CGFloat = 80
        static let promptHeight: CGFloat = 180
        static let width: CGFloat = 860
        static let height: CGFloat = 700
        static let minWidth: CGFloat = 760
        static let minHeight: CGFloat = 580
        static let sidebarWidth: CGFloat = 212
        static let dictionaryHeight: CGFloat = 340
        static let iconWidth: CGFloat = 22
        static let titleFont = SwiftUI.Font.system(size: 27, weight: .bold)
        static let bodyFont = SwiftUI.Font.system(size: 13)
        static let navigationFont = SwiftUI.Font.system(size: 13, weight: .medium)
        static let refreshInterval: Duration = .seconds(3)
    }

    enum Learning {
        static let width: CGFloat = 360
        static let height: CGFloat = 96
        static let cancelDiameter: CGFloat = 36
        static let ringWidth: CGFloat = 3
        static let textLines = 2
        static let refresh: TimeInterval = 0.1
        static let cancelFont = SwiftUI.Font.system(size: 16, weight: .semibold)
        static let countdownStartAngle: Double = -90
    }


    enum Color {
        static let sidebar = face(light: 0xEEF1F6, dark: 0x111318)
        static let panel = face(light: 0xF7F8FA, dark: 0x17191F)
        static let cap = face(light: 0xFFFFFF, dark: 0x20232B)
        static let ink = face(light: 0x20242C, dark: 0xEDF0F7)
        static let inkSecondary = face(light: 0x687080, dark: 0xA0A7B7)
        static let accent = face(light: 0x3866DD, dark: 0x82A7FF)
        static let action = face(light: 0x3866DD, dark: 0x4268D8)
        static let accentSoft = face(light: 0xDCE6FC, dark: 0x283650)
        static let seam = face(light: 0xE1E5EC, dark: 0x303540)
        static let hover = face(light: 0xEBEFF6, dark: 0x2A303B)
        static let record = SwiftUI.Color(nsColor: .systemRed)
        static let success = SwiftUI.Color(nsColor: .systemGreen)
        static let deck = SwiftUI.Color(nsColor: NSColor(hex: 0x151820))
        static let inkOnDeck = SwiftUI.Color(nsColor: NSColor(hex: 0xF0F3FA))
        private static func face(light: UInt32, dark: UInt32) -> SwiftUI.Color {
            SwiftUI.Color(nsColor: NSColor(name: nil) { appearance in
                NSColor(hex: appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light)
            })
        }
    }
    enum Font {
        static let caption = SwiftUI.Font.system(size: 11)
        static let label = SwiftUI.Font.system(size: 12)
        static let body = SwiftUI.Font.system(size: 14)
        static let bodyEmphasis = SwiftUI.Font.system(size: 14, weight: .semibold)
        static let title = SwiftUI.Font.system(size: 16, weight: .semibold)
        static let brand = SwiftUI.Font.system(size: 19, weight: .bold)
        static let brandIcon = SwiftUI.Font.system(size: 19, weight: .semibold)
        static let section = SwiftUI.Font.system(size: 11, weight: .semibold)
        static let counter = SwiftUI.Font.system(size: 12, design: .monospaced).monospacedDigit()
    }
    enum Space {
        static let hair: CGFloat = 2
        static let tight: CGFloat = 4
        static let snug: CGFloat = 8
        static let base: CGFloat = 12
        static let roomy: CGFloat = 16
        static let wide: CGFloat = 24
        static let panel: CGFloat = 32
    }
    enum Radius {
        static let chip: CGFloat = 6
        static let control: CGFloat = 8
        static let panel: CGFloat = 12
        static let window: CGFloat = 14
    }
    enum Border {
        static let hairline: CGFloat = 1
    }
    enum Motion {
        static let panel = Animation.easeInOut(duration: 0.18)
    }
}

private extension NSColor {
    convenience init(hex: UInt32) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
                  green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255, alpha: 1)
    }
}
