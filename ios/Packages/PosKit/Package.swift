// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PosKit",
    defaultLocalization: "en",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "PosKit", targets: ["PosKit"])
    ],
    targets: [
        .target(name: "PosKit", resources: [.process("Resources")]),
        .testTarget(
            name: "PosKitTests",
            dependencies: ["PosKit"],
            resources: [.copy("Fixtures")]
        )
    ]
)
