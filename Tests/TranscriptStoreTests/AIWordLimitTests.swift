import Foundation
import Testing
@testable import ChitChat

struct AIWordLimitTests {
    private actor Formatter: TextFormatter {
        private(set) var calls = 0
        func format(_ raw: String) -> String { calls += 1; return "AI result" }
    }

    @Test func countIgnoresWhitespaceAndStandalonePunctuation() {
        #expect(DictationCleanup.wordCount("  Hello,\nworld!\t42  ") == 3)
        #expect(DictationCleanup.wordCount("Don't re-type café names.") == 4)
        #expect(DictationCleanup.wordCount("... — 🙂 \n") == 0)
        #expect(!DictationCleanup.allowsAI("one two three four", minimumWords: 5))
        #expect(DictationCleanup.allowsAI("one two three four five", minimumWords: 5))
        #expect(DictationCleanup.allowsAI("Hello", minimumWords: 0))
    }

    @Test func shortDictationUsesBasicCleanupWithoutInvokingAppleAI() async {
        let smart = Formatter()
        let result = await DictationCleanup.format("um hello there", enabled: true,
            smartCleanup: true, minimumWords: 5, smartFormatter: smart)
        #expect(result == "Hello there.")
        #expect(await smart.calls == 0)
    }

    @Test func exactBoundaryAndZeroUseAppleAIWhileCleanupOffPreservesRawText() async {
        let smart = Formatter()
        #expect(await DictationCleanup.format("one two three four five", enabled: true,
            smartCleanup: true, minimumWords: 5, smartFormatter: smart) == "AI result")
        #expect(await DictationCleanup.format("Hello", enabled: true,
            smartCleanup: true, minimumWords: 0, smartFormatter: smart) == "AI result")
        #expect(await DictationCleanup.format("um hello", enabled: false,
            smartCleanup: true, minimumWords: 5, smartFormatter: smart) == "um hello")
        #expect(await DictationCleanup.format("one two three four five", enabled: true,
            smartCleanup: false, minimumWords: 5, smartFormatter: smart) == "One two three four five.")
        #expect(await smart.calls == 2)
    }

    @MainActor @Test func thresholdDefaultsToFivePersistsAndAcceptsZero() {
        let name = "ChitChatWordLimitTests.\(UUID())"
        // Use a dedicated suite for each test without touching the app's preferences.
        let isolated = UserDefaults(suiteName: name)!
        defer { isolated.removePersistentDomain(forName: name) }
        let settings = Settings(defaults: isolated)
        #expect(settings.minimumAIWordCount == 5)
        for minimum in [12, 0, 1000] {
            settings.minimumAIWordCount = minimum
            #expect(Settings(defaults: isolated).minimumAIWordCount == minimum)
        }
        settings.minimumAIWordCount = -1
        #expect(settings.minimumAIWordCount == 0)
        #expect(Settings(defaults: isolated).minimumAIWordCount == 0)
    }

    @MainActor @Test func shortDictationNeverReadsKeyOrSendsOpenAIRequest() async throws {
        let name = "ChitChatWordLimitTests.\(UUID())"
        let isolated = UserDefaults(suiteName: name)!
        defer { isolated.removePersistentDomain(forName: name) }
        let ai = OpenAIEnhancement(defaults: isolated, client: OpenAIClient(transport: { _ in
            Issue.record("A short dictation reached OpenAI")
            throw OpenAIError.invalidResponse
        }), readKey: { Issue.record("A short dictation read the API key"); return nil })
        ai.enabled = true
        #expect(try await ai.enhance("Please send this now.", originalText: "please send this now", minimumWords: 5)
                == "Please send this now.")
        #expect(ai.lastDictationStatus?.contains("below the 5-word AI minimum") == true)
    }

    @MainActor @Test func openAIUsesOriginalCountEvenWhenLocalCleanupRemovesWords() async throws {
        let name = "ChitChatWordLimitTests.\(UUID())"
        let defaults = UserDefaults(suiteName: name)!
        defer { defaults.removePersistentDomain(forName: name) }
        let ai = OpenAIEnhancement(defaults: defaults, client: OpenAIClient(transport: { request in
            let body = try #require(JSONSerialization.jsonObject(with: request.httpBody!) as? [String: Any])
            #expect(body["input"] as? String == OpenAIClient.input("Please send this now."))
            return (OpenAIClientTests.completed,
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!)
        }), readKey: { "test-only-key" })
        ai.enabled = true
        #expect(try await ai.enhance("Please send this now.", originalText: "um please send this now", minimumWords: 5)
                == "Polished text.")
    }
}
