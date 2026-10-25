import Foundation

/// Recherche simulée (tests et `-UITestMode`) : rend `found` après `delay` secondes ; l'annulation interrompt l'attente.
public struct FakePrinterDiscovery: PrinterDiscovery {
    private let found: [DiscoveredPrinter]
    private let delay: TimeInterval

    public init(_ found: [DiscoveredPrinter], delay: TimeInterval = 0) {
        self.found = found
        self.delay = delay
    }

    public func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter] {
        if delay > 0 { try await Task.sleep(for: .seconds(delay)) }
        return found
    }
}
