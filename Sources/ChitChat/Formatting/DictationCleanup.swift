import Foundation

enum DictationCleanup {
    static func wordCount(_ text: String) -> Int {
        text.split(whereSeparator: \.isWhitespace).count { token in
            token.contains { $0.isLetter || $0.isNumber }
        }
    }

    static func allowsAI(_ original: String, minimumWords: Int) -> Bool {
        wordCount(original) >= max(0, minimumWords)
    }

    static func format(_ raw: String, enabled: Bool, smartCleanup: Bool, minimumWords: Int,
                       basicFormatter: any TextFormatter = RuleBasedFormatter(),
                       smartFormatter: any TextFormatter = FoundationModelFormatter()) async -> String {
        guard enabled else { return raw }
        let formatter = smartCleanup && allowsAI(raw, minimumWords: minimumWords)
            ? smartFormatter : basicFormatter
        return await formatter.format(raw)
    }
}
