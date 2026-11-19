import Foundation

enum OpenAIReasoningEffort: String, CaseIterable, Sendable {
    case modelDefault = "default", off = "none", minimal, low, medium, high, xhigh, max

    var label: String {
        switch self {
        case .modelDefault: "Model default"
        case .off: "Off"
        case .xhigh: "Extra high"
        case .max: "Maximum"
        default: rawValue.capitalized
        }
    }
}

/// Explicit model contracts from official OpenAI model pages (2026-09-06).
/// Match only aliases and dated snapshots; never assume a chat/pro/custom variant
/// inherits its family's capabilities. Unknown models use the API default.
enum OpenAIReasoning {
    static func options(for model: String) -> [OpenAIReasoningEffort] {
        let base = model.replacingOccurrences(of: "-\\d{4}-\\d{2}-\\d{2}$", with: "", options: .regularExpression)
        let efforts: [OpenAIReasoningEffort]
        switch base {
        case "gpt-5", "gpt-5-mini", "gpt-5-nano": efforts = [.minimal, .low, .medium, .high]
        case "gpt-5-pro": efforts = [.high]
        case "o3": efforts = [.low, .medium, .high]
        case "gpt-5.1": efforts = [.off, .low, .medium, .high]
        case "gpt-5.2", "gpt-5.4", "gpt-5.4-mini", "gpt-5.4-nano", "gpt-5.5":
            efforts = [.off, .low, .medium, .high, .xhigh]
        case "gpt-5.6", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna":
            efforts = [.off, .low, .medium, .high, .xhigh, .max]
        case "gpt-6-astra": efforts = [.low, .medium, .high, .xhigh, .max]
        case "gpt-5.2-codex", "gpt-5.3-codex": efforts = [.low, .medium, .high, .xhigh]
        case "gpt-5.4-pro", "gpt-5.5-pro": efforts = [.medium, .high, .xhigh]
        default: efforts = []
        }
        return [.modelDefault] + efforts
    }

    static func explanation(for model: String) -> String {
        let options = options(for: model)
        if options.count == 1 {
            return "This model has no verified effort controls in Chit-Chat. The API’s default behavior is used; no reasoning option is sent."
        }
        if !options.contains(.off) {
            return "This model requires reasoning, so Off is unavailable. Choose an effort level or let the model use its default."
        }
        return "Choose Off to disable reasoning, or an effort level to turn it on. Model default lets OpenAI choose. Higher effort can take longer and use more API tokens."
    }
}
