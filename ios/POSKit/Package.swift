// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "POSKit",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "POSKit", targets: ["POSKit"])
    ],
    targets: [
        .target(name: "POSKit"),
        .testTarget(
            name: "POSKitTests",
            dependencies: ["POSKit"],
            resources: [.copy("Fixtures")]
        )
    ]
)
