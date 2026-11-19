import Foundation
import Testing
import ChitChatDictionary
@testable import ChitChat

struct CorrectionLearningTests {
    private let correction = LearnedCorrection(heard: "Wurner", corrected: "Werner")

    private func state(_ text: String = "Hello Wurner.") -> CorrectionLearningState {
        CorrectionLearningState(original: text, value: text,
            range: NSRange(location: 0, length: text.utf16.count), now: 0)
    }

    @Test func waitsForTypingToSettleThenGivesFullTenSeconds() {
        var sut = state()
        sut.observe("Hello Wern.", now: 1)
        #expect(sut.advance(now: 1.9, eligible: { _ in true }) == nil)
        #expect(sut.pending == nil)
        sut.observe("Hello Werner.", now: 2)
        #expect(sut.advance(now: 2.9, eligible: { _ in true }) == nil)
        #expect(sut.pending == nil)
        #expect(sut.advance(now: 3, eligible: { _ in true }) == nil)
        #expect(sut.pending == CorrectionProposal(correction: correction, deadline: 13))
        #expect(sut.advance(now: 12.99, eligible: { _ in true }) == nil)
        #expect(sut.advance(now: 13, eligible: { _ in true }) == correction)
        #expect(sut.advance(now: 30, eligible: { _ in true }) == nil)
        #expect(sut.pending == nil)
    }

    @Test func cancelPreventsSavingAndDoesNotRepeat() {
        var sut = state()
        sut.observe("Hello Werner.", now: 1)
        _ = sut.advance(now: 2, eligible: { _ in true })
        sut.dismiss()
        #expect(sut.advance(now: 20, eligible: { _ in true }) == nil)
        sut.observe("Hello Wurner.", now: 21)
        sut.observe("Hello Werner.", now: 22)
        _ = sut.advance(now: 24, eligible: { _ in true })
        #expect(sut.pending == nil)
    }

    @Test func undoOrRevisionInvalidatesPendingImmediately() {
        var sut = state()
        sut.observe("Hello Werner.", now: 1)
        _ = sut.advance(now: 2, eligible: { _ in true })
        sut.observe("Hello Wurner.", now: 3)
        #expect(sut.pending == nil)
        #expect(sut.advance(now: 20, eligible: { _ in true }) == nil)
        sut.observe("Hello Warner.", now: 21)
        _ = sut.advance(now: 22, eligible: { _ in true })
        #expect(sut.pending?.correction.corrected == "Warner")
        #expect(sut.pending?.deadline == 32)
    }

    @Test func multipleCorrectionsEachReceiveTheirOwnCountdown() {
        var sut = state("Hello Wurner and Annah.")
        sut.observe("Hello Werner and Anna.", now: 1)
        _ = sut.advance(now: 2, eligible: { _ in true })
        #expect(sut.advance(now: 12, eligible: { _ in true }) == correction)
        _ = sut.advance(now: 12.2, eligible: { _ in true })
        #expect(sut.pending?.correction == LearnedCorrection(heard: "Annah", corrected: "Anna"))
        #expect(sut.pending?.deadline == 22.2)
    }

    @Test func newExistingRuleCancelsPending() {
        var sut = state()
        sut.observe("Hello Werner.", now: 1)
        _ = sut.advance(now: 2, eligible: { _ in true })
        #expect(sut.advance(now: 12, eligible: { _ in false }) == nil)
        #expect(sut.pending == nil)
    }

    @Test func onlyTracksInsertedTextAndAdjustsUTF16Offsets() {
        let original = "Hello Wurner."
        var tracked = TrackedDictation(original: original, value: "😀 Before \(original) After",
            range: NSRange(location: "😀 Before ".utf16.count, length: original.utf16.count))
        #expect(tracked.update("😀 Longer before \(original) After") == .surrounding)
        #expect(tracked.update("😀 Longer before Hello Werner. After") == .dictation)
        #expect(tracked.corrections == [correction])
        #expect(tracked.update("😀 Longer before Hello Werner. Afterwards") == .surrounding)
        #expect(tracked.corrections == [correction])
    }

    @Test func deletingAcrossInsertionBoundaryStopsLearning() {
        var sut = CorrectionLearningState(original: "Wurner", value: "Hi Wurner!",
            range: NSRange(location: 3, length: 6), now: 0)
        sut.observe("HWerner!", now: 1)
        #expect(!sut.isValid)
        #expect(sut.advance(now: 20, eligible: { _ in true }) == nil)
    }

