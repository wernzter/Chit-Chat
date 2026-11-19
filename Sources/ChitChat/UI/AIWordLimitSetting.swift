import SwiftUI

/// One shared preference shown beside both AI features.
struct AIWordLimitSetting: View {
    @State private var settings = Settings.shared

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Space.snug) {
            LabeledContent("Minimum words for AI") {
                TextField("Word count", value: $settings.minimumAIWordCount, format: .number.grouping(.never))
                    .textFieldStyle(.roundedBorder)
                    .frame(width: DS.Preferences.wordCountFieldWidth)
                    .accessibilityLabel("Minimum words for AI")
            }
            Text("Shorter dictations skip Apple AI cleanup and OpenAI enhancement. Basic local cleanup and dictionary corrections still apply. Counted before cleanup; set 0 to remove the minimum.")
                .foregroundStyle(DS.Color.inkSecondary)
                .fixedSize(horizontal: false, vertical: true)
        }
    }
}
