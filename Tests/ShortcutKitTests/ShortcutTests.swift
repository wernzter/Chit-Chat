import Foundation
import Testing
@testable import ShortcutKit

struct ShortcutTests {
    let chord = RecordingShortcut.control | RecordingShortcut.shift

    @Test func defaultAndPersistence() throws {
        #expect(RecordingShortcut.default.displayName == "Control + Shift")
        for shortcut in [RecordingShortcut.default,
                         RecordingShortcut(keyCode: 49, modifiers: 0, keyLabel: "Space"),
                         RecordingShortcut(keyCode: 40, modifiers: chord, keyLabel: "K")] {
            let data = try JSONEncoder().encode(shortcut)
            #expect(try JSONDecoder().decode(RecordingShortcut.self, from: data) == shortcut)
        }
        #expect(!RecordingShortcut(modifiers: 0).isValid)
    }

    @Test func modifierChordInEitherOrder() {
        for first in [RecordingShortcut.control, RecordingShortcut.shift] {
            var matcher = ShortcutMatcher()
            #expect(matcher.handle(.modifiers, keyCode: 59, flags: first, shortcut: .default).edge == nil)
            let press = matcher.handle(.modifiers, keyCode: 56, flags: chord, shortcut: .default)
            #expect(press.edge == .press)
            #expect(!press.consume)
            #expect(matcher.handle(.modifiers, keyCode: 56, flags: first, shortcut: .default).edge == .release)
            #expect(matcher.handle(.modifiers, keyCode: 59, flags: 0, shortcut: .default).edge == nil)
        }
    }

    @Test func modifierUnionHandlesBothPhysicalSides() {
        var matcher = ShortcutMatcher()
        let shortcut = RecordingShortcut(modifiers: RecordingShortcut.shift)
        #expect(matcher.handle(.modifiers, keyCode: 56, flags: RecordingShortcut.shift, shortcut: shortcut).edge == .press)
        #expect(matcher.handle(.modifiers, keyCode: 60, flags: RecordingShortcut.shift, shortcut: shortcut).edge == nil)
        #expect(matcher.handle(.modifiers, keyCode: 56, flags: RecordingShortcut.shift, shortcut: shortcut).edge == nil)
        #expect(matcher.handle(.modifiers, keyCode: 60, flags: 0, shortcut: shortcut).edge == .release)
    }

    @Test func keyChordBalancesEventsWhenModifierReleasedFirst() {
        var matcher = ShortcutMatcher()
        let shortcut = RecordingShortcut(keyCode: 40, modifiers: chord, keyLabel: "K")
        #expect(matcher.handle(.down, keyCode: 40, flags: chord, shortcut: shortcut).edge == .press)
        #expect(matcher.handle(.down, keyCode: 40, flags: chord, isRepeat: true, shortcut: shortcut).consume)
        #expect(matcher.handle(.modifiers, keyCode: 56, flags: RecordingShortcut.control, shortcut: shortcut).edge == .release)
        #expect(matcher.handle(.down, keyCode: 40, flags: 0, isRepeat: true, shortcut: shortcut).edge == nil)
        let release = matcher.handle(.up, keyCode: 40, flags: 0, shortcut: shortcut)
        #expect(release.edge == nil)
        #expect(release.consume)
    }

    @Test func singleKeyAndUnrelatedKeys() {
        var matcher = ShortcutMatcher()
        let shortcut = RecordingShortcut(keyCode: 49, modifiers: 0, keyLabel: "Space")
        #expect(!matcher.handle(.down, keyCode: 0, flags: 0, shortcut: shortcut).consume)
        #expect(matcher.handle(.down, keyCode: 49, flags: RecordingShortcut.command, shortcut: shortcut).edge == nil)
        #expect(matcher.handle(.down, keyCode: 49, flags: 0, shortcut: shortcut).edge == .press)
        let release = matcher.handle(.up, keyCode: 49, flags: 0, shortcut: shortcut)
        #expect(release.edge == .release)
        #expect(release.consume)
    }

