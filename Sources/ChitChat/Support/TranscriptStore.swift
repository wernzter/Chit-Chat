import Foundation
import ChitChatDictionary
import Observation

/// One completed dictation. Unknown fields in older history files are ignored on decode.
struct DictationRun: Codable, Sendable, Identifiable {
    let id: UUID
    let date: Date
    let engine: String
    let processSeconds: Double
    let text: String
    let corrections: [AppliedCorrection]?

    init(id: UUID = UUID(), date: Date, engine: String, processSeconds: Double,
         text: String, corrections: [AppliedCorrection]? = nil) {
        self.id = id
        self.date = date
        self.engine = engine
        self.processSeconds = processSeconds
        self.text = text
        self.corrections = corrections
    }

    init(from decoder: any Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        // Early history entries predate stable IDs and dictionary correction metadata.
        id = try values.decodeIfPresent(UUID.self, forKey: .id) ?? UUID()
        date = try values.decode(Date.self, forKey: .date)
        engine = try values.decode(String.self, forKey: .engine)
        processSeconds = try values.decode(Double.self, forKey: .processSeconds)
        text = try values.decode(String.self, forKey: .text)
        corrections = try values.decodeIfPresent([AppliedCorrection].self, forKey: .corrections)
    }
}

/// Owns history persistence. Learning only receives edits after they are safely saved.
@MainActor
@Observable
final class TranscriptStore {
    static let shared = TranscriptStore(fileURL: AppStorage.dataFile("runs.jsonl"))

    private(set) var runs: [DictationRun]
    private let fileURL: URL
    private let learnEdit: (String, String) -> Void

    init(fileURL: URL, learnEdit: @escaping (String, String) -> Void = {
        CorrectionLearner.shared.learnTranscriptEdit(from: $0, to: $1)
    }) {
        self.fileURL = fileURL
        self.learnEdit = learnEdit
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        let data = (try? Data(contentsOf: fileURL)) ?? Data()
        runs = data.split(separator: 0x0A).compactMap {
            try? decoder.decode(DictationRun.self, from: Data($0))
        }
    }

    @discardableResult
    func record(_ run: DictationRun) -> Bool {
        do {
            try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(),
                                                    withIntermediateDirectories: true)
            let line = try Self.encode(run)
            if FileManager.default.fileExists(atPath: fileURL.path) {
                let handle = try FileHandle(forWritingTo: fileURL)
                defer { try? handle.close() }
                try handle.seekToEnd()
                try handle.write(contentsOf: line)
            } else {
                try line.write(to: fileURL, options: .atomic)
            }
            runs.append(run)
            return true
        } catch {
            Log.app.error("Could not save transcription: \(error.localizedDescription)")
            return false
        }
    }

    @discardableResult
    func updateText(id: UUID, text: String) -> Bool {
        guard let index = runs.firstIndex(where: { $0.id == id }),
              !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return false }
        let previous = runs[index]
        guard previous.text != text else { return true }
        var updated = runs
        updated[index] = DictationRun(id: previous.id, date: previous.date, engine: previous.engine,
            processSeconds: previous.processSeconds, text: text, corrections: previous.corrections)
        guard replace(with: updated) else { return false }
        learnEdit(previous.text, text)
        return true
    }

    @discardableResult
    func delete(_ run: DictationRun) -> Bool {
        replace(with: runs.filter { $0.id != run.id })
    }

    @discardableResult
    func clear() -> Bool { replace(with: []) }

    private func replace(with updated: [DictationRun]) -> Bool {
        do {
            var data = Data()
            for run in updated { data.append(try Self.encode(run)) }
            try data.write(to: fileURL, options: .atomic)
            runs = updated
            return true
        } catch {
            Log.app.error("Could not update transcription history: \(error.localizedDescription)")
            return false
        }
    }

    private static func encode(_ run: DictationRun) throws -> Data {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        var line = try encoder.encode(run)
        line.append(0x0A)
        return line
    }
}
