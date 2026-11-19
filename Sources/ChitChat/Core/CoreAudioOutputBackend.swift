import CoreAudio
import Foundation

/// Controls output hardware, never media players or microphone input properties.
@MainActor
final class CoreAudioOutputBackend: AudioOutputBackend {
    private var listener: AudioObjectPropertyListenerBlock?
    private static let changeSelectors = [kAudioHardwarePropertyDevices,
        kAudioHardwarePropertyDefaultOutputDevice, kAudioHardwarePropertyDefaultSystemOutputDevice]

    func outputs() -> [AudioOutput] {
        var property = address(kAudioHardwarePropertyDevices)
        var size: UInt32 = 0
        let system = AudioObjectID(kAudioObjectSystemObject)
        guard AudioObjectGetPropertyDataSize(system, &property, 0, nil, &size) == noErr,
              size > 0 else { return [] }
        var devices = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        let result = devices.withUnsafeMutableBytes {
            AudioObjectGetPropertyData(system, &property, 0, nil, &size, $0.baseAddress!)
        }
        guard result == noErr else { return [] }
        let outputs: [AudioObjectID: AudioOutput] = Dictionary(uniqueKeysWithValues: devices.compactMap { device in
            let channels = outputChannelCount(device)
            guard channels > 0, let uid = string(device, kAudioDevicePropertyDeviceUID) else { return nil }
            let name = string(device, kAudioObjectPropertyName) ?? "Audio output"
            func control(_ element: UInt32, _ kind: AudioOutputControl.Kind) -> AudioOutputControl {
                .init(deviceID: device, deviceUID: uid, element: element, kind: kind)
            }
            // Prefer the device-wide mute switch. Volume is a fallback for devices
            // with no mute control; per-channel fallback preserves stereo balance.
            for kind in [AudioOutputControl.Kind.mute, .volume] {
                let main = control(kAudioObjectPropertyElementMain, kind)
                if isSettable(main) { return (device, AudioOutput(name: name, controls: [main])) }
            }
            var controls: [AudioOutputControl] = []
            for channel in 1...channels {
                if let item = [control(channel, .mute), control(channel, .volume)].first(where: isSettable) {
                    controls.append(item)
                } else {
                    // Do not partially mute a device and imply all its channels are silent.
                    return (device, AudioOutput(name: name, controls: []))
                }
            }
            return (device, AudioOutput(name: name, controls: controls))
        })
        let devicesByUID = Dictionary(uniqueKeysWithValues: devices.compactMap { device in
            string(device, kAudioDevicePropertyDeviceUID).map { ($0, device) }
        })
        return outputs.compactMap { device, output in
            // macOS creates aggregate devices when recording. Their physical output
            // members are already controlled above, so a missing aggregate-level
            // switch is not a failure. Unknown/uncontrollable members still warn.
            if output.controls.isEmpty, let members = subDeviceUIDs(device), !members.isEmpty,
               members.allSatisfy({ uid in
                   guard let member = devicesByUID[uid] else { return false }
                   return outputChannelCount(member) == 0 || outputs[member]?.controls.isEmpty == false
               }) { return nil }
            return output
        }
    }

    func value(of control: AudioOutputControl) -> Float? {
        guard string(control.deviceID, kAudioDevicePropertyDeviceUID) == control.deviceUID else { return nil }
        var property = address(control)
        var bits: UInt32 = 0
        var size = UInt32(MemoryLayout<UInt32>.size)
        guard AudioObjectGetPropertyData(control.deviceID, &property, 0, nil, &size, &bits) == noErr else { return nil }
        return control.kind == .mute ? Float(bits) : Float(bitPattern: bits)
    }

    func setValue(_ value: Float, for control: AudioOutputControl) -> Bool {
        guard string(control.deviceID, kAudioDevicePropertyDeviceUID) == control.deviceUID else { return false }
        var property = address(control)
        var bits = control.kind == .mute ? UInt32(value) : value.bitPattern
        return AudioObjectSetPropertyData(control.deviceID, &property, 0, nil,
            UInt32(MemoryLayout<UInt32>.size), &bits) == noErr
    }

    func observeOutputChanges(_ handler: (@MainActor @Sendable () -> Void)?) {
        let system = AudioObjectID(kAudioObjectSystemObject)
        if let listener {
            for selector in Self.changeSelectors {
                var property = address(selector)
                AudioObjectRemovePropertyListenerBlock(system, &property, .main, listener)
            }
            self.listener = nil
        }
        guard let handler else { return }
        let listener: AudioObjectPropertyListenerBlock = { _, _ in
            Task { @MainActor in handler() }
        }
        self.listener = listener
        for selector in Self.changeSelectors {
            var property = address(selector)
            AudioObjectAddPropertyListenerBlock(system, &property, .main, listener)
        }
    }

    private func isSettable(_ control: AudioOutputControl) -> Bool {
        var property = address(control)
        var settable: DarwinBoolean = false
        return AudioObjectIsPropertySettable(control.deviceID, &property, &settable) == noErr && settable.boolValue
    }

    private func outputChannelCount(_ device: AudioObjectID) -> UInt32 {
        var property = address(kAudioDevicePropertyStreamConfiguration, scope: kAudioDevicePropertyScopeOutput)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(device, &property, 0, nil, &size) == noErr,
              size >= MemoryLayout<AudioBufferList>.size else { return 0 }
        let storage = UnsafeMutableRawPointer.allocate(byteCount: Int(size), alignment: MemoryLayout<AudioBufferList>.alignment)
        defer { storage.deallocate() }
        guard AudioObjectGetPropertyData(device, &property, 0, nil, &size, storage) == noErr else { return 0 }
        let buffers = UnsafeMutableAudioBufferListPointer(storage.assumingMemoryBound(to: AudioBufferList.self))
        return buffers.reduce(0) { $0 + $1.mNumberChannels }
    }

    private func string(_ device: AudioObjectID, _ selector: AudioObjectPropertySelector) -> String? {
        var property = address(selector)
        var value: CFString?
        var size = UInt32(MemoryLayout<CFString?>.size)
        guard AudioObjectGetPropertyData(device, &property, 0, nil, &size, &value) == noErr else { return nil }
        return value as String?
    }

    private func subDeviceUIDs(_ device: AudioObjectID) -> [String]? {
        var property = address(kAudioAggregateDevicePropertyFullSubDeviceList)
        guard AudioObjectHasProperty(device, &property) else { return nil }
        var value: CFArray?
        var size = UInt32(MemoryLayout<CFArray?>.size)
        guard AudioObjectGetPropertyData(device, &property, 0, nil, &size, &value) == noErr else { return nil }
        return value as? [String]
    }

    private func address(_ control: AudioOutputControl) -> AudioObjectPropertyAddress {
        address(control.kind == .mute ? kAudioDevicePropertyMute : kAudioDevicePropertyVolumeScalar,
                scope: kAudioDevicePropertyScopeOutput, element: control.element)
    }

    private func address(_ selector: AudioObjectPropertySelector,
                         scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal,
                         element: AudioObjectPropertyElement = kAudioObjectPropertyElementMain) -> AudioObjectPropertyAddress {
        .init(mSelector: selector, mScope: scope, mElement: element)
    }
}
