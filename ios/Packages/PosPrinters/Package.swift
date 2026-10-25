// swift-tools-version: 6.0
import Foundation
import PackageDescription

// SDK Epson (non commité, voir ios/scripts/install_epson_sdk.sh) : cible binaire seulement s'il est installé.
let epsonSDK = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
    .appendingPathComponent("Vendor/libepos2.xcframework").path
let hasEpsonSDK = FileManager.default.fileExists(atPath: epsonSDK)
// Framework dynamique : aucun linkerSetting requis (le SDK statique exigeait xml2 et consorts).
let epsonDependency: [Target.Dependency] = hasEpsonSDK
    ? [.target(name: "libepos2", condition: .when(platforms: [.iOS]))] : []

let package = Package(
    name: "PosPrinters",
    platforms: [.iOS(.v17), .macOS(.v14)],
    products: [
        .library(name: "PosPrinters", targets: ["PosPrinters"]),
        .library(name: "PosPrintersStar", targets: ["PosPrintersStar"]),
        .library(name: "PosPrintersEpson", targets: ["PosPrintersEpson"])
    ],
    dependencies: [
        .package(path: "../PosKit"),
        .package(url: "https://github.com/star-micronics/StarXpand-SDK-iOS", exact: "2.14.1")
    ],
    targets: [
        .target(name: "PosPrinters", dependencies: ["PosKit"]),
        .target(
            name: "PosPrintersStar",
            dependencies: [
                "PosPrinters",
                .product(name: "StarIO10", package: "StarXpand-SDK-iOS", condition: .when(platforms: [.iOS]))
            ]
        ),
        .target(name: "PosPrintersEpson", dependencies: ["PosPrinters"] + epsonDependency),
        .testTarget(name: "PosPrintersTests", dependencies: ["PosPrinters"]),
        .testTarget(name: "PosPrintersEpsonTests", dependencies: ["PosPrinters", "PosPrintersEpson"] + epsonDependency)
    ] + (hasEpsonSDK ? [.binaryTarget(name: "libepos2", path: "Vendor/libepos2.xcframework")] : [])
)