    @Test func ignoresLockFlagsAndAdditionalModifiers() {
        var matcher = ShortcutMatcher()
        #expect(matcher.handle(.modifiers, keyCode: 56, flags: chord | RecordingShortcut.command, shortcut: .default).edge == nil)
        #expect(matcher.handle(.modifiers, keyCode: 55, flags: chord | (1 << 16), shortcut: .default).edge == .press)
        #expect(matcher.handle(.down, keyCode: 0, flags: chord, shortcut: .default).edge == nil)
    }

    @Test func tapStartsAndNextPressStopsWithoutRestartingOnRelease() {
        var gesture = RecordingGesture()
        #expect(gesture.press(at: 0, recording: false, busy: false) == .start)
        #expect(gesture.release(at: 0.1) == nil)
        #expect(gesture.press(at: 2, recording: true, busy: false) == .stop)
        #expect(gesture.release(at: 2.1) == nil)
    }

    @Test func holdStopsAtBoundaryAndDuplicatePressIsIgnored() {
        var gesture = RecordingGesture()
        #expect(gesture.press(at: 0, recording: false, busy: false) == .start)
        #expect(gesture.press(at: 0.1, recording: true, busy: false) == nil)
        #expect(gesture.release(at: RecordingGesture.holdThreshold) == .stop)
        #expect(gesture.release(at: 1) == nil)
    }

    @Test func busyAndCancellationCannotLatchRecording() {
        var gesture = RecordingGesture()
        #expect(gesture.press(at: 0, recording: false, busy: true) == nil)
        #expect(gesture.release(at: 1) == nil)
        #expect(gesture.press(at: 2, recording: false, busy: false) == .start)
        gesture.reset()
        #expect(gesture.release(at: 3) == nil)
        #expect(gesture.press(at: 4, recording: false, busy: false) == .start)
    }

    @Test func modifierRecorderWaitsForAllReleases() {
        var capture = ShortcutCapture()
        #expect(capture.modifiersChanged(RecordingShortcut.shift) == nil)
        #expect(capture.modifiersChanged(chord) == nil)
        #expect(capture.modifiersChanged(RecordingShortcut.control) == nil)
        #expect(capture.modifiersChanged(0) == .default)
        #expect(capture.modifiersChanged(0) == nil)
    }

    @Test func functionKeysDoNotAcquireAnImplicitFnModifier() {
        var flags = ShortcutModifiers()
        #expect(flags.normalize(RecordingShortcut.function, modifierEvent: false) == 0)
        #expect(flags.normalize(RecordingShortcut.function, modifierEvent: true) == RecordingShortcut.function)
        #expect(flags.normalize(RecordingShortcut.function, modifierEvent: false) == RecordingShortcut.function)
        #expect(flags.normalize(0, modifierEvent: true) == 0)

        var matcher = ShortcutMatcher()
        let shortcut = RecordingShortcut(keyCode: 97, modifiers: 0, keyLabel: "F6")
        #expect(matcher.handle(.down, keyCode: 97, flags: RecordingShortcut.function, shortcut: shortcut).edge == .press)
        #expect(matcher.handle(.up, keyCode: 97, flags: RecordingShortcut.function, shortcut: shortcut).edge == .release)
    }

    @Test func completeModifierTapAndHoldSequence() {
        var matcher = ShortcutMatcher()
        var gesture = RecordingGesture()
        var recording = false
        func event(_ flags: UInt64, at time: TimeInterval) -> RecordingGesture.Action? {
            let edge = matcher.handle(.modifiers, keyCode: 56, flags: flags, shortcut: .default).edge
            let action: RecordingGesture.Action?
            switch edge {
            case .press: action = gesture.press(at: time, recording: recording, busy: false)
            case .release: action = gesture.release(at: time)
            case nil: action = nil
            }
            if let action {
                recording = action == .start
                if action == .stop { gesture.reset() }
            }
            return action
        }
        #expect(event(chord, at: 0) == .start)
        #expect(event(0, at: 0.1) == nil)
        #expect(recording)
        #expect(event(chord, at: 1) == .stop)
        #expect(event(0, at: 1.1) == nil)
        #expect(!recording)
        #expect(event(chord, at: 2) == .start)
        #expect(event(0, at: 3) == .stop)
        #expect(!recording)
    }
}