    @Test func supportsDeleteThenTypeAndExtendingBoundaryWords() {
        var sut = state("Wurner")
        sut.observe("", now: 1)
        sut.observe("W", now: 1.2)
        sut.observe("Werner", now: 1.4)
        _ = sut.advance(now: 2.4, eligible: { _ in true })
        #expect(sut.pending?.correction == correction)
        var tracked = TrackedDictation(original: "Mat", value: "Mat", range: NSRange(location: 0, length: 3))
        #expect(tracked.update("Matt") == .dictation)
        #expect(tracked.corrections == [LearnedCorrection(heard: "Mat", corrected: "Matt")])
        #expect(tracked.update("Matt is here") == .surrounding)
        #expect(tracked.corrections == [LearnedCorrection(heard: "Mat", corrected: "Matt")])
    }

    @Test func unicodeNormalizationDoesNotCorruptRanges() {
        var tracked = TrackedDictation(original: "Wurner", value: "Café Wurner",
            range: NSRange(location: 5, length: 6))
        #expect(tracked.update("Cafe\u{301} Wurner") == .surrounding)
        #expect(tracked.update("Cafe\u{301} Werner") == .dictation)
        #expect(tracked.corrections == [correction])
        #expect(WordCorrections.detect(from: "Cafe\u{301}", to: "Café").isEmpty)
        #expect(WordCorrections.detect(from: "cafe", to: "café") == [.init(heard: "cafe", corrected: "café")])
    }

    @Test func ignoresPunctuationNumbersInsertionsDeletionsAndRewrites() {
        for (before, after) in [("Hello Wurner.", "Hello, Wurner!"), ("don't", "don’t"),
                                 ("It's 12 today", "It's 13 today"), ("Hello there", "Hello friend there"),
                                 ("Hello my friend", "Hello friend"), ("A sunny day", "The wet evening")] {
            #expect(WordCorrections.detect(from: before, to: after).isEmpty)
        }
    }

    @Test func preservesCapitalizationAndDeduplicatesRepeatedCorrections() {
        #expect(WordCorrections.detect(from: "werner", to: "Werner") == [.init(heard: "werner", corrected: "Werner")])
        #expect(WordCorrections.detect(from: "Wurner met Wurner", to: "Werner met Werner") == [correction])
        #expect(WordCorrections.detect(from: "Wurner met Wurner", to: "Werner met Warner").isEmpty)
    }
}

@MainActor
struct LearnedDictionaryTests {
    private func fixture() -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString).appendingPathComponent("dictionary.txt")
    }

    @Test func learnedCorrectionPersistsAndAppliesToNextDictation() throws {
        let file = fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        let store = DictionaryStore(fileURL: file, watchChanges: false)
        let pair = LearnedCorrection(heard: "Wurner", corrected: "Werner")
        #expect(store.addLearnedCorrection(pair))
        #expect(!store.addLearnedCorrection(pair))
        let reloaded = DictionaryStore(fileURL: file, watchChanges: false)
        #expect(reloaded.entries.count == 1)
        #expect(reloaded.corrector.apply(to: "Hello Wurner.").text == "Hello Werner.")
    }

    @Test func existingAndDisabledRulesAreNeverOverridden() throws {
        let file = fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        let store = DictionaryStore(fileURL: file, watchChanges: false)
        store.add(.init(kind: .correction, write: "Café", hear: "cafe", isEnabled: false))
        #expect(!store.canLearn(.init(heard: "CAFE", corrected: "Cafe")))
        store.add(.correction(hear: "Cafe\u{301}", write: "Coffee"))
        #expect(!store.canLearn(.init(heard: "Café", corrected: "Cafe")))
        #expect(store.entries.count == 2)
    }

    @Test func failedSaveDoesNotCreatePhantomCorrection() throws {
        let file = fixture()
        defer { try? FileManager.default.removeItem(at: file.deletingLastPathComponent()) }
        try FileManager.default.createDirectory(at: file, withIntermediateDirectories: true)
        let store = DictionaryStore(fileURL: file, watchChanges: false)
        #expect(!store.addLearnedCorrection(.init(heard: "Wurner", corrected: "Werner")))
        #expect(store.entries.isEmpty)
    }
}
