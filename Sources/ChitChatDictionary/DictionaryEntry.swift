import Foundation

public struct DictionaryEntry: Identifiable, Codable, Hashable, Sendable {
    public enum Kind: String, Codable, Sendable {
        case term, correction
    }

    public var id: UUID
    public var kind: Kind
    public var write: String
    public var hear: String
    public var isEnabled: Bool

    public init(id: UUID = UUID(), kind: Kind, write: String, hear: String = "", isEnabled: Bool = true) {
        self.id = id
        self.kind = kind
        self.write = write
        self.hear = hear
        self.isEnabled = isEnabled
    }

    public static func term(_ spelling: String) -> Self {
        .init(kind: .term, write: spelling)
    }

    public static func correction(hear trigger: String, write replacement: String) -> Self {
        .init(kind: .correction, write: replacement, hear: trigger)
    }

    public var fileLine: String {
        let prefix = isEnabled ? "" : "# off: "
        switch kind {
        case .term: return prefix + write
        case .correction: return prefix + hear + " -> " + write
        }
    }
}

public struct DictionaryWarning: Identifiable, Sendable {
    public let message: String
    public var id: String { message }

    public static func check(_ entry: DictionaryEntry) -> [Self] {
        guard case .correction = entry.kind else { return [] }
        let trigger = entry.hear.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trigger.isEmpty else { return [] }
        let words = trigger.lowercased().split { " -".contains($0) }

        var result: [Self] = []
        if words.count == 1, let word = words.first {
            if everydayWords.contains(word) {
                result.append(.init(message: "“\(trigger)” is an ordinary word. A longer trigger will make the correction more specific."))
            } else if word.count < 4 {
                result.append(.init(message: "“\(trigger)” is a short trigger and may match frequently."))
            }
        }
        let replacement = entry.write.trimmingCharacters(in: .whitespacesAndNewlines)
        if trigger.caseInsensitiveCompare(replacement) == .orderedSame {
            result.append(.init(message: "The trigger and replacement have the same spelling."))
        }
        return result
    }

    private static let everydayWords = Set("""
        about after against again also bank before both call case change check cloud code come copy
        data date day down edit end file find first folder form from group home into item last
        left level line link list mail make move name near next note open over page part place
        point read report right save send set some start stop team test text that them then
        this time today turn under want when where which while with work write year
        """.split(whereSeparator: \.isWhitespace))
}
