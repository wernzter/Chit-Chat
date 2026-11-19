import AppKit
import ChitChatDictionary
import SwiftUI

struct DictionaryPanel: View {
    @State private var dictionary = DictionaryStore.shared
    @State private var query = ""
    @State private var editor: EditorRequest?

    var body: some View {
        VStack(spacing: DS.Space.roomy) {
            HStack(spacing: DS.Space.base) {
                SearchField(text: $query, placeholder: "Search your dictionary")
                Button("Add entry", systemImage: "plus") { editor = EditorRequest() }
                    .buttonStyle(.borderedProminent)
                    .tint(DS.Color.action)
                    .keyboardShortcut("n", modifiers: .command)
            }

            entryList

            HStack {
                Text("\(dictionary.entries.count) entries")
                Spacer()
                Button("Show dictionary file", action: revealFile)
                    .buttonStyle(.plain)
            }
            .font(DS.Font.label)
            .foregroundStyle(DS.Color.inkSecondary)
        }
        .sheet(item: $editor) { request in
            DictionaryEditor(existing: request.existing) { entry in
                if request.existing == nil {
                    dictionary.add(entry)
                } else {
                    dictionary.update(entry)
                }
            }
        }
    }

    @ViewBuilder
    private var entryList: some View {
        let matches = dictionary.filtered(by: query)
        if matches.isEmpty {
            if dictionary.entries.isEmpty {
                EmptyPanel(label: "Make it sound like you",
                           detail: "Add names, specialist terms, or words that need a correction.")
            } else {
                EmptyPanel(label: "No matches", detail: "Try a different search.")
            }
        } else {
            ScrollView {
                LazyVStack(spacing: DS.Space.snug) {
                    ForEach(matches) { row($0) }
                }
            }
        }
    }

    private func row(_ entry: DictionaryEntry) -> some View {
        HStack(spacing: DS.Space.roomy) {
            VStack(alignment: .leading, spacing: DS.Space.snug) {
                Text(entry.kind == .term ? "Term" : "Correction")
                    .font(DS.Font.caption)
                    .foregroundStyle(DS.Color.inkSecondary)

                HStack(spacing: DS.Space.snug) {
                    if entry.kind == .correction {
                        Text(entry.hear)
                        Image(systemName: "arrow.right").accessibilityHidden(true)
                    }
                    Text(entry.write)
                        .font(DS.Font.bodyEmphasis)
                        .foregroundStyle(DS.Color.ink)
                }
                .foregroundStyle(DS.Color.inkSecondary)
                .textSelection(.enabled)
            }
            .opacity(entry.isEnabled ? 1 : DS.Layout.rowDisabledOpacity)

            Spacer(minLength: DS.Space.snug)
            Toggle("Enable \(entry.write)", isOn: enabled(for: entry))
                .labelsHidden()
                .toggleStyle(.switch)
                .controlSize(.small)

            Button("Edit \(entry.write)", systemImage: "pencil") {
                editor = EditorRequest(existing: entry)
            }
            .help("Edit entry")

            Button("Delete \(entry.write)", systemImage: "trash") {
                dictionary.delete(entry)
            }
            .help("Delete entry")
        }
        .labelStyle(.iconOnly)
        .buttonStyle(.borderless)
        .padding(DS.Space.roomy)
        .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.panel))
    }

    private func enabled(for entry: DictionaryEntry) -> Binding<Bool> {
        Binding(get: { entry.isEnabled }, set: { enabled in
            var revised = entry
            revised.isEnabled = enabled
            dictionary.update(revised)
        })
    }

    private func revealFile() {
        NSWorkspace.shared.activateFileViewerSelecting([DictionaryStore.fileURL])
    }

    private struct EditorRequest: Identifiable {
        let existing: DictionaryEntry?
        let id = UUID()

        init(existing: DictionaryEntry? = nil) { self.existing = existing }
    }
}

private struct DictionaryEditor: View {
    private let save: (DictionaryEntry) -> Void
    private let title: String
    @Environment(\.dismiss) private var dismiss
    @State private var draft: DictionaryEntry

    init(existing: DictionaryEntry?, save: @escaping (DictionaryEntry) -> Void) {
        self.save = save
        title = existing == nil ? "Add dictionary entry" : "Edit dictionary entry"
        _draft = State(initialValue: existing ?? .term(""))
    }

    var body: some View {
        let candidate = normalized()
        let complete = !candidate.write.isEmpty && (candidate.kind == .term || !candidate.hear.isEmpty)

        VStack(alignment: .leading, spacing: DS.Space.wide) {
            Text(title).font(DS.Font.title)
            Picker("Entry type", selection: $draft.kind) {
                Text("Term").tag(DictionaryEntry.Kind.term)
                Text("Correction").tag(DictionaryEntry.Kind.correction)
            }
            .pickerStyle(.segmented)

            if draft.kind == .term {
                Text("Give the recognizer the preferred spelling of a phrase or name.")
                    .font(DS.Font.label)
                    .foregroundStyle(DS.Color.inkSecondary)
                field("Word or phrase", example: "Juniper", text: $draft.write)
            } else {
                Text("Replace a recurring transcription error.")
                    .font(DS.Font.label)
                    .foregroundStyle(DS.Color.inkSecondary)
                field("When you hear", example: "juniper lab", text: $draft.hear)
                field("Replace with", example: "Juniper Labs", text: $draft.write)
            }

            ForEach(DictionaryWarning.check(candidate)) { caution in
                Label(caution.message, systemImage: "info.circle")
                    .font(DS.Font.label)
                    .foregroundStyle(DS.Color.inkSecondary)
                    .fixedSize(horizontal: false, vertical: true)
            }

            HStack {
                Spacer()
                Button("Cancel", action: { dismiss() }).keyboardShortcut(.cancelAction)
                Button("Save") {
                    save(candidate)
                    dismiss()
                }
                .buttonStyle(.borderedProminent)
                .tint(DS.Color.action)
                .disabled(!complete)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(DS.Space.panel)
        .frame(width: DS.Layout.editorWidth)
        .foregroundStyle(DS.Color.ink)
        .background(DS.Color.panel)
        .tint(DS.Color.accent)
    }

    private func normalized() -> DictionaryEntry {
        var entry = draft
        entry.write = draft.write.trimmingCharacters(in: .whitespacesAndNewlines)
        entry.hear = draft.kind == .term ? "" : draft.hear.trimmingCharacters(in: .whitespacesAndNewlines)
        return entry
    }

    private func field(_ name: String, example: String, text: Binding<String>) -> some View {
        VStack(alignment: .leading, spacing: DS.Space.snug) {
            Text(name).font(DS.Font.bodyEmphasis)
            TextField(name, text: text, prompt: Text(example))
                .textFieldStyle(.roundedBorder)
        }
    }
}
