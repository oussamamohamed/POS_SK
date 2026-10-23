import Foundation
import Testing
@testable import PosKit

@Suite("RoutingPrinterTransport et FakePrinterTransport")
struct RoutingPrinterTransportTests {
    private func printer(_ kind: PrinterTransportKind) -> Printer {
        Printer(name: "P", ipAddress: "10.0.0.1", transport: kind)
    }

    @Test("lan aiguillé vers le transport LAN injecté")
    func routesLan() async throws {
        let lan = FakePrinterTransport()
        let routing = RoutingPrinterTransport(lan: lan)
        let p = printer(.lan)
        try await routing.send([1, 2], to: p)
        #expect(lan.sent.count == 1)
        #expect(lan.sent[0].printerId == p.id)
        #expect(lan.sent[0].payload == [1, 2])
    }

    @Test("star sans transport enregistré : unsupported")
    func unsupportedStar() async {
        let routing = RoutingPrinterTransport(lan: FakePrinterTransport())
        await #expect(throws: PrinterTransportError.unsupported(.star)) {
            try await routing.send([1], to: printer(.star))
        }
        await #expect(throws: PrinterTransportError.unsupported(.epson)) {
            try await routing.send([1], to: printer(.epson))
        }
    }

    @Test("star enregistré : appelé avec la même imprimante, pas le LAN")
    func routesStar() async throws {
        let lan = FakePrinterTransport()
        let star = FakePrinterTransport()
        let epson = FakePrinterTransport()
        let routing = RoutingPrinterTransport(lan: lan, star: star, epson: epson)
        let p = printer(.star)
        try await routing.send([9], to: p)
        #expect(star.sent.map(\.printerId) == [p.id])
        #expect(lan.sent.isEmpty && epson.sent.isEmpty)
    }

    @Test("échecs programmés consommés dans l'ordre")
    func failNextConsumedInOrder() async throws {
        let fake = FakePrinterTransport()
        fake.failNext(2, error: PrinterTransportError.connectTimeout)
        let p = printer(.lan)
        await #expect(throws: PrinterTransportError.connectTimeout) { try await fake.send([1], to: p) }
        await #expect(throws: PrinterTransportError.connectTimeout) { try await fake.send([2], to: p) }
        try await fake.send([3], to: p)
        #expect(fake.sent.map(\.payload) == [[3]])
    }

    @Test("failAlways et setOnline par imprimante")
    func perPrinterFailures() async throws {
        let fake = FakePrinterTransport()
        let a = printer(.lan), b = printer(.lan)
        fake.failAlways(for: a.id)
        await #expect(throws: PrinterTransportError.self) { try await fake.send([1], to: a) }
        try await fake.send([1], to: b)
        fake.setOnline(false, for: b.id)
        await #expect(throws: PrinterTransportError.self) { try await fake.send([2], to: b) }
        fake.setOnline(true, for: b.id)
        try await fake.send([3], to: b)
        #expect(fake.sent.map(\.payload) == [[1], [3]])
    }

    @Test("gate suspend l'envoi jusqu'à sa libération")
    func gateSuspends() async throws {
        let fake = FakePrinterTransport()
        let gate = AsyncGate()
        fake.gate = { await gate.wait() }
        let p = printer(.lan)
        let task = Task { try await fake.send([1], to: p) }
        await gate.waitUntilWaiting()
        #expect(fake.sent.isEmpty)
        gate.open()
        try await task.value
        #expect(fake.sent.count == 1)
    }
}

/// Barrière asynchrone : `wait()` suspend jusqu'à `open()`.
final class AsyncGate: @unchecked Sendable {
    private let lock = NSLock()
    private var opened = false
    private var waiter: CheckedContinuation<Void, Never>?
    private var observer: CheckedContinuation<Void, Never>?

    func wait() async {
        await withCheckedContinuation { c in
            lock.lock()
            if opened { lock.unlock(); c.resume(); return }
            waiter = c
            let o = observer; observer = nil
            lock.unlock()
            o?.resume()
        }
    }

    func waitUntilWaiting() async {
        await withCheckedContinuation { c in
            lock.lock()
            if waiter != nil { lock.unlock(); c.resume() } else { observer = c; lock.unlock() }
        }
    }

    func open() {
        lock.lock(); opened = true; let w = waiter; waiter = nil; lock.unlock()
        w?.resume()
    }
}
