import AppKit
import ApplicationServices

/// The original insertion snapshot and live access to that same field.
@MainActor
protocol DictationTextTarget {
    var before: String { get }
    var selection: NSRange { get }
    var isFocused: Bool { get }
    func read() -> String?
}

extension DictationTextTarget {
    func expectedValue(inserting text: String) -> String {
        (before as NSString).replacingCharacters(in: selection, with: text)
    }
}

/// A bounded snapshot of the specific, non-secure text field receiving a dictation.
@MainActor
struct EditableTextField: DictationTextTarget {
    static let maximumLength = 200_000
    let element: AXUIElement
    let before: String
    let selection: NSRange

    static func capture() -> EditableTextField? {
        guard let element = focusedElement() else { return nil }
        return capture(element: element)
    }

    static func capture(element: AXUIElement) -> EditableTextField? {
        guard string(kAXSubroleAttribute, of: element) != kAXSecureTextFieldSubrole,
              let text = string(kAXValueAttribute, of: element),
              text.utf16.count <= maximumLength,
              let selected = attribute(kAXSelectedTextRangeAttribute, of: element),
              CFGetTypeID(selected) == AXValueGetTypeID() else { return nil }
        var range = CFRange()
        let decoded = AXValueGetValue(unsafeDowncast(selected, to: AXValue.self), .cfRange, &range)
        guard decoded,
              range.location >= 0, range.length >= 0,
              range.location <= text.utf16.count, range.length <= text.utf16.count - range.location else { return nil }
        return EditableTextField(element: element, before: text,
                                 selection: NSRange(location: range.location, length: range.length))
    }

    var isFocused: Bool {
        guard let focused = Self.focusedElement() else { return false }
        return CFEqual(element, focused)
    }

    func read() -> String? {
        guard Self.string(kAXSubroleAttribute, of: element) != kAXSecureTextFieldSubrole,
              let text = Self.string(kAXValueAttribute, of: element),
              text.utf16.count <= Self.maximumLength else { return nil }
        return text
    }

    static func isSecure(_ element: AXUIElement) -> Bool {
        string(kAXSubroleAttribute, of: element) == kAXSecureTextFieldSubrole
    }

    static func focusedElement() -> AXUIElement? {
        guard let value = attribute(kAXFocusedUIElementAttribute, of: AXUIElementCreateSystemWide()),
              CFGetTypeID(value) == AXUIElementGetTypeID() else { return nil }
        return unsafeDowncast(value, to: AXUIElement.self)
    }

    private static func string(_ name: String, of element: AXUIElement) -> String? {
        attribute(name, of: element) as? String
    }

    private static func attribute(_ name: String, of element: AXUIElement) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, name as CFString, &value) == .success else { return nil }
        return value
    }
}
