import SwiftUI

struct HUDView: View {
    @Bindable var controller: DictationController

    var body: some View {
        ListeningIndicator(level: controller.level, recording: controller.state == .listening,
                           active: controller.state.isActive)
            .accessibilityLabel(status)
    }

    private var status: String {
        switch controller.state {
        case .starting: "Preparing speech engine"
        case .listening: "Listening"
        case .finishing: controller.enhancing ? "Enhancing with OpenAI" : "Transcribing"
        case .error(let message): message
        case .idle: "Dictation idle"
        }
    }
}

/// Just the level bars; no transcript preview or visible status label.
struct ListeningIndicator: View {
    let level: Float
    let recording: Bool
    let active: Bool

    var body: some View {
        TimelineView(.animation(minimumInterval: DS.ListeningIndicator.frameInterval, paused: !active)) { timeline in
            HStack(spacing: DS.ListeningIndicator.barGap) {
                ForEach(0..<DS.ListeningIndicator.barCount, id: \.self) { index in
                    Capsule()
                        .fill(recording ? DS.Color.record : DS.Color.inkOnDeck)
                        .frame(width: DS.ListeningIndicator.barWidth,
                               height: barHeight(index, at: timeline.date.timeIntervalSinceReferenceDate))
                }
            }
            .frame(width: DS.ListeningIndicator.waveformWidth, height: DS.ListeningIndicator.waveformHeight)
        }
        .frame(width: DS.ListeningIndicator.width, height: DS.ListeningIndicator.height)
        .background(DS.Color.deck, in: RoundedRectangle(cornerRadius: DS.Radius.window))
        .overlay(RoundedRectangle(cornerRadius: DS.Radius.window)
            .strokeBorder(DS.Color.seam, lineWidth: DS.Border.hairline))
        .accessibilityElement(children: .ignore)
    }

    private func barHeight(_ index: Int, at time: TimeInterval) -> CGFloat {
        guard active else { return DS.ListeningIndicator.minimumBarHeight }
        let phase = (Double(index) * DS.ListeningIndicator.phaseOffset).truncatingRemainder(dividingBy: 1)
        let wave = sin(time * DS.ListeningIndicator.waveSpeed + phase * .pi * 2)
        let amplitude = CGFloat(min(1, max(DS.ListeningIndicator.minimumLevel,
                                          recording ? level : DS.ListeningIndicator.workingLevel)))
        let scaled = amplitude * (DS.ListeningIndicator.waveBase + DS.ListeningIndicator.waveAmplitude * CGFloat(wave))
        return DS.ListeningIndicator.minimumBarHeight
            + scaled * (DS.ListeningIndicator.waveformHeight - DS.ListeningIndicator.minimumBarHeight)
    }
}
