// swift-tools-version: 6.0
import PackageDescription

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
        .target(name: "PosPrintersEpson", dependencies: ["PosPrinters"]),
        .testTarget(name: "PosPrintersTests", dependencies: ["PosPrinters"])
    ]
)
