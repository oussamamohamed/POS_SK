import Foundation
import PosKit

public enum StarInterface: String, Sendable { case lan, bluetooth, bluetoothLE, usb }

/// Adresse d'une imprimante Star ou Epson, lue dans `Printer.deviceIdentifier`.
/// Star : `<interface>:<identifiant>` (ex. `bluetooth:00:11:62:AA:BB:CC`). Epson : `<série>;<cible>` (ex. `TM_M30;TCP:192.168.1.60`).
public enum PrinterAddress: Equatable, Sendable {
    case star(interface: StarInterface, identifier: String)
    case epson(series: String, target: String)

    private static let defaultEpsonSeries = "TM_T88"
    private static let epsonTargetPrefixes = ["TCP:", "BT:", "USB:"]

    /// Lit `printer.deviceIdentifier` selon `printer.transport` ; nil/vide → réseau sur `ipAddress`.
    public static func parse(_ printer: Printer) throws -> PrinterAddress {
        let raw = printer.deviceIdentifier ?? ""
        switch printer.transport {
        case .star:
            if raw.isEmpty { return .star(interface: .lan, identifier: printer.ipAddress) }
            // Seul le premier « : » sépare l'interface ; un identifiant Bluetooth garde les siens.
            guard let colon = raw.firstIndex(of: ":"),
                  let interface = StarInterface(rawValue: String(raw[..<colon])),
                  raw.index(after: colon) < raw.endIndex
            else { throw invalid(raw) }
            return .star(interface: interface, identifier: String(raw[raw.index(after: colon)...]))
        case .epson:
            if raw.isEmpty { return .epson(series: defaultEpsonSeries, target: "TCP:\(printer.ipAddress)") }
            let parts = raw.split(separator: ";", maxSplits: 1, omittingEmptySubsequences: false).map(String.init)
            let (series, target) = parts.count == 2 ? (parts[0], parts[1]) : (defaultEpsonSeries, parts[0])
            guard !series.isEmpty, epsonTargetPrefixes.contains(where: { target.hasPrefix($0) && target.count > $0.count })
            else { throw invalid(raw) }
            return .epson(series: series, target: target)
        case .lan:
            throw invalid(raw)
        }
    }

    /// Chaîne à stocker dans `deviceIdentifier` (inverse de `parse`).
    public var deviceIdentifier: String {
        switch self {
        case let .star(interface, identifier): "\(interface.rawValue):\(identifier)"
        case let .epson(series, target): "\(series);\(target)"
        }
    }

    private static func invalid(_ raw: String) -> PrinterTransportError {
        .connection("Identifiant d'imprimante invalide : \(raw)")
    }
}
