import ChitChatDictionary
import Foundation
import Testing

struct VectorTests {
    private struct Fixture: Decodable {
        let version: Int
        let cases: [Example]
    }

    private struct Example: Decodable {
        let name: String
        let entries: [InputRule]
        let input: String
        let expected: String
        let expectedCorrections: [Hit]
    }

    private struct InputRule: Decodable {
        let kind: DictionaryEntry.Kind
        let write: String
        let hear: String?
        let isEnabled: Bool?

        var entry: DictionaryEntry {
            DictionaryEntry(kind: kind, write: write, hear: hear ?? "", isEnabled: isEnabled ?? true)
        }
    }

    private struct Hit: Decodable, Hashable {
        let to: String
        let count: Int
    }

    @Test func correctionsHonorTheSharedExamples() throws {
        let url = try #require(Bundle.module.url(forResource: "dictionary-test-vectors", withExtension: "json"))
        let examples = try JSONDecoder().decode(Fixture.self, from: Data(contentsOf: url))
        #expect(examples.version == 1)
        #expect(!examples.cases.isEmpty)

        for row in examples.cases {
            let correction = DictionaryCorrector(entries: row.entries.map(\.entry)).apply(to: row.input)
            let actual = correction.applied.map { Hit(to: $0.to, count: $0.count) }
            #expect(correction.text == row.expected, "\(row.name)")
            #expect(actual.count == row.expectedCorrections.count, "\(row.name)")
            #expect(Set(actual) == Set(row.expectedCorrections), "\(row.name)")
        }
    }

    @Test func biasLimitIncludesUniqueSpellingsInInputOrder() {
        let repeated = [DictionaryEntry.term("  Juniper  "), .term("JUNIPER")]
        let candidates = (1...100).map { DictionaryEntry.term("Phrase \($0)") }
        let actual = DictionaryCorrector.biasPhrases(from: repeated + candidates)
        let expected = ["Juniper"] + (1...39).map { "Phrase \($0)" }
        #expect(actual == expected)
    }

    @Test func biasListUsesOnlyEnabledWrittenForms() {
        let entries: [DictionaryEntry] = [
            .init(kind: .term, write: "Inactive", isEnabled: false),
            .correction(hear: "juniper lab", write: "Juniper Labs"),
            .term("  ")
        ]
        #expect(DictionaryCorrector.biasPhrases(from: entries) == ["Juniper Labs"])
    }

    @Test func singleCommonWordsGetAWarnBeforeSaving() {
        let entry = DictionaryEntry.correction(hear: "about", write: "Alma")
        #expect(!DictionaryWarning.check(entry).isEmpty)
    }

    @Test func specificMultiwordTriggersCanBeSavedWithoutAWarning() {
        let entry = DictionaryEntry.correction(hear: "kestrel shuttle", write: "Kestrel Shuttle Project")
        #expect(DictionaryWarning.check(entry).isEmpty)
    }
}
