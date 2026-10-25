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
        receiver.reset()
        if let failure = EpsonCodes.failure(returnCode: printer.addCommand(Data(payload))) { throw failure }
        if let failure = EpsonCodes.failure(returnCode: printer.sendData(Int(timeout * 1000))) { throw failure }
        // Le résultat d'impression arrive ensuite au délégué de réception.
        if let failure = EpsonCodes.failure(callbackCode: await receiver.result()) { throw failure }
    }

    func close() async {
        _ = printer.clearCommandBuffer()
        _ = printer.disconnect()
        printer.setReceiveEventDelegate(nil)
    }

    /// Délégué du SDK converti en reprise unique de continuation ; `lock` protège l'état (d'où `@unchecked Sendable`).
    private final class Receiver: NSObject, Epos2PtrReceiveDelegate, @unchecked Sendable {
        private let lock = NSLock()
        private var code: Int32?
        private var continuation: CheckedContinuation<Int32, Never>?

        func reset() { lock.lock(); code = nil; lock.unlock() }

        /// Valable si le callback arrive avant ou après l'attente.
        func result() async -> Int32 {
            await withCheckedContinuation { c in
                lock.lock()
                if let code { lock.unlock(); c.resume(returning: code) } else { continuation = c; lock.unlock() }
            }
        }

        func onPtrReceive(_ printerObj: Epos2Printer!, code: Int32, status: Epos2PrinterStatusInfo!, printJobId: String!) {
            lock.lock()
            self.code = code
            let c = continuation
            continuation = nil
            lock.unlock()
            c?.resume(returning: code)
        }
    }
}
#endif
