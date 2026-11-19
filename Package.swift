// swift-tools-version: 6.2
import PackageDescription

let package = Package(
    name: "ChitChat",
    platforms: [.macOS(.v26)],
    products: [
        .executable(name: "ChitChat", targets: ["ChitChat"])
    ],
    dependencies: [
        .package(url: "https://github.com/FluidInference/FluidAudio.git", from: "0.15.6")
    ],
    targets: [
        .target(name: "ChitChatDictionary"),
        .target(name: "ShortcutKit"),
        .executableTarget(
            name: "ChitChat",
            dependencies: ["ChitChatDictionary", "ShortcutKit", .product(name: "FluidAudio", package: "FluidAudio")]
        ),
        .testTarget(
            name: "ChitChatDictionaryTests",
            dependencies: ["ChitChatDictionary"],
            resources: [.copy("dictionary-test-vectors.json")]
        ),
        .testTarget(name: "ShortcutKitTests", dependencies: ["ShortcutKit"]),
        .testTarget(name: "TranscriptStoreTests", dependencies: ["ChitChat"])
    ],
    swiftLanguageModes: [.v6]
)
