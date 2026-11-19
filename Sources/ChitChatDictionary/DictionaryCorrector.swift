import Foundation

public struct AppliedCorrection: Codable, Hashable, Sendable {
    public let from: String
    public let to: String
    public let count: Int
}

public struct DictionaryCorrector: Sendable {
    public static let biasLimit = 40

    private let substitutions: [Substitution]

    public init(entries: [DictionaryEntry]) {
        substitutions = entries.enumerated().compactMap { index, entry in
            Substitution(entry, order: index)
        }.sorted {
            ($0.priority, -$0.order) > ($1.priority, -$1.order)
        }
    }

    public var isEmpty: Bool { substitutions.isEmpty }

    public func apply(to text: String) -> (text: String, applied: [AppliedCorrection]) {
        if substitutions.isEmpty { return (text, []) }

        var rewritten = text.precomposedStringWithCanonicalMapping
        var applied: [AppliedCorrection] = []
        for substitution in substitutions {
            if let result = substitution.replace(in: rewritten) {
                rewritten = result.text
                applied.append(result.correction)
            }
        }
        return (rewritten, applied)
    }

    public static func biasPhrases(from entries: [DictionaryEntry]) -> [String] {
        var keys = Set<String>()
        var hints: [String] = []
        for entry in entries where entry.isEnabled {
            let spelling = entry.write.trimmingCharacters(in: .whitespacesAndNewlines)
            if !spelling.isEmpty, keys.insert(spelling.lowercased()).inserted {
                hints.append(spelling)
                if hints.count >= biasLimit { break }
            }
        }
        return hints
    }
}

private struct Substitution: Sendable {
    let expression: NSRegularExpression
    let replacement: String
    let priority: Int
    let order: Int

    init?(_ entry: DictionaryEntry, order: Int) {
        guard entry.kind == .correction, entry.isEnabled else { return nil }
        let trigger = entry.hear.precomposedStringWithCanonicalMapping
            .trimmingCharacters(in: .whitespacesAndNewlines)
        let words = trigger.split(whereSeparator: { " -\t".contains($0) })
        guard !words.isEmpty else { return nil }

        let phrase = words.map { NSRegularExpression.escapedPattern(for: String($0)) }
            .joined(separator: #"[\s-]*"#)
        let flank = #"[\p{L}\p{N}]"#
        guard let expression = try? NSRegularExpression(
            pattern: "(?<!\(flank))\(phrase)(?!\(flank))", options: .caseInsensitive
        ) else { return nil }

        self.expression = expression
        replacement = entry.write
        priority = entry.hear.count
        self.order = order
    }

    func replace(in input: String) -> (text: String, correction: AppliedCorrection)? {
        let source = input as NSString
        let hits = expression.matches(in: input, range: NSRange(location: 0, length: source.length))
        guard let first = hits.first else { return nil }

        // Copy spans around the matches so replacement text is always literal.
        var position = 0
        var result = ""
        for hit in hits {
            result += source.substring(with: NSRange(location: position, length: hit.range.location - position))
            result += replacement
            position = NSMaxRange(hit.range)
        }
        result += source.substring(from: position)

        return (result, AppliedCorrection(from: source.substring(with: first.range),
                                          to: replacement, count: hits.count))
    }
}
