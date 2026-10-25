import Foundation

/// Une connexion à une imprimante, ouverte pour un envoi puis fermée.
public protocol PrinterSession: Sendable {
    func open(timeout: TimeInterval) async throws
    func send(_ payload: [UInt8], timeout: TimeInterval) async throws
    func close() async
}

/// Échecs que les sessions (SDK Star/Epson) rendent ; traduits par `SessionPrinterTransport`.
public enum PrinterSessionFailure: Error, Equatable, Sendable { case timeout, failed(String) }
