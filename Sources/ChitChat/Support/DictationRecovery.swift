import AppKit
import Observation

/// Final text is also stored in transcription history. Recovery stays visible
/// until explicitly copied or dismissed, without stealing keyboard focus.
@MainActor @Observable
final class DictationRecovery {
    struct Item: Identifiable {
        let id = UUID()
        let text: String
        let message: String
    }
    static let shared = DictationRecovery()
    private(set) var items: [Item] = []
    func retain(_ text: String, result: InsertionResult) {
        items.append(Item(text: text, message: result.message))
    }
    func copy(_ item: Item) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(item.text, forType: .string)
        dismiss(item)
    }
    func dismiss(_ item: Item) { items.removeAll { $0.id == item.id } }
}
