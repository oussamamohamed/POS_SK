// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "PosKit",
    defaultLocalization: "fr",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "PosKit", targets: ["PosKit"])
    ],
    targets: [
        .target(name: "PosKit"),
        .testTarget(
            name: "PosKitTests",
            dependencies: ["PosKit"],
            resources: [.copy("Fixtures")]
        )
    ]
)
