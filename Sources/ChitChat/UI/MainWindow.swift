import AppKit
import SwiftUI

struct MainWindow: View {
    @Bindable var controller: DictationController
    @Bindable var navigation: AppNavigation
    let setup: SetupCheck
    @Environment(\.openWindow) private var openWindow
    @State private var settings = Settings.shared

    var body: some View {
        HStack(spacing: DS.Space.hair) {
            sidebar
            VStack(spacing: DS.Space.hair) {
                header
                RecoveryNotice()
                if case .error(let message) = controller.state {
                    Label(message, systemImage: "exclamationmark.circle")
                        .font(DS.Font.label).foregroundStyle(DS.Color.inkSecondary)
                        .padding(.horizontal, DS.Space.panel)
                }
                content
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            .background(DS.Color.panel)
        }
        .background(DS.Color.seam)
        .foregroundStyle(DS.Color.ink)
        .font(DS.Font.body)
        .tint(DS.Color.accent)
        .frame(minWidth: DS.Layout.minWidth, minHeight: DS.Layout.minHeight)
        .onAppear {
            navigation.openMainWindow = { openWindow(id: "main") }
        }
        .onChange(of: navigation.page) { previous, _ in
            if previous == .setup { setup.stop() }
        }
        .onDisappear { setup.stop() }
        .onReceive(NotificationCenter.default.publisher(for: NSWindow.willCloseNotification)) { _ in setup.stop() }
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: DS.Space.snug) {
            HStack(spacing: DS.Space.base) {
                Image(systemName: "waveform")
                    .font(DS.Font.brandIcon)
                    .foregroundStyle(DS.Color.accent)
                    .frame(width: DS.Layout.brandIconSize, height: DS.Layout.brandIconSize)
                    .background(DS.Color.accentSoft, in: RoundedRectangle(cornerRadius: DS.Radius.control))
                Text("Chit-Chat").font(DS.Font.brand)
            }
            .padding(.vertical, DS.Space.roomy)
            groupLabel("Workspace")
            navigationRow(.history)
            navigationRow(.dictionary)
            groupLabel("Settings").padding(.top, DS.Space.roomy)
            ForEach([AppPage.recording, .speech, .cleanup, .openAI, .access], id: \.self) { navigationRow($0) }
            Spacer(minLength: DS.Space.roomy)
            Divider()
            navigationRow(.setup)
            Label(settings.engine.displayName, systemImage: "cpu")
                .font(DS.Font.caption)
                .foregroundStyle(DS.Color.inkSecondary)
                .padding(DS.Space.base)
        }
        .padding(.horizontal, DS.Space.base)
        .padding(.bottom, DS.Space.base)
        .frame(width: DS.Layout.sidebarWidth)
        .background(DS.Color.sidebar)
    }

    private func groupLabel(_ title: String) -> some View {
        Text(title).font(DS.Font.section).foregroundStyle(DS.Color.inkSecondary)
            .padding(.horizontal, DS.Space.base)
            .padding(.bottom, DS.Space.tight)
    }

    private func navigationRow(_ page: AppPage) -> some View {
        Button { navigation.select(page) } label: {
            HStack(spacing: DS.Space.base) {
                Image(systemName: page.icon).frame(width: DS.Layout.navigationIconWidth)
                Text(page.title)
                Spacer(minLength: DS.Space.tight)
            }
            .font(DS.Font.bodyEmphasis)
            .foregroundStyle(navigation.page == page ? DS.Color.accent : DS.Color.inkSecondary)
            .padding(DS.Space.base)
            .background(navigation.page == page ? DS.Color.accentSoft : .clear,
                        in: RoundedRectangle(cornerRadius: DS.Radius.control))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(navigation.page == page ? .isSelected : [])
    }

    private var header: some View {
        HStack(spacing: DS.Space.wide) {
            VStack(alignment: .leading, spacing: DS.Space.snug) {
                Text(navigation.page.title).font(DS.Preferences.titleFont)
                Text(navigation.page.detail).font(DS.Font.label).foregroundStyle(DS.Color.inkSecondary)
            }
            Spacer(minLength: DS.Space.base)
            RecordControl(controller: controller)
        }
        .padding(DS.Space.panel)
    }

    @ViewBuilder private var content: some View {
        switch navigation.page {
        case .history: TranscriptionList()
        case .dictionary:
            VStack(spacing: DS.Space.roomy) {
                Label("Corrections learned from your edits appear here. You can also add terms and spelling rules.",
                      systemImage: "lightbulb")
                    .font(DS.Font.label).foregroundStyle(DS.Color.inkSecondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                DictionaryPanel()
            }
            .padding(.horizontal, DS.Space.panel)
            .padding(.bottom, DS.Space.wide)
        case .setup: SetupCheckView(check: setup)
        default:
            if let section = navigation.page.settingsSection {
                SettingsPage(controller: controller, openSetup: { navigation.select(.setup) }, section: section)
                    .id(section)
            }
        }
    }
}

private struct RecordControl: View {
    @Bindable var controller: DictationController
    @State private var startedAt: Date?
    private var busy: Bool { controller.state == .finishing }

    var body: some View {
        VStack(alignment: .trailing, spacing: DS.Space.snug) {
            Button {
                if controller.state.isActive { controller.stopButtonRecording() }
                else { controller.startButtonRecording() }
            } label: {
                HStack(spacing: DS.Space.snug) {
                    if busy { ProgressView().controlSize(.small) }
                    else { Image(systemName: controller.state.isActive ? "stop.fill" : "mic.fill") }
                    Text(busy ? "Processing" : controller.state.isActive ? "Stop" : "Record")
                }
                .frame(minWidth: DS.Layout.recordButtonWidth)
                .padding(.vertical, DS.Space.tight)
            }
            .buttonStyle(.borderedProminent)
            .tint(controller.state.isActive && !busy ? DS.Color.record : DS.Color.action)
            .disabled(controller.isCheckingSetup || busy)
            TimelineView(.periodic(from: .now, by: DS.Layout.timerInterval)) { _ in
                if let startedAt, controller.state.isActive {
                    Text(startedAt, style: .timer).font(DS.Font.counter)
                } else {
                    Text(controller.isCheckingSetup ? "Setup check active" : Settings.shared.shortcut.displayName)
                        .font(DS.Font.caption)
                }
            }
            .foregroundStyle(DS.Color.inkSecondary)
        }
        .onChange(of: controller.state.isActive) { _, active in startedAt = active ? Date() : nil }
    }
}
