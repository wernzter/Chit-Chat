import SwiftUI

struct OpenAISettingsView: View {
    @State private var ai = OpenAIEnhancement.shared
    @State private var key = ""
    @State private var customModel = ""

    var body: some View {
        card("AI enhancement") {
            Toggle("Enhance dictation with OpenAI", isOn: $ai.enabled).toggleStyle(.switch)
            note("When enabled, your transcript and prompt are sent to OpenAI after local cleanup. Audio stays on your Mac. API usage is billed to your OpenAI account.")
            note("If enhancement fails, Chit-Chat inserts the locally cleaned text. Dictionary corrections are applied last.")
            Divider()
            AIWordLimitSetting()
            if let status = ai.lastDictationStatus { note(status) }
        }
        card("Connection") {
            SecureField("Enter or replace your API key", text: $key)
                .textFieldStyle(.roundedBorder)
                .accessibilityLabel("OpenAI API key")
            HStack {
                Button("Save key") { if ai.saveKey(key) { key = "" } }.disabled(key.isEmpty)
                Button("Remove saved key") { ai.removeKey(); key = "" }
                Link("Get an API key", destination: URL(string: "https://platform.openai.com/api-keys")!)
            }
            Button(key.isEmpty ? "Test connection" : "Save & test connection") {
                if !key.isEmpty {
                    guard ai.saveKey(key) else { return }
                    key = ""
                }
                Task { await ai.testConnection() }
            }
            .disabled(ai.busy)
            connectionResult
            note("Tests your saved key and selected model with a short sample. No recordings are sent. Each test uses a small amount of paid API tokens.")
            note("Keys are stored in Chit-Chat’s local app data, without Keychain prompts. The file is readable only by your macOS account, but is not encrypted by Chit-Chat. A ChatGPT subscription does not provide API credit.")
            if !ai.message.isEmpty {
                Text(ai.message).textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
            }
            if ai.busy { ProgressView().controlSize(.small) }
        }
        card("Model & speed") {
            HStack {
                Picker("Sort by", selection: $ai.sort) {
                    ForEach(OpenAIModelSort.allCases, id: \.self) { Text($0.rawValue).tag($0) }
                }
                Button("Refresh models") { Task { await ai.refreshModels() } }.disabled(ai.busy)
            }
            Picker("Model", selection: $ai.model) {
                ForEach(ai.sortedModels, id: \.self) { model in
                    Text(modelLabel(model)).tag(model)
                }
            }
            HStack {
                TextField("Custom model ID", text: $customModel).textFieldStyle(.roundedBorder)
                Button("Use model") {
                    ai.model = customModel.trimmingCharacters(in: .whitespacesAndNewlines)
                    customModel = ""
                }.disabled(customModel.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            Divider()
            Picker("Reasoning effort", selection: $ai.effort) {
                ForEach(OpenAIReasoning.options(for: ai.model), id: \.self) { effort in
                    Text(effort.label).tag(effort)
                }
            }
            .disabled(OpenAIReasoning.options(for: ai.model).count == 1)
            note(OpenAIReasoning.explanation(for: ai.model))
            note("Your choice is saved for each model and used for dictation, prompt previews, and speed tests.")
            Button("Measure selected model’s speed") { Task { await ai.testModel(speedOnly: true) } }
                .disabled(ai.busy)
            note("Speed uses the same short sample and default prompt, including network time. Timings are kept separately for each model and effort. Test several models, then choose Fastest tested. Untested settings appear last. Each test uses paid API tokens; actual dictations may take longer.")
        }
        card("Enhancement prompt") {
            note("Tell the model how to edit your transcript. The transcript is sent separately; no placeholder is needed. Changes save automatically.")
            TextEditor(text: $ai.prompt)
                .font(DS.Preferences.bodyFont)
                .frame(height: DS.Preferences.promptHeight)
                .padding(DS.Space.snug)
                .background(DS.Color.panel, in: RoundedRectangle(cornerRadius: DS.Radius.panel))
                .accessibilityLabel("Enhancement prompt")
            HStack {
                Button("Restore default prompt") { ai.prompt = OpenAIEnhancement.defaultPrompt }
                Button("Test my prompt") { Task { await ai.testModel(speedOnly: false) } }
                    .disabled(ai.busy || ai.prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
            note("Sample: “\(OpenAIEnhancement.speedSample)”")
            if !ai.preview.isEmpty {
                Divider()
                Text("Sample result").font(DS.Font.bodyEmphasis)
                Text(ai.preview).textSelection(.enabled)
            }
        }
        .onDisappear { key = "" }
    }

    @ViewBuilder private var connectionResult: some View {
        if !key.isEmpty {
            note("Unsaved key. Save and test it to check the connection.")
        } else {
            switch ai.connectionTest {
            case .notTested:
                note("Connection not tested. Save your key, then test it here.")
            case .testing:
                Label("Testing your key and selected model…", systemImage: "arrow.triangle.2.circlepath")
                    .foregroundStyle(DS.Color.inkSecondary)
            case .passed(let result):
                Label(result, systemImage: "checkmark.circle.fill")
                    .foregroundStyle(DS.Color.success)
                    .fixedSize(horizontal: false, vertical: true)
            case .failed(let reason):
                Label("Connection test failed. \(reason)", systemImage: "exclamationmark.circle.fill")
                    .foregroundStyle(DS.Color.record)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }

    private func modelLabel(_ model: String) -> String {
        if let seconds = ai.timings[model] { return "\(model) · \(String(format: "%.2f", seconds)) s · \(ai.effort(for: model).label)" }
        return "\(model) · untested"
    }

    private func note(_ text: String) -> some View {
        Text(text).foregroundStyle(DS.Color.inkSecondary).fixedSize(horizontal: false, vertical: true)
    }

    private func card<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: DS.Space.roomy) {
            Text(title).font(DS.Font.title)
            content()
        }
        .padding(DS.Space.wide)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(DS.Color.cap, in: RoundedRectangle(cornerRadius: DS.Radius.window))
    }
}
