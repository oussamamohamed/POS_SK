import Foundation
import PosKit
import Testing
@testable import PosPrinters

private func found(_ id: String, _ kind: PrinterTransportKind = .star, ip: String? = nil) -> DiscoveredPrinter {
    DiscoveredPrinter(transport: kind, name: "M-\(id)", deviceIdentifier: id, ipAddress: ip)
}

@MainActor @Suite struct PrinterDiscoveryStoreTests {
    @Test func rechercheAboutitAvecLesResultats() async {
        let store = PrinterDiscoveryStore(discovery: FakePrinterDiscovery([found("a"), found("b")]))
        #expect(store.phase == .idle)
        await store.search()
        #expect(store.phase == .done)
        #expect(store.results.map(\.deviceIdentifier) == ["a", "b"])
    }

    @Test func phaseSearchingPendantLaRecherche() async {
        let store = PrinterDiscoveryStore(discovery: FakePrinterDiscovery([found("a")], delay: 0.1))
        let task = Task { await store.search() }
        try? await Task.sleep(for: .milliseconds(30))
        #expect(store.phase == .searching)
        await task.value
        #expect(store.phase == .done)
    }

    @Test func zeroResultatEstDone() async {
        let store = PrinterDiscoveryStore(discovery: FakePrinterDiscovery([]))
        await store.search()
        #expect(store.phase == .done)
        #expect(store.results.isEmpty)
    }

    @Test func seuleLaDerniereRechercheCompte() async {
        let slow = SwitchingDiscovery([([found("lent")], 0.2), ([found("rapide")], 0)])
        let store = PrinterDiscoveryStore(discovery: slow)
        let first = Task { await store.search() }
        try? await Task.sleep(for: .milliseconds(30))
        await store.search()
        await first.value
        #expect(store.phase == .done)
        #expect(store.results.map(\.deviceIdentifier) == ["rapide"])
    }

    @Test func annulerRevientAIdleSansMiseAJourTardive() async {
        let store = PrinterDiscoveryStore(discovery: FakePrinterDiscovery([found("a")], delay: 0.1))
        let task = Task { await store.search() }
        try? await Task.sleep(for: .milliseconds(30))
        store.cancel()
        #expect(store.phase == .idle)
        await task.value
        try? await Task.sleep(for: .milliseconds(150))
        #expect(store.phase == .idle)
        #expect(store.results.isEmpty)
    }

    @Test func brouillonStarBluetooth() {
        let store = PrinterDiscoveryStore(discovery: FakePrinterDiscovery([]))
        let p = store.draft(from: found("star:bluetooth:ABC", .star))
        #expect(p.name == "M-star:bluetooth:ABC")
        #expect(p.ipAddress == "")
        #expect(p.transport == .star)
        #expect(p.deviceIdentifier == "star:bluetooth:ABC")
        #expect(p.paperWidthMm == 80)
        #expect(p.assignedStationIds == ["RECEIPT"])
    }

    @Test func brouillonEpsonReseau() {
        let store = PrinterDiscoveryStore(discovery: FakePrinterDiscovery([]))
        let p = store.draft(from: found("epson:TCP:1", .epson, ip: "192.168.1.50"))
        #expect(p.ipAddress == "192.168.1.50")
        #expect(p.transport == .epson)
    }
}

/// Renvoie, à chaque appel, la réponse suivante (après son délai).
private final class SwitchingDiscovery: PrinterDiscovery, @unchecked Sendable {
    private let lock = NSLock()
    private var steps: [([DiscoveredPrinter], TimeInterval)]
    init(_ steps: [([DiscoveredPrinter], TimeInterval)]) { self.steps = steps }
    func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter] {
        let step = lock.withLock { steps.removeFirst() }
        try await Task.sleep(for: .seconds(step.1))
        return step.0
    }
}
