import OSLog

enum Log {
    static let audio = Logger(subsystem: "ai.pivotstudio.chit-chat", category: "audio")
    static let speech = Logger(subsystem: "ai.pivotstudio.chit-chat", category: "speech")
    static let hotkey = Logger(subsystem: "ai.pivotstudio.chit-chat", category: "hotkey")
    static let inject = Logger(subsystem: "ai.pivotstudio.chit-chat", category: "inject")
    static let app = Logger(subsystem: "ai.pivotstudio.chit-chat", category: "app")
}
