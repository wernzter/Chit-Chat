import Foundation

struct LearnedCorrection: Hashable, Sendable {
    let heard: String
    let corrected: String
}

struct CorrectionProposal: Equatable, Sendable {
    let correction: LearnedCorrection
    let deadline: TimeInterval
}

/// Tracks only the inserted span, adjusting its location when surrounding text changes.
/// Accessibility offsets are UTF-16, so all document ranges use NSString coordinates.
struct TrackedDictation {
    let original: String
    private(set) var value: String
    private(set) var range: NSRange

    init(original: String, value: String, range: NSRange) {
        self.original = original
        self.value = value
        self.range = range
    }

    enum Change { case unchanged, surrounding, dictation, invalid }

    mutating func update(_ next: String) -> Change {
        guard !next.utf16.elementsEqual(value.utf16) else { return .unchanged }
        let old = Array(value.utf16)
        let new = Array(next.utf16)
        var prefix = 0
        while prefix < min(old.count, new.count), old[prefix] == new[prefix] { prefix += 1 }
        var suffix = 0
        while suffix < min(old.count, new.count) - prefix,
              old[old.count - 1 - suffix] == new[new.count - 1 - suffix] { suffix += 1 }
        let removed = old.count - prefix - suffix
        let added = new.count - prefix - suffix
        let end = prefix + removed
        let change: Change
        // Extending the first/last word belongs to the dictation; adding separated
        // surrounding text does not. Also retain an emptied span while a replacement
        // is being typed, since Accessibility can expose deletion and typing separately.
        let insertedText = String(decoding: new[prefix..<(prefix + added)], as: UTF16.self)
        let current = (value as NSString).substring(with: range)
        let extendsStart = prefix == range.location &&
            insertedText.last?.isLetter == true && current.first?.isLetter == true
        let extendsEnd = prefix == NSMaxRange(range) &&
            insertedText.first?.isLetter == true && current.last?.isLetter == true
        if removed == 0, prefix == range.location, range.length == 0 {
            range.length = added
            change = .dictation
        } else if removed == 0, extendsStart || extendsEnd {
            range.length += added
            change = .dictation
        } else if end <= range.location {
            range.location += added - removed
            change = .surrounding
        } else if prefix >= NSMaxRange(range) {
            change = .surrounding
        } else if prefix >= range.location, end <= NSMaxRange(range) {
            range.length += added - removed
            change = .dictation
        } else {
            return .invalid
        }
        value = next
        return change
    }

    var corrections: [LearnedCorrection] {
        let current = (value as NSString).substring(with: range)
        return WordCorrections.detect(from: original, to: current)
    }
}

enum WordCorrections {
    private static let words = try! NSRegularExpression(pattern: #"[\p{L}\p{M}\p{N}]+(?:['’\-][\p{L}\p{M}\p{N}]+)*"#)

    private static func tokens(_ text: String) -> [String] {
        let text = text.precomposedStringWithCanonicalMapping
        return words.matches(in: text, range: NSRange(location: 0, length: (text as NSString).length))
            .map { (text as NSString).substring(with: $0.range) }
    }

    static func detect(from old: String, to new: String) -> [LearnedCorrection] {
        let before = tokens(old), after = tokens(new)
        // Bound work on very long dictations. Whole-paragraph rewrites are not word corrections.
        guard !before.isEmpty, !after.isEmpty, max(before.count, after.count) <= 1_000 else { return [] }
        let differences = after.difference(from: before)
        var removed = Set<Int>(), inserted = Set<Int>()
        for change in differences {
            switch change {
            case .remove(let offset, _, _): removed.insert(offset)
            case .insert(let offset, _, _): inserted.insert(offset)
            }
        }
        guard before.count == 1 || removed.count < before.count else { return [] }
        var result: [LearnedCorrection] = []
        var i = 0, j = 0
        while i < before.count || j < after.count {
            var deleted: [String] = [], added: [String] = []
            while i < before.count, removed.contains(i) { deleted.append(before[i]); i += 1 }
            while j < after.count, inserted.contains(j) { added.append(after[j]); j += 1 }
            if deleted.count == 1, added.count == 1,
               deleted[0].count <= 64, added[0].count <= 64,
               deleted[0].contains(where: \.isLetter), added[0].contains(where: \.isLetter),
               deleted[0].filter({ $0.isLetter || $0.isNumber }) != added[0].filter({ $0.isLetter || $0.isNumber }) {
                let correction = LearnedCorrection(heard: deleted[0], corrected: added[0])
                if !result.contains(correction) { result.append(correction) }
            }
            if i < before.count, j < after.count, !removed.contains(i), !inserted.contains(j) {
                i += 1; j += 1
            } else if deleted.isEmpty, added.isEmpty {
                break
            }
        }
        // One source word edited to different spellings is ambiguous; do not choose for the user.
        let grouped = Dictionary(grouping: result, by: { $0.heard.lowercased() })
        return result.filter { grouped[$0.heard.lowercased()]?.count == 1 }
    }
}

/// Debouncing and opt-out timing are independent of Accessibility and the popup.
struct CorrectionLearningState {
    static let settleDelay: TimeInterval = 1
    static let countdown: TimeInterval = 10
    private var tracked: TrackedDictation
    private var lastEdit: TimeInterval
    private var handled: Set<LearnedCorrection> = []
    private var candidates: [LearnedCorrection] = []
    private(set) var pending: CorrectionProposal?
    private(set) var isValid = true

    init(original: String, value: String, range: NSRange, now: TimeInterval) {
        tracked = TrackedDictation(original: original, value: value, range: range)
        lastEdit = now
    }

    @discardableResult
    mutating func observe(_ text: String, now: TimeInterval) -> Bool {
        switch tracked.update(text) {
        case .invalid: isValid = false; pending = nil
        case .dictation:
            lastEdit = now
            candidates = tracked.corrections
            if let pending, !candidates.contains(pending.correction) { self.pending = nil }
            return true
        case .unchanged, .surrounding: break
        }
        return false
    }

    mutating func advance(now: TimeInterval, eligible: (LearnedCorrection) -> Bool) -> LearnedCorrection? {
        guard isValid else { return nil }
        if let pending, !eligible(pending.correction) { dismiss() }
        guard now - lastEdit >= Self.settleDelay else { return nil }
        if let pending, now >= pending.deadline {
            let correction = pending.correction
            dismiss()
            return correction
        }
        if pending == nil, let correction = candidates.first(where: {
            !handled.contains($0) && eligible($0)
        }) {
            pending = CorrectionProposal(correction: correction, deadline: now + Self.countdown)
        }
        return nil
    }

    mutating func dismiss() {
        if let pending { handled.insert(pending.correction) }
        pending = nil
    }
}
