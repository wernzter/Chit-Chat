import Foundation
import Observation

/// Watches only a verified insertion in its original focused field. Nothing is recorded
/// from other fields, and document snapshots remain in memory for this observation only.
@MainActor
@Observable
final class CorrectionLearner {
    static let shared = CorrectionLearner()
    private(set) var pending: CorrectionProposal?
    private(set) var saveError = false
    private(set) var isWatching = false
    var onVisibilityChange: ((Bool) -> Void)?

    private let dictionary: DictionaryStore
    private let clock: () -> TimeInterval
    private var task: Task<Void, Never>?
    private var session = UUID()
    private var learning: CorrectionLearningState?

    init(dictionary: DictionaryStore = .shared,
         clock: @escaping () -> TimeInterval = { ProcessInfo.processInfo.systemUptime }) {
        self.dictionary = dictionary
        self.clock = clock
    }

    func watchInsertion(_ text: String, into target: (any DictationTextTarget)?,
                        duration: TimeInterval? = nil) {
        stop()
        guard let target, !text.isEmpty else { return }
        let expected = target.expectedValue(inserting: text)
        guard expected.utf16.count <= EditableTextField.maximumLength else { return }
        let watchDuration = duration ?? TimeInterval(Settings.shared.editWatchSeconds)
        let token = session
        isWatching = true
        task = Task { @MainActor in
            defer {
                if session == token { finishObservation() }
            }
            // Pasting is asynchronous. Only a confirmed, exact insertion can become a source
            // of learned corrections; a failed paste or normalized replacement is skipped.
            let confirmationDeadline = now + 2
            var observationDeadline: TimeInterval?
            while !Task.isCancelled, session == token, target.isFocused {
                // Stop after an idle window. Editing the inserted span renews it, leaving
                // time to finish typing and cancel a proposal even near the original expiry.
                if let observationDeadline, now >= observationDeadline { return }
                guard let value = target.read() else { return }
                if learning == nil {
                    if value == expected {
                        learning = CorrectionLearningState(original: text, value: expected,
                            range: NSRange(location: target.selection.location, length: text.utf16.count), now: now)
                        observationDeadline = now + watchDuration
                    } else if now >= confirmationDeadline {
                        return
                    }
                } else {
                    if learning?.observe(value, now: now) == true {
                        observationDeadline = now + watchDuration
                    }
                    guard learning?.isValid == true else { return }
                    let ready = learning?.advance(now: now) { dictionary.canLearn($0) }
                    if let ready, !dictionary.addLearnedCorrection(ready) {
                        saveError = true
                        publish()
                        try? await Task.sleep(for: .seconds(4))
                        return
                    }
                    publish()
                }
                try? await Task.sleep(for: .milliseconds(200))
            }
        }
    }

    /// Saving a history edit is explicit, so learn its word corrections without a focus
    /// watcher or countdown. This work survives new dictations and navigation changes.
    func learnTranscriptEdit(from original: String, to edited: String) {
        Task { @MainActor in
            let corrections = await Task.detached(priority: .utility) {
                WordCorrections.detect(from: original, to: edited)
            }.value
            for correction in corrections where dictionary.canLearn(correction) {
                if !dictionary.addLearnedCorrection(correction) {
                    saveError = true
                    publish()
                    return
                }
            }
        }
    }

    func cancelPending() {
        if saveError { stop(); return }
        learning?.dismiss()
        publish()
    }

    func stop() {
        session = UUID()
        task?.cancel()
        task = nil
        finishObservation()
    }

    private var now: TimeInterval { clock() }

    private func publish() {
        let next = learning?.pending
        guard pending != next || saveError else { return }
        pending = next
        onVisibilityChange?(pending != nil || saveError)
    }

    private func finishObservation() {
        isWatching = false
        learning = nil
        pending = nil
        saveError = false
        onVisibilityChange?(false)
    }
}
