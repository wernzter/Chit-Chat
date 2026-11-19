import Foundation
import Testing
@testable import ChitChat

@MainActor
struct AudioOutputMuteTests {
    private let speaker = AudioOutputControl(deviceID: 1, deviceUID: "speaker", element: 0, kind: .mute)
    private let headphone = AudioOutputControl(deviceID: 2, deviceUID: "headphone", element: 0, kind: .mute)

    @Test func settingDefaultsOffAndPersists() throws {
        let name = "ChitChatAudioMuteTests.\(UUID())"
        let defaults = try #require(UserDefaults(suiteName: name))
        defer { defaults.removePersistentDomain(forName: name) }
        let settings = Settings(defaults: defaults)
        #expect(!settings.muteAudioWhileListening)
        settings.muteAudioWhileListening = true
        #expect(Settings(defaults: defaults).muteAudioWhileListening)
    }

    @Test func disabledLeavesAudioAlone() {
        let backend = FakeOutputBackend(values: [speaker: 0])
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: false)
        controller.end()
        #expect(backend.writes.isEmpty)
        #expect(backend.values[speaker] == 0)
    }

    @Test func immediateReleaseRestoresOnceAndNextRecordingWorks() {
        let backend = FakeOutputBackend(values: [speaker: 0])
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: true)
        #expect(backend.values[speaker] == 1)
        controller.begin(enabled: true)
        controller.end()
        controller.end()
        #expect(backend.writes.map(\.value) == [1, 0])
        controller.begin(enabled: true)
        controller.end()
        #expect(backend.writes.map(\.value) == [1, 0, 1, 0])
    }

    @Test func alreadyMutedOutputStaysMuted() {
        let backend = FakeOutputBackend(values: [speaker: 0, headphone: 1])
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: true)
        controller.end()
        #expect(backend.values == [speaker: 0, headphone: 1])
        #expect(backend.writes.allSatisfy { $0.control == speaker })
    }

    @Test func volumeFallbackRestoresExactChannelLevels() {
        let left = AudioOutputControl(deviceID: 3, deviceUID: "stereo", element: 1, kind: .volume)
        let right = AudioOutputControl(deviceID: 3, deviceUID: "stereo", element: 2, kind: .volume)
        let backend = FakeOutputBackend(values: [left: 0.25, right: 0.7])
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: true)
        #expect(backend.values == [left: 0, right: 0])
        controller.end()
        #expect(backend.values == [left: 0.25, right: 0.7])
    }

    @Test func newOutputIsMutedWithoutOverwritingSavedState() {
        let backend = FakeOutputBackend(values: [speaker: 0])
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: true)
        backend.values[headphone] = 0
        backend.change?()
        #expect(backend.values == [speaker: 1, headphone: 1])
        backend.change?()
        controller.end()
        #expect(backend.values == [speaker: 0, headphone: 0])
        #expect(backend.writes.count == 4)
        #expect(backend.change == nil)
    }

    @Test func disconnectedOutputDoesNotBlockRestoration() {
        let backend = FakeOutputBackend(values: [speaker: 0, headphone: 0])
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: true)
        backend.values.removeValue(forKey: headphone)
        controller.end()
        #expect(backend.values == [speaker: 0])
    }

    @Test func failureLeavesOtherOutputsRestorableAndReportsIt() {
        let backend = FakeOutputBackend(values: [speaker: 0, headphone: 0])
        backend.failed.insert(headphone)
        let controller = AudioOutputMuteController(backend: backend)
        controller.begin(enabled: true)
        #expect(controller.status?.contains("headphone") == true)
        controller.end()
        #expect(backend.values == [speaker: 0, headphone: 0])
    }
}

@MainActor
private final class FakeOutputBackend: AudioOutputBackend {
    var values: [AudioOutputControl: Float]
    var failed: Set<AudioOutputControl> = []
    var writes: [(control: AudioOutputControl, value: Float)] = []
    var change: (@MainActor @Sendable () -> Void)?

    init(values: [AudioOutputControl: Float]) { self.values = values }
    func outputs() -> [AudioOutput] {
        values.keys.map { AudioOutput(name: $0.deviceUID, controls: [$0]) }
    }
    func value(of control: AudioOutputControl) -> Float? { values[control] }
    func setValue(_ value: Float, for control: AudioOutputControl) -> Bool {
        guard !failed.contains(control) else { return false }
        writes.append((control, value))
        values[control] = value
        return true
    }
    func observeOutputChanges(_ handler: (@MainActor @Sendable () -> Void)?) { change = handler }
}
