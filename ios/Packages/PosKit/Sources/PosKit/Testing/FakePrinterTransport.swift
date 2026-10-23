import Foundation

/// Transport de test : enregistre les envois, échecs programmables, aucun réseau.
public final class FakePrinterTransport: PrinterTransport, @unchecked Sendable {
    private let lock = NSLock()
    private var _sent: [(printerId: UUID, payload: [UInt8])] = []
    private var pendingFailures: [any Error] = []
    private var alwaysFailing: Set<UUID> = []
    private var offline: Set<UUID> = []
    private var _gate: (@Sendable () async -> Void)?

    public init() {}

    public var sent: [(printerId: UUID, payload: [UInt8])] {
        lock.lock(); defer { lock.unlock() }
        return _sent
    }

    /// Appelé au début de chaque envoi, avant toute décision : permet de simuler un envoi lent.
    public var gate: (@Sendable () async -> Void)? {
        get { lock.lock(); defer { lock.unlock() }; return _gate }
        set { lock.lock(); _gate = newValue; lock.unlock() }
    }

    /// Les `count` prochains envois échouent avec `error`, dans l'ordre.
    public func failNext(_ count: Int, error: any Error = PrinterTransportError.connection("échec programmé")) {
        lock.lock(); pendingFailures.append(contentsOf: Array(repeating: error, count: count)); lock.unlock()
    }

    public func failAlways(for printerId: UUID) {
        lock.lock(); alwaysFailing.insert(printerId); lock.unlock()
    }

    public func setOnline(_ online: Bool, for printerId: UUID) {
        lock.lock()
        if online { offline.remove(printerId); alwaysFailing.remove(printerId) } else { offline.insert(printerId) }
        lock.unlock()
    }

    public func send(_ payload: [UInt8], to printer: Printer) async throws {
        if let gate { await gate() }
        let failure: (any Error)? = {
            lock.lock(); defer { lock.unlock() }
            if offline.contains(printer.id) { return PrinterTransportError.connection("imprimante hors ligne") }
            if alwaysFailing.contains(printer.id) { return PrinterTransportError.connection("échec permanent") }
            if !pendingFailures.isEmpty { return pendingFailures.removeFirst() }
            _sent.append((printer.id, payload))
            return nil
        }()
        if let failure { throw failure }
    }
}
