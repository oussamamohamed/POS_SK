import Foundation
import PosKit
import Testing
@testable import PosPrinters

private struct FakeDiscovery: PrinterDiscovery {
    var result: Result<[DiscoveredPrinter], any Error>
    var delay: Duration = .zero
    func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter] {
        try await Task.sleep(for: delay)
        return try result.get()
    }
}

private struct Boom: Error {}

private func printer(_ id: String, _ kind: PrinterTransportKind = .star) -> DiscoveredPrinter {
    DiscoveredPrinter(transport: kind, name: "M-\(id)", deviceIdentifier: id, ipAddress: nil)
}

@Suite struct PrinterDiscoveryTests {
    @Test func fusionneDansLOrdreDesSources() async throws {
        let all = try await CombinedPrinterDiscovery([
            FakeDiscovery(result: .success([printer("a"), printer("b")])),
            FakeDiscovery(result: .success([printer("c", .epson)]))
        ]).discover(timeout: 1)
        #expect(all.map(\.deviceIdentifier) == ["a", "b", "c"])
    }

    @Test func dedoublonneParDeviceIdentifier() async throws {
        let all = try await CombinedPrinterDiscovery([
            FakeDiscovery(result: .success([printer("a"), printer("a")])),
            FakeDiscovery(result: .success([printer("b"), printer("a")]))
        ]).discover(timeout: 1)
        #expect(all.map(\.deviceIdentifier) == ["a", "b"])
    }

    @Test func sourceEnEchecIgnoree() async throws {
        let all = try await CombinedPrinterDiscovery([
            FakeDiscovery(result: .failure(Boom())),
            FakeDiscovery(result: .success([printer("a")]))
        ]).discover(timeout: 1)
        #expect(all.map(\.deviceIdentifier) == ["a"])
    }

    @Test func ordreDeSourceMemeSiLaPremiereEstLente() async throws {
        let all = try await CombinedPrinterDiscovery([
            FakeDiscovery(result: .success([printer("slow")]), delay: .milliseconds(100)),
            FakeDiscovery(result: .success([printer("fast")]))
        ]).discover(timeout: 1)
        #expect(all.map(\.deviceIdentifier) == ["slow", "fast"])
    }

    @Test func sansSourceRendVide() async throws {
        #expect(try await CombinedPrinterDiscovery([]).discover(timeout: 1).isEmpty)
    }
}
