import Foundation

/// Canal d'envoi d'octets ESC/POS vers une imprimante. Commun à LAN, Star et Epson (4c).
public protocol PrinterTransport: Sendable {
    /// Envoie les octets ESC/POS déjà rendus ; lève en cas d'échec (réseau, délai, périphérique absent).
    func send(_ payload: [UInt8], to printer: Printer) async throws
}

public enum PrinterTransportError: Error, Equatable, Sendable {
    /// Aucun transport enregistré pour ce type.
    case unsupported(PrinterTransportKind)
    case connectTimeout, writeTimeout
    case connection(String)
}

/// Aiguille sur le transport du type de l'imprimante ; `unsupported` sinon (4c enregistre Star et Epson).
public struct RoutingPrinterTransport: PrinterTransport {
    private let lan: any PrinterTransport
    private let star: (any PrinterTransport)?
    private let epson: (any PrinterTransport)?

    public init(lan: any PrinterTransport = LANPrinterTransport(), star: (any PrinterTransport)? = nil, epson: (any PrinterTransport)? = nil) {
        self.lan = lan
        self.star = star
        self.epson = epson
    }

    public func send(_ payload: [UInt8], to printer: Printer) async throws {
        let transport: (any PrinterTransport)? = switch printer.transport {
        case .lan: lan
        case .star: star
        case .epson: epson
        }
        guard let transport else { throw PrinterTransportError.unsupported(printer.transport) }
        try await transport.send(payload, to: printer)
    }
}
