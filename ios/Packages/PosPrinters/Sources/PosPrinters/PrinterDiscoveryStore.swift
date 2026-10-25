import Foundation
import Observation
import PosKit

/// État d'une recherche d'imprimantes pour l'écran Imprimantes.
@MainActor @Observable
public final class PrinterDiscoveryStore {
    public enum Phase: Equatable, Sendable { case idle, searching, done }

    public private(set) var phase: Phase = .idle
    public private(set) var results: [DiscoveredPrinter] = []

    private let discovery: any PrinterDiscovery
    private let timeout: TimeInterval
    private var task: Task<[DiscoveredPrinter], Never>?
    /// Numéro de la recherche courante : une recherche remplacée ou annulée ne touche plus l'état.
    private var generation = 0

    public init(discovery: any PrinterDiscovery, timeout: TimeInterval = 10) {
        self.discovery = discovery
        self.timeout = timeout
    }

    /// Lance (ou relance : annule la précédente). Une source en échec est déjà absorbée par `CombinedPrinterDiscovery`.
    public func search() async {
        task?.cancel()
        generation += 1
        let mine = generation
        phase = .searching
        results = []
        let discovery = discovery, timeout = timeout
        let current = Task { (try? await discovery.discover(timeout: timeout)) ?? [] }
        task = current
        // Appelant annulé (vue disparue) : on annule la recherche et on traite comme `cancel()`.
        let found = await withTaskCancellationHandler { await current.value } onCancel: { current.cancel() }
        guard mine == generation else { return }
        if Task.isCancelled { cancel(); return }
        results = found
        phase = .done
    }

    /// Phase → idle, résultats conservés ; la recherche en cours n'écrira plus rien.
    public func cancel() {
        task?.cancel()
        task = nil
        generation += 1
        phase = .idle
    }

    /// Brouillon pour l'éditeur : nom = modèle, ip = `ipAddress ?? ""`, transport, identifiant, papier 80 mm, poste RECEIPT.
    public func draft(from found: DiscoveredPrinter) -> Printer {
        Printer(
            name: found.name, ipAddress: found.ipAddress ?? "", paperWidthMm: 80,
            assignedStationIds: ["RECEIPT"], transport: found.transport, deviceIdentifier: found.deviceIdentifier)
    }
}
