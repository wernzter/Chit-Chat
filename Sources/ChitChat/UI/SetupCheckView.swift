import AppKit
import SwiftUI

struct SetupCheckView: View {
    @Bindable var check: SetupCheck
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: DS.Space.wide) {
                Text("Test your actual shortcut, microphone, and keyboard paste. Dictation is paused while the check is active.")
                HStack {
                    Button(check.active ? "Check running" : "Start check") { check.start() }.disabled(check.active)
                    if check.active { Button("Finish check") { check.stop() } }
                }
                GroupBox("1. Recording shortcut") {
                    VStack(alignment: .leading, spacing: DS.Space.snug) {
                        Text(check.shortcutMessage)
                        HStack {
                            Button("Retry shortcut") { check.retryShortcut() }.disabled(!check.active)
                            Button("Accessibility settings") { Permissions.openAccessibilitySettings() }
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(DS.Space.snug)
                }
                GroupBox("2. Microphone") {
                    VStack(alignment: .leading, spacing: DS.Space.snug) {
                        Text(check.microphoneMessage)
                        ProgressView(value: Double(check.level)).accessibilityLabel("Microphone input level")
                        HStack {
                            Button("Test microphone") { check.testMicrophone() }.disabled(!check.active || check.microphoneRunning)
                            Button("Microphone settings") { Permissions.openMicrophoneSettings() }
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(DS.Space.snug)
                }
                GroupBox("3. Text insertion") {
                    VStack(alignment: .leading, spacing: DS.Space.snug) {
                        Text(check.insertionMessage)
                        SetupTestField(check: check).frame(height: DS.Setup.fieldHeight)
                        Button("Test insertion here") { check.testInsertion() }.disabled(!check.active || check.insertionRunning)
                        Text("This uses a fixed sample, restores your clipboard if it hasn’t changed, and adds nothing to history or your dictionary.")
                            .font(DS.Font.label).foregroundStyle(DS.Color.inkSecondary)
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(DS.Space.snug)
                }
            }
            .padding(DS.Space.panel)
        }
        .font(DS.Preferences.bodyFont)
        .foregroundStyle(DS.Color.ink)
        .tint(DS.Color.accent)
        .background(DS.Color.panel)
        .onDisappear { check.stop() }
    }
}

private struct SetupTestField: NSViewRepresentable {
    let check: SetupCheck
    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSTextView.scrollableTextView()
        let text = scroll.documentView as! NSTextView
        text.isRichText = false
        text.isAutomaticQuoteSubstitutionEnabled = false
        text.isAutomaticSpellingCorrectionEnabled = false
        text.setAccessibilityLabel("Setup insertion test field")
        check.textView = text
        return scroll
    }
    func updateNSView(_ view: NSScrollView, context: Context) {}
}
