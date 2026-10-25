#if os(iOS) && canImport(libepos2)
import Foundation
import libepos2
import PosKit
import PosPrinters

/// Recherche réseau, Bluetooth et USB (imprimantes seulement) pendant `timeout`.
struct EpsonDiscovery: PrinterDiscovery {
    func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter] {
        let delegate = Delegate()
        let filter = Epos2FilterOption()
        filter.portType = Int32(EPOS2_PORTTYPE_ALL.rawValue)
        filter.deviceType = Int32(EPOS2_TYPE_PRINTER.rawValue)
        let code = Epos2Discovery.start(filter, delegate: delegate)
        if let failure = EpsonCodes.failure(returnCode: code) { throw failure }
        // Annulation : on sort du sleep, puis on arrête et on rend les résultats partiels.
        try? await Task.sleep(for: .seconds(timeout))
        await Self.stop()
        return delegate.printers
    }

    /// `Epos2Discovery.stop()` renvoie « en cours » tant que la recherche démarre : on réessaie brièvement.
    private static func stop() async {
        for _ in 0..<20 {
            if Epos2Discovery.stop() != Int32(EPOS2_ERR_PROCESSING.rawValue) { return }
            try? await Task.sleep(for: .milliseconds(100))
        }
    }

    private final class Delegate: NSObject, Epos2DiscoveryDelegate, @unchecked Sendable {
        private let lock = NSLock()
        private var found: [DiscoveredPrinter] = []
        var printers: [DiscoveredPrinter] { lock.lock(); defer { lock.unlock() }; return found }

        func onDiscovery(_ deviceInfo: Epos2DeviceInfo!) {
            guard let target = deviceInfo.target, !target.isEmpty else { return }
            let name = deviceInfo.deviceName ?? ""
            let address = PrinterAddress.epson(series: EpsonCodes.series(forDeviceName: name), target: target)
            let ip = deviceInfo.ipAddress ?? ""
            let item = DiscoveredPrinter(
                transport: .epson, name: name.isEmpty ? "Epson" : name,
                deviceIdentifier: address.deviceIdentifier, ipAddress: ip.isEmpty ? nil : ip)
            lock.lock(); found.append(item); lock.unlock()
        }
    }
}
#endif
