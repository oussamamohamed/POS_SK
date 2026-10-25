#if os(iOS)
import Foundation
import PosKit
import PosPrinters
import StarIO10

public enum StarPrinters {
    public static func transport() -> SessionPrinterTransport {
        SessionPrinterTransport { address in
            guard case let .star(interface, identifier) = address else {
                throw PrinterTransportError.connection("Adresse Star attendue")
            }
            return StarSession(interface: interface, identifier: identifier)
        }
    }

    public static func discovery(interfaces: [StarInterface] = [.lan, .bluetooth, .bluetoothLE, .usb]) -> any PrinterDiscovery {
        StarDiscovery(interfaces: interfaces)
    }
}

struct StarDiscovery: PrinterDiscovery {
    let interfaces: [StarInterface]

    func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter] {
        let types: [InterfaceType] = interfaces.map {
            switch $0 {
            case .lan: .lan
            case .bluetooth: .bluetooth
            case .bluetoothLE: .bluetoothLE
            case .usb: .usb
            }
        }
        let delegate = Delegate()
        let manager = try StarDeviceDiscoveryManagerFactory.create(interfaceTypes: types)
        manager.discoveryTime = Int(timeout * 1000)
        manager.delegate = delegate // faible côté SDK : `delegate` reste vivant jusqu'à la fin de la recherche
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                delegate.start(manager, continuation)
            }
        } onCancel: {
            delegate.stop()
        }
    }

    /// Délégué du SDK converti en reprise unique de continuation ; `lock` protège l'état (d'où `@unchecked Sendable`).
    private final class Delegate: NSObject, StarDeviceDiscoveryManagerDelegate, @unchecked Sendable {
        private let lock = NSLock()
        private var found: [DiscoveredPrinter] = []
        private var continuation: CheckedContinuation<[DiscoveredPrinter], Error>?
        private nonisolated(unsafe) var running: (any StarDeviceDiscoveryManager)?
        private var cancelled = false

        func start(_ manager: any StarDeviceDiscoveryManager, _ c: CheckedContinuation<[DiscoveredPrinter], Error>) {
            lock.lock(); continuation = c; running = manager; let skip = cancelled; lock.unlock()
            // Annulée avant le départ : on rend tout de suite ce qu'on a, sans lancer la recherche.
            if skip { finish(.success([])); return }
            do { try manager.startDiscovery() } catch { finish(.failure(error)) }
        }

        /// Annulation : on ne suppose pas que `stopDiscovery` déclenche `managerDidFinishDiscovery` ; `finish` est idempotent
        /// et rend les résultats partiels.
        func stop() {
            lock.lock(); cancelled = true; let m = running; let all = found; lock.unlock()
            m?.stopDiscovery()
            finish(.success(all))
        }

        func manager(_ manager: any StarDeviceDiscoveryManager, didFind printer: StarPrinter) {
            let settings = printer.connectionSettings
            let interface: StarInterface
            switch settings.interfaceType {
            case .lan: interface = .lan
            case .bluetooth: interface = .bluetooth
            case .bluetoothLE: interface = .bluetoothLE
            case .usb: interface = .usb
            case .unknown: return
            @unknown default: return
            }
            let item = DiscoveredPrinter(
                transport: .star,
                name: printer.information?.model.description ?? "Star",
                deviceIdentifier: PrinterAddress.star(interface: interface, identifier: settings.identifier).deviceIdentifier,
                ipAddress: interface == .lan ? settings.identifier : nil)
            lock.lock(); found.append(item); lock.unlock()
        }

        func managerDidFinishDiscovery(_ manager: any StarDeviceDiscoveryManager) {
            lock.lock(); let all = found; lock.unlock()
            finish(.success(all))
        }

        private func finish(_ result: Result<[DiscoveredPrinter], Error>) {
            lock.lock(); let c = continuation; continuation = nil; lock.unlock()
            c?.resume(with: result)
        }
    }
}
#endif
