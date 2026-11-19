import AppKit
import AVFoundation
import ApplicationServices

@MainActor
enum Permissions {
    static var hasAccessibility: Bool { AXIsProcessTrusted() }
    static var hasMicrophone: Bool { microphoneStatus == .authorized }

    private static var microphoneStatus: AVAuthorizationStatus {
        AVCaptureDevice.authorizationStatus(for: .audio)
    }

    @discardableResult
    static func promptForAccessibility() -> Bool {
        if hasAccessibility { return true }
        let settings = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        return AXIsProcessTrustedWithOptions(settings)
    }

    static func requestMicrophone() async -> Bool {
        if microphoneStatus == .notDetermined {
            return await AVCaptureDevice.requestAccess(for: .audio)
        }
        return hasMicrophone
    }

    static func openAccessibilitySettings() { openPrivacyPane("Accessibility") }
    static func openMicrophoneSettings() { openPrivacyPane("Microphone") }

    private static func openPrivacyPane(_ name: String) {
        let settings = "x-apple.systempreferences:com.apple.preference.security?Privacy_"
        if let location = URL(string: settings + name) {
            NSWorkspace.shared.open(location)
        }
    }
}
