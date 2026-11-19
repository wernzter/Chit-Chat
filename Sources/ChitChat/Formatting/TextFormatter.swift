import Foundation

protocol TextFormatter: Sendable {
    func format(_ raw: String) async -> String
}

struct RuleBasedFormatter: TextFormatter {
    private static let edits: [(pattern: String, template: String)] = [
        (#"(?i)(?<![\w'])(?:uhm|erm|hmm|mhm|um|uh)\b,?"#, ""),
        (#"(?i)\bnew paragraph\b"#, "\n\n"),
        (#"(?i)\bnew line\b"#, "\n"),
        (#"(?i)\bopen paren\b"#, " ("),
        (#"(?i)\bclose paren\b"#, ") "),
        (#"[ \t]+"#, " "),
        (#" +([,.!?;:])"#, "$1"),
        (#"\n{3,}"#, "\n\n")
    ]

    func format(_ raw: String) async -> String {
        let draft = Self.edits.reduce(raw.trimmingCharacters(in: .whitespacesAndNewlines)) { text, edit in
            text.replacingOccurrences(of: edit.pattern, with: edit.template, options: .regularExpression)
        }.trimmingCharacters(in: .whitespacesAndNewlines)

        var readyForSentence = true
        var output = String()
        output.reserveCapacity(draft.utf8.count)
        for glyph in draft {
            if glyph.isLetter, readyForSentence {
                output += glyph.uppercased()
                readyForSentence = false
            } else {
                output.append(glyph)
            }
            if ".!?\n".contains(glyph) { readyForSentence = true }
        }
        if let ending = output.last, ending.isLetter || ending.isNumber {
            output.append(".")
        }
        return output
    }
}
