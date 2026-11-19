import Foundation
import Observation

struct AudioOutputControl: Hashable {
    enum Kind: Hashable { case mute, volume }
    let deviceID: UInt32
    let deviceUID: String
    let element: UInt32
    let kind: Kind
    var silentValue: Float { kind == .mute ? 1 : 0 }
}

struct AudioOutput {
    let name: String
    let controls: [AudioOutputControl]
}

@MainActor
protocol AudioOutputBackend: AnyObject {
    func outputs() -> [AudioOutput]
    func value(of control: AudioOutputControl) -> Float?
    func setValue(_ value: Float, for control: AudioOutputControl) -> Bool
    func observeOutputChanges(_ handler: (@MainActor @Sendable () -> Void)?)
}

/// Owns only output settings changed for this recording. All edges are synchronous,
/// so even a quick press/release restores sound before another recording can start.
@MainActor
@Observable
final class AudioOutputMuteController {
    static let shared = AudioOutputMuteController(backend: CoreAudioOutputBackend())

    private(set) var status: String?
    @ObservationIgnored private let backend: any AudioOutputBackend
    @ObservationIgnored private var originals: [AudioOutputControl: Float] = [:]
    @ObservationIgnored private var handled: Set<AudioOutputControl> = []
    @ObservationIgnored private var recording = false
    @ObservationIgnored private var enabled = false

    init(backend: any AudioOutputBackend) { self.backend = backend }

    func begin(enabled: Bool) {
        guard !recording else { return }
        recording = true
        self.enabled = enabled
        guard enabled else { return }
        status = nil
        backend.observeOutputChanges { [weak self] in self?.muteOutputs() }
        muteOutputs()
    }

    func end() {
        guard recording else { return }
        recording = false
        enabled = false
        backend.observeOutputChanges(nil)
        var failed = false
        for (control, value) in originals {
            // A disconnected/replaced device must not affect a different device that
            // happens to reuse its numeric ID. The backend verifies the persistent UID.
            guard backend.value(of: control) != nil else { continue }
            if !backend.setValue(value, for: control) { failed = true }
        }
        originals.removeAll()
        handled.removeAll()
        if failed { status = "Couldn’t restore an audio output. Check macOS Sound settings." }
    }

    private func muteOutputs() {
        guard recording, enabled else { return }
        var unavailable: [String] = []
        for output in backend.outputs() {
            var failed = output.controls.isEmpty
            for control in output.controls where !handled.contains(control) {
                guard let value = backend.value(of: control) else { failed = true; continue }
                if value != control.silentValue {
                    guard backend.setValue(control.silentValue, for: control) else {
                        failed = true
                        continue
                    }
                    originals[control] = value
                }
                handled.insert(control)
            }
            if failed { unavailable.append(output.name) }
        }
        status = unavailable.isEmpty ? nil
            : "Couldn’t mute \(unavailable.joined(separator: ", ")). Check macOS Sound settings."
    }
}
