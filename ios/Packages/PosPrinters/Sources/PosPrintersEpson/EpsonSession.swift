#if os(iOS) && canImport(libepos2)
import Foundation
import libepos2
import PosPrinters

/// Session Epson : `Epos2Printer` n'est pas thread-safe, d'où l'acteur (un seul appelant à la fois, déjà garanti par le moteur 4b).
actor EpsonSession: PrinterSession {
    // ponytail: SDK non Sendable ; l'acteur sérialise tous les accès.
    private nonisolated(unsafe) let printer: Epos2Printer
    private let target: String
    private let receiver = Receiver()

    init(series: String, target: String) throws {
        // Langue ANK : le moteur 4b envoie des octets ESC/POS bruts, la langue n'agit pas sur eux.
        guard let printer = Epos2Printer(printerSeries: EpsonCodes.seriesValue(series), lang: Int32(EPOS2_MODEL_ANK.rawValue)) else {
            throw PrinterSessionFailure.failed("Imprimante Epson indisponible (série \(series))")
        }
        self.printer = printer
        self.target = target
        printer.setReceiveEventDelegate(receiver)
    }

    func open(timeout: TimeInterval) async throws {
        let code = printer.connect(target, timeout: Int(timeout * 1000))
        if let failure = EpsonCodes.failure(returnCode: code) { throw failure }
    }

    func send(_ payload: [UInt8], timeout: TimeInterval) async throws {
        _ = printer.clearCommandBuffer()
        receiver.gate.reset()
        if let failure = EpsonCodes.failure(returnCode: printer.addCommand(Data(payload))) { throw failure }
        if let failure = EpsonCodes.failure(returnCode: printer.sendData(Int(timeout * 1000))) { throw failure }
        // Le résultat d'impression arrive ensuite au délégué de réception.
        if let failure = EpsonCodes.failure(callbackCode: await receiver.gate.result(onCancel: EpsonCodes.codeErrTimeout)) { throw failure }
    }

    func close() async {
        // Reprend un envoi encore en attente (délai du transport) pour ne pas laisser de continuation suspendue.
        receiver.gate.finish(EpsonCodes.codeErrTimeout)
        _ = printer.clearCommandBuffer()
        // Après un délai, disconnect() rend ERR_PROCESSING tant que l'impression court : réessai borné (2 s).
        await EpsonCodes.retryWhileProcessing { printer.disconnect() }
        printer.setReceiveEventDelegate(nil)
    }

    /// Délégué du SDK : transmet le code à la porte à reprise unique.
    private final class Receiver: NSObject, Epos2PtrReceiveDelegate, @unchecked Sendable {
        let gate = EpsonCallbackGate()
        func onPtrReceive(_ printerObj: Epos2Printer!, code: Int32, status: Epos2PrinterStatusInfo!, printJobId: String!) {
            gate.finish(code)
        }
    }
}
#endif
