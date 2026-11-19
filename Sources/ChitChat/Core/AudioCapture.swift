import AVFoundation
import Foundation

@MainActor
final class AudioCapture {
    private var runningEngine: AVAudioEngine?

    func start(outputFormat: AVAudioFormat,
               onBuffer: @escaping @Sendable (AudioChunk) -> Void,
               onLevel: @escaping @Sendable (Float) -> Void) throws {
        guard runningEngine == nil else { return }

        let engine = AVAudioEngine()
        let mic = engine.inputNode
        let deviceFormat = mic.outputFormat(forBus: 0)
        guard deviceFormat.sampleRate > 0, deviceFormat.channelCount > 0 else {
            throw CaptureFailure.noInput
        }

        let bridge = try MicrophoneTap(source: deviceFormat, output: outputFormat)
        mic.installTap(onBus: 0, bufferSize: 2048, format: deviceFormat) { buffer, _ in
            onLevel(MicrophoneTap.level(buffer))
            if let audio = bridge.makeOwnedChunk(buffer) {
                onBuffer(audio)
            }
        }

        do {
            engine.prepare()
            try engine.start()
            runningEngine = engine
        } catch {
            mic.removeTap(onBus: 0)
            throw error
        }
    }

    func stop() {
        guard let engine = runningEngine else { return }
        runningEngine = nil
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
    }

    private enum CaptureFailure: LocalizedError {
        case noInput

        var errorDescription: String? { "No microphone is available for recording." }
    }
}

/// An instance belongs to one installed tap. Its converter is only used from that tap.
private final class MicrophoneTap: @unchecked Sendable {
    private let target: AVAudioFormat
    private let resampler: AVAudioConverter?

    init(source: AVAudioFormat, output: AVAudioFormat) throws {
        target = output
        if source == output {
            resampler = nil
        } else if let converter = AVAudioConverter(from: source, to: output) {
            resampler = converter
        } else {
            throw TranscriptionError.noAudioFormat
        }
    }

    func makeOwnedChunk(_ borrowed: AVAudioPCMBuffer) -> AudioChunk? {
        guard borrowed.frameLength > 0 else { return nil }
        if let resampler {
            let rate = target.sampleRate / borrowed.format.sampleRate
            let size = AVAudioFrameCount(ceil(rate * Double(borrowed.frameLength))) + 64
            guard let output = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: size) else { return nil }
            let input = ConverterInput(borrowed)
            var error: NSError?
            let status = resampler.convert(to: output, error: &error, withInputFrom: input.provide)
            guard error == nil, status != .error, output.frameLength > 0 else { return nil }
            return AudioChunk(buffer: output)
        }

        guard let owned = AVAudioPCMBuffer(pcmFormat: target, frameCapacity: borrowed.frameLength) else {
            return nil
        }
        owned.frameLength = borrowed.frameLength
        let planes = UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: borrowed.audioBufferList))
        for (from, to) in zip(planes, UnsafeMutableAudioBufferListPointer(owned.mutableAudioBufferList)) {
            guard let input = from.mData, let output = to.mData, from.mDataByteSize <= to.mDataByteSize else {
                return nil
            }
            output.copyMemory(from: input, byteCount: Int(from.mDataByteSize))
        }
        return AudioChunk(buffer: owned)
    }

    static func level(_ buffer: AVAudioPCMBuffer) -> Float {
        let frames = Int(buffer.frameLength)
        guard frames > 0, let left = buffer.floatChannelData?.pointee else { return 0 }
        let stride = Int(buffer.stride)
        var energy: Float = 0
        for frame in 0..<frames {
            let sample = left[frame * stride]
            energy += sample * sample
        }
        let average = max(energy / Float(frames), 1e-14)
        let decibels = 10 * log10(average)
        return min(1, max(0, 1 + decibels / 50))
    }
}

/// AVAudioConverter reads the tap buffer synchronously before the callback ends.
private final class ConverterInput: @unchecked Sendable {
    private var pcm: AVAudioPCMBuffer?

    init(_ pcm: AVAudioPCMBuffer) { self.pcm = pcm }

    func provide(_: AVAudioPacketCount, _ status: UnsafeMutablePointer<AVAudioConverterInputStatus>) -> AVAudioBuffer? {
        let available = pcm
        pcm = nil
        status.pointee = available == nil ? .noDataNow : .haveData
        return available
    }
}
