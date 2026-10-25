#if os(iOS)
import Foundation
import PosKit
import PosPrinters

/// Point d'entrée Epson. Le SDK ePOS2 n'est pas commité : sans `Vendor/libepos2.xcframework`
/// (voir `ios/scripts/install_epson_sdk.sh`), `isAvailable` vaut false et le reste rend nil.
public enum EpsonPrinters {
    #if canImport(libepos2)
    public static var isAvailable: Bool { true }

    public static func transport() -> SessionPrinterTransport? {
        SessionPrinterTransport { address in
            guard case let .epson(series, target) = address else {
                throw PrinterTransportError.connection("Adresse Epson attendue")
            }
            return try EpsonSession(series: series, target: target)
        }
    }

    public static func discovery() -> (any PrinterDiscovery)? { EpsonDiscovery() }
    #else
    public static var isAvailable: Bool { false }
    public static func transport() -> SessionPrinterTransport? { nil }
    public static func discovery() -> (any PrinterDiscovery)? { nil }
    #endif
}
#endif
