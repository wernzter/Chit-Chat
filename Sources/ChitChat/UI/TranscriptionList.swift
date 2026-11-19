import AppKit
import ChitChatDictionary
import SwiftUI

struct TranscriptionList: View {
    @State private var store = TranscriptStore.shared
    @State private var query = ""
    @State private var isConfirmingClear = false
    @State private var editing: DictationRun?

    private var runs: [DictationRun] {
        let all = store.runs.reversed().map { $0 }
        let search = query.trimmingCharacters(in: .whitespacesAndNewlines)
        return search.isEmpty ? all : all.filter { $0.text.localizedStandardContains(search) }
    }

    var body: some View {
        VStack(spacing: DS.Space.roomy) {
            SearchField(text: $query, placeholder: "Search your transcriptions")
            if runs.isEmpty {
                EmptyPanel(label: store.runs.isEmpty ? "Your words start here" : "No matches",
                           detail: store.runs.isEmpty ? "Press Record or use your shortcut to start dictating." : "Try a different search.")
            } else {
                ScrollView {
                    LazyVStack(spacing: DS.Space.base) {
                        ForEach(runs) { run in
                            TranscriptionRow(run: run, onEdit: { editing = run }, onDelete: { _ = store.delete(run) })
                        }
                    }
                }
            }
            HStack {
                Text("\(store.runs.count) transcriptions").font(DS.Font.caption)
                Spacer()
                if !store.runs.isEmpty {
                    Button("Clear history") { isConfirmingClear = true }.buttonStyle(.plain)
                }
            }
            .font(DS.Font.label)
            .foregroundStyle(DS.Color.inkSecondary)
        }
        .padding(.horizontal, DS.Space.panel)
        .padding(.bottom, DS.Space.wide)
        .confirmationDialog("Delete all \(store.runs.count) transcriptions?", isPresented: $isConfirmingClear, titleVisibility: .visible) {
            Button("Delete All", role: .destructive) { _ = store.clear() }
            Button("Cancel", role: .cancel) {}
        } message: { Text("This can’t be undone.") }
        .sheet(item: $editing) { run in
            TranscriptEditor(run: run) { store.updateText(id: run.id, text: $0) }
        }
    }
}

private struct TranscriptionRow: View {
    let run: DictationRun
    let onEdit: () -> Void
    let onDelete: () -> Void
    @State private var didCopy = false

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Space.base) {
            HStack(spacing: DS.Space.snug) {
                Text(run.date.formatted(date: .abbreviated, time: .shortened))
                Spacer()
                Text(run.engine)
                Text(String(format: "%.2f s", run.processSeconds)).monospacedDigit()
            }
            .font(DS.Font.caption).foregroundStyle(DS.Color.inkSecondary)
            Text(run.text)
                .font(DS.Font.body)
                .textSelection(.enabled)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            HStack {
                if let corrections = run.corrections, !corrections.isEmpty {
                    Text(corrections.map { "\($0.from) → \($0.to)" }.joined(separator: " · "))
                        .font(DS.Font.caption).foregroundStyle(DS.Color.inkSecondary)
                        .lineLimit(DS.Layout.correctionBadgeLines)
                }
                Spacer()
                Button {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(run.text, forType: .string)
                    didCopy = true
                    Task { try? await Task.sleep(for: DS.Layout.copyFeedback); didCopy = false }
                } label: {
                    Label(didCopy ? "Copied" : "Copy", systemImage: didCopy ? "checkmark" : "doc.on.doc")
                }
                .buttonStyle(.borderless)
                Button(action: onEdit) { Label("Edit", systemImage: "pencil") }
                    .buttonStyle(.borderless)
                    .accessibilityLabel("Edit transcription")
                Button(action: onDelete) { Image(systemName: "trash") }
                    .buttonStyle(.borderless)
                    .foregroundStyle(DS.Color.inkSecondary)
                    .accessibilityLabel("Delete transcription")
                    .help("Delete this transcription")
            }
            .font(DS.Font.label)
        }
        .padding(DS.Space.roomy)
        .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.panel))
    }
}

struct TranscriptEditor: View {
    let onSave: (String) -> Bool
    private let original: String
    @State private var text: String
    @State private var saveFailed = false
    @Environment(\.dismiss) private var dismiss
    @FocusState private var editorFocused: Bool

    init(run: DictationRun, onSave: @escaping (String) -> Bool) {
        self.onSave = onSave
        original = run.text
        _text = State(initialValue: run.text)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: DS.Space.roomy) {
            Text("Edit transcription").font(DS.Font.title)
            Text("Saving updates this history entry and learns word corrections for future dictations. Existing dictionary rules are preserved. Text already pasted into another app stays as it is.")
                .font(DS.Font.label)
                .foregroundStyle(DS.Color.inkSecondary)
                .fixedSize(horizontal: false, vertical: true)
            TextEditor(text: $text)
                .font(DS.Font.body)
                .scrollContentBackground(.hidden)
                .padding(DS.Space.snug)
                .frame(height: DS.Layout.transcriptEditorHeight)
                .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.control))
                .overlay(RoundedRectangle(cornerRadius: DS.Radius.control).strokeBorder(DS.Color.seam, lineWidth: DS.Border.hairline))
                .focused($editorFocused)
                .accessibilityLabel("Transcript text")
            if saveFailed {
                Text("Couldn’t save this transcript. Your edits are still here; try again.")
                    .font(DS.Font.label).foregroundStyle(DS.Color.record)
            }
            HStack {
                Spacer()
                Button("Cancel") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Save & learn") {
                    if onSave(text) { dismiss() } else { saveFailed = true }
                }
                .buttonStyle(.borderedProminent)
                .tint(DS.Color.action)
                .keyboardShortcut(.defaultAction)
                .disabled(text == original || text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }
        .padding(DS.Space.panel)
        .frame(width: DS.Layout.transcriptEditorWidth)
        .foregroundStyle(DS.Color.ink)
        .background(DS.Color.panel)
        .onAppear { editorFocused = true }
    }
}

struct SearchField: View {
    @Binding var text: String
    let placeholder: String

    private var shape: RoundedRectangle { RoundedRectangle(cornerRadius: DS.Radius.control) }

    var body: some View {
        HStack(spacing: DS.Space.snug) {
            Image(systemName: "magnifyingglass")
                .accessibilityHidden(true)
            TextField("", text: $text, prompt: Text(placeholder))
                .textFieldStyle(.plain)
                .accessibilityLabel(placeholder)
                .foregroundStyle(DS.Color.ink)
            if !text.isEmpty {
                Button("Clear search", systemImage: "xmark.circle.fill") { text.removeAll() }
                    .labelStyle(.iconOnly)
                    .buttonStyle(.plain)
            }
        }
        .foregroundStyle(DS.Color.inkSecondary)
        .padding(DS.Space.base)
        .background(DS.Color.cap, in: shape)
        .overlay(shape.strokeBorder(DS.Color.seam, lineWidth: DS.Border.hairline))
    }
}

struct EmptyPanel: View {
    let label: String
    let detail: String
    var body: some View {
        ContentUnavailableView(label, systemImage: "text.bubble", description: Text(detail))
            .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
