import Foundation
import Testing
@testable import ChitChat

@MainActor struct OpenAIConnectionTests {
    private func defaults() -> (UserDefaults, String) {
        let name = "ChitChatConnectionTests.\(UUID())"
        return (UserDefaults(suiteName: name)!, name)
    }

    @Test func missingKeyFailsWithoutSendingRequest() async {
        let (defaults, name) = defaults()
        defer { defaults.removePersistentDomain(forName: name) }
        let ai = OpenAIEnhancement(defaults: defaults, client: OpenAIClient(transport: { _ in
            Issue.record("Sent a request without a saved key")
            throw OpenAIError.missingKey
        }), readKey: { nil })
        await ai.testConnection()
        #expect(ai.connectionTest == .failed(OpenAIError.missingKey.localizedDescription))
        #expect(!ai.busy)
    }

    @Test func verifiesSavedKeyWithSelectedModelAndEffortWithoutChangingDictation() async throws {
        let (defaults, name) = defaults()
        let store = OpenAIKeyStore(fileURL: FileManager.default.temporaryDirectory
            .appendingPathComponent("\(name)/key"))
        defer {
            defaults.removePersistentDomain(forName: name)
            try? FileManager.default.removeItem(at: store.fileURL.deletingLastPathComponent())
        }
        let sample = OpenAIEnhancement.speedSample, prompt = OpenAIEnhancement.defaultPrompt
        let ai = OpenAIEnhancement(defaults: defaults, client: OpenAIClient(transport: { request in
            #expect(request.url?.path == "/v1/responses")
            #expect(request.value(forHTTPHeaderField: "Authorization") == "Bearer test-only-connection")
            let data = try #require(request.httpBody)
            let body = try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
            #expect(body["model"] as? String == "gpt-5.4-mini")
            #expect(body["input"] as? String == OpenAIClient.input(sample))
            #expect(body["instructions"] as? String == OpenAIClient.instructions(prompt))
            #expect((body["reasoning"] as? [String: String])?["effort"] == "low")
            return (OpenAIClientTests.completed,
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!)
        }), keyStore: store)
        #expect(ai.saveKey("test-only-connection"))
        ai.effort = .low
        ai.prompt = "User prompt is not needed for the connection check."
        await ai.testConnection()
        guard case .passed(let message) = ai.connectionTest else {
            Issue.record("Valid response did not pass the connection test"); return
        }
        #expect(message.contains("gpt-5.4-mini") && message.contains("Low"))
        #expect(!ai.enabled && !ai.busy)
        #expect(ai.preview.isEmpty && ai.timings.isEmpty && ai.lastDictationStatus == nil)
        ai.effort = .high
        #expect(ai.connectionTest == .notTested)
        ai.effort = .low
        await ai.testConnection()
        ai.model = "gpt-4.1-mini"
        #expect(ai.connectionTest == .notTested)
        #expect(ai.saveKey("test-only-replacement"))
        #expect(ai.connectionTest == .notTested)
        ai.removeKey()
        #expect(ai.connectionTest == .notTested)
        #expect(try store.read() == nil)
    }

    @Test(arguments: [401, 403, 404, 429, 500])
    func reportsAPIRejectionsWithoutEchoingSecrets(status: Int) async {
        let (defaults, name) = defaults()
        defer { defaults.removePersistentDomain(forName: name) }
        let ai = OpenAIEnhancement(defaults: defaults, client: OpenAIClient(transport: { request in
            (Data("private API response body".utf8),
             HTTPURLResponse(url: request.url!, statusCode: status, httpVersion: nil, headerFields: nil)!)
        }), readKey: { "test-only-key" })
        await ai.testConnection()
        #expect(ai.connectionTest == .failed(OpenAIError.http(status).localizedDescription))
        #expect(!ai.busy)
    }

    @Test func timeoutReportsFailureAndClearsBusy() async {
        let (defaults, name) = defaults()
        defer { defaults.removePersistentDomain(forName: name) }
        let ai = OpenAIEnhancement(defaults: defaults, client: OpenAIClient(transport: { _ in
            throw URLError(.timedOut)
        }), readKey: { "test-only-key" })
        await ai.testConnection()
        guard case .failed(let reason) = ai.connectionTest else {
            Issue.record("Timeout was not shown as a failure"); return
        }
        #expect(reason.contains("timed out") && !ai.busy)
    }

    @Test func modelChangeDuringRequestDiscardsStaleSuccessAndBlocksDuplicateTest() async {
        let (defaults, name) = defaults()
        defer { defaults.removePersistentDomain(forName: name) }
        let gate = ConnectionGate()
        let ai = OpenAIEnhancement(defaults: defaults, client: OpenAIClient(transport: { request in
            await gate.wait()
            return (OpenAIClientTests.completed,
                    HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!)
        }), readKey: { "test-only-key" })
        let test = Task { await ai.testConnection() }
        while !(await gate.started) { await Task.yield() }
        #expect(ai.connectionTest == .testing && ai.busy)
        await ai.testConnection()
        ai.model = "gpt-4.1-mini"
        await gate.release()
        await test.value
        #expect(ai.connectionTest == .notTested && !ai.busy)
        #expect(await gate.requests == 1)
    }
}

private actor ConnectionGate {
    private var continuation: CheckedContinuation<Void, Never>?
    private(set) var started = false
    private(set) var requests = 0
    func wait() async {
        requests += 1
        await withCheckedContinuation { continuation in
            self.continuation = continuation
            started = true
        }
    }
    func release() { continuation?.resume(); continuation = nil }
}
