#if os(iOS)
import Foundation
import PosKit
import PosPrinters
import StarIO10

/// Session Star : `StarPrinter` n'est pas `Sendable`, d'où l'acteur (un seul appelant à la fois, déjà garanti par le moteur 4b).
actor StarSession: PrinterSession {
    // ponytail: SDK non Sendable ; l'acteur sérialise tous les accès.
    private nonisolated(unsafe) let printer: StarPrinter

    init(interface: StarInterface, identifier: String) {
        let type: InterfaceType = switch interface {
        case .lan: .lan
        case .bluetooth: .bluetooth
        case .bluetoothLE: .bluetoothLE
        case .usb: .usb
        }
        printer = StarPrinter(StarConnectionSettings(interfaceType: type, identifier: identifier))
    }

    func open(timeout: TimeInterval) async throws {
        printer.openTimeout = Int(timeout * 1000)
        do { try await printer.open() } catch { throw Self.translate(error) }
    }

    func send(_ payload: [UInt8], timeout: TimeInterval) async throws {
        printer.printTimeout = Int(timeout * 1000)
        do { try await printer.print(raw: Data(payload)) } catch { throw Self.translate(error) }
    }

    func close() async { await printer.close() }

    /// Erreurs StarIO10 → `PrinterSessionFailure` (messages en français).
    static func translate(_ error: Error) -> PrinterSessionFailure {
        guard let star = error as? StarIO10Error else { return .failed("Erreur Star : \(error.localizedDescription)") }
        switch star {
        case .notFound: return .failed("Imprimante introuvable")
        case .inUse: return .failed("Imprimante occupée par un autre appareil")
        case let .unprintable(_, code, _):
            return code == .printingTimeout
                ? .timeout : .failed("Impression non effectuée (couvercle ouvert ou papier absent ?)")
        case let .communication(_, code) where code == .printingTimeout: return .timeout
        default: return .failed("Erreur Star : \(star.localizedDescription)")
        }
    }
}
#endif
