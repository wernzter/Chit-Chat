import ChitChatDictionary
import Foundation
import Observation

@MainActor
@Observable
final class DictionaryStore {
    static let shared = DictionaryStore()

    private(set) var entries: [DictionaryEntry] = []
    private(set) var revision = 0

    private let url: URL
    private let observesDisk: Bool
    private var diskText: String?
    private var monitor: DispatchSourceFileSystemObject?

    static var fileURL: URL { AppStorage.dataFile("dictionary.txt") }

    init(fileURL: URL = DictionaryStore.fileURL, watchChanges: Bool = true) {
        url = fileURL
        observesDisk = watchChanges
        reloadFromDisk()
        attachMonitor()
    }

    var corrector: DictionaryCorrector { .init(entries: entries) }
    var biasPhrases: [String] { DictionaryCorrector.biasPhrases(from: entries) }

    func canLearn(_ correction: LearnedCorrection) -> Bool {
        let candidate = correction.heard.precomposedStringWithCanonicalMapping.lowercased()
        return entries.allSatisfy {
            $0.kind != .correction || $0.hear.precomposedStringWithCanonicalMapping.lowercased() != candidate
        }
    }

    @discardableResult
    func addLearnedCorrection(_ correction: LearnedCorrection) -> Bool {
        guard canLearn(correction) else { return false }
        let rule = DictionaryEntry.correction(hear: correction.heard, write: correction.corrected)
        return persist(entries + [rule])
    }

    func add(_ entry: DictionaryEntry) { persist(entries + [entry]) }

    func update(_ entry: DictionaryEntry) {
        guard let position = entries.firstIndex(where: { $0.id == entry.id }) else { return }
        var next = entries
        next[position] = entry
        persist(next)
    }

    func delete(_ entry: DictionaryEntry) { delete(ids: [entry.id]) }

    func delete(ids: Set<UUID>) {
        persist(entries.filter { !ids.contains($0.id) })
    }

    func filtered(by query: String) -> [DictionaryEntry] {
        let needle = query.trimmingCharacters(in: .whitespacesAndNewlines)
        if needle.isEmpty { return entries }
        return entries.filter { entry in
            [entry.write, entry.hear].contains { $0.localizedStandardContains(needle) }
        }
    }

    static func parse(_ text: String) -> [DictionaryEntry] {
        text.split(whereSeparator: \.isNewline).compactMap { line in
            var record = line.trimmingCharacters(in: .whitespaces)
            let disabled = record.hasPrefix("#")
            if disabled {
                let comment = record.dropFirst().trimmingCharacters(in: .whitespaces)
                guard comment.lowercased().hasPrefix("off:") else { return nil }
                record = comment.dropFirst("off:".count).trimmingCharacters(in: .whitespaces)
            }
            guard !record.isEmpty else { return nil }
            guard let separator = record.range(of: "->") else {
                return .init(kind: .term, write: record, isEnabled: !disabled)
            }
            let source = record[..<separator.lowerBound].trimmingCharacters(in: .whitespaces)
            let target = record[separator.upperBound...].trimmingCharacters(in: .whitespaces)
            guard !source.isEmpty, !target.isEmpty else { return nil }
            return .init(kind: .correction, write: target, hear: source, isEnabled: !disabled)
        }
    }

    private func reloadFromDisk() {
        let latest = try? String(contentsOf: url, encoding: .utf8)
        guard latest != diskText else { return }
        diskText = latest
        replaceEntries(latest.map(Self.parse) ?? [])
    }

    private func replaceEntries(_ new: [DictionaryEntry]) {
        entries = new
        revision += 1
    }

    @discardableResult
    private func persist(_ new: [DictionaryEntry]) -> Bool {
        let lines = ["# Chit-Chat dictionary", "# Enter a term or a misheard phrase -> preferred spelling.",
                     "# Start a rule with # off: to disable it.", ""]
            + new.map(\.fileLine) + [""]
        let contents = lines.joined(separator: "\n")
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            try contents.write(to: url, atomically: true, encoding: .utf8)
            diskText = contents
            replaceEntries(new)
            attachMonitor()
            return true
        } catch {
            Log.app.error("Dictionary write failed: \(error.localizedDescription)")
            return false
        }
    }

    private func attachMonitor() {
        guard observesDisk else { return }
        monitor?.cancel()
        monitor = nil
        let target = FileManager.default.fileExists(atPath: url.path)
            ? url : url.deletingLastPathComponent()
        let fd = open(target.path, O_EVTONLY)
        guard fd >= 0 else { return }

        let source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd,
            eventMask: [.write, .extend, .rename, .delete], queue: .main)
        source.setEventHandler { [weak self] in
            self?.reloadFromDisk()
            self?.attachMonitor()
        }
        source.setCancelHandler { close(fd) }
        monitor = source
        source.activate()
    }

    isolated deinit { monitor?.cancel() }
}
