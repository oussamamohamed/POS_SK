import Foundation
import PosKit
import Testing
@testable import PosPrinters

private actor FakeSession: PrinterSession {
    enum Behavior: Sendable { case ok, hang, fail(any Error) }
    private(set) var events: [String] = []
    private(set) var sent: [UInt8] = []
    let openBehavior: Behavior, sendBehavior: Behavior, closeBehavior: Behavior

    init(open: Behavior = .ok, send: Behavior = .ok, close: Behavior = .ok) {
        openBehavior = open; sendBehavior = send; closeBehavior = close
    }

    func open(timeout: TimeInterval) async throws { events.append("open"); try await run(openBehavior) }
    func send(_ payload: [UInt8], timeout: TimeInterval) async throws { events.append("send"); sent = payload; try await run(sendBehavior) }
    func close() async { events.append("close"); try? await run(closeBehavior) }

    private func run(_ b: Behavior) async throws {
        switch b {
        case .ok: return
        case let .fail(e): throw e
        // Session muette qui ignore l'annulation : seul le transport peut rendre la main.
        case .hang: await withCheckedContinuation { (_: CheckedContinuation<Void, Never>) in }
        }
    }
}

private struct Boom: Error, LocalizedError { var errorDescription: String? { "boom" } }

@Suite struct SessionPrinterTransportTests {
    private let printer = Printer(name: "P", ipAddress: "10.0.0.9", transport: .star, deviceIdentifier: "usb:SN1")

    private func transport(_ s: FakeSession, timeout: TimeInterval = 0.2) -> SessionPrinterTransport {
        SessionPrinterTransport(connectTimeout: timeout, writeTimeout: timeout) { _ in s }
    }

    private func failure(_ s: FakeSession) async -> PrinterTransportError? {
        let started = Date()
        do { try await transport(s).send([1, 2], to: printer) } catch {
            #expect(Date().timeIntervalSince(started) < 2)
            return error as? PrinterTransportError
        }
        return nil
    }

    @Test func successOpensSendsClosesInOrder() async throws {
        let s = FakeSession()
        try await transport(s).send([0x1B, 0x40, 0x0A], to: printer)
        #expect(await s.events == ["open", "send", "close"])
        #expect(await s.sent == [0x1B, 0x40, 0x0A])
    }

    @Test func hungCloseDoesNotBlockSuccess() async throws {
        let s = FakeSession(close: .hang)
        let started = Date()
        try await transport(s).send([1], to: printer)
        #expect(Date().timeIntervalSince(started) < 2)
        #expect(await s.events == ["open", "send", "close"])
    }

    @Test func hungCloseKeepsTheSameFailure() async {
        let s = FakeSession(send: .fail(PrinterSessionFailure.failed("papier")), close: .hang)
        #expect(await failure(s) == .connection("papier"))
    }

    @Test func openFailureStillCloses() async {
        let s = FakeSession(open: .fail(Boom()))
        #expect(await failure(s) == .connection("boom"))
        #expect(await s.events == ["open", "close"])
    }

    @Test func sendFailureCloses() async {
        let s = FakeSession(send: .fail(PrinterSessionFailure.failed("papier")))
        #expect(await failure(s) == .connection("papier"))
        #expect(await s.events == ["open", "send", "close"])
    }

    @Test func silentOpenGivesConnectTimeoutAndCloses() async {
        let s = FakeSession(open: .hang)
        #expect(await failure(s) == .connectTimeout)
        #expect(await s.events == ["open", "close"])
    }

    @Test func silentSendGivesWriteTimeoutAndCloses() async {
        let s = FakeSession(send: .hang)
        #expect(await failure(s) == .writeTimeout)
        #expect(await s.events == ["open", "send", "close"])
    }

    @Test func sessionTimeoutIsMappedPerPhase() async {
        let o = FakeSession(open: .fail(PrinterSessionFailure.timeout))
        #expect(await failure(o) == .connectTimeout)
        let w = FakeSession(send: .fail(PrinterSessionFailure.timeout))
        #expect(await failure(w) == .writeTimeout)
    }

    @Test func makeSessionErrorPropagatesWithoutSession() async {
        let t = SessionPrinterTransport { _ in throw Boom() }
        await #expect(throws: Boom.self) { try await t.send([1], to: printer) }
    }

    @Test func invalidAddressCreatesNoSession() async {
        let created = Counter()
        let t = SessionPrinterTransport { _ in created.bump(); return FakeSession() }
        var bad = printer
        bad.deviceIdentifier = "nope"
        await #expect(throws: PrinterTransportError.connection("Identifiant d'imprimante invalide : nope")) {
            try await t.send([1], to: bad)
        }
        #expect(created.value == 0)
    }
}

private final class Counter: @unchecked Sendable {
    private let lock = NSLock()
    private var n = 0
    var value: Int { lock.lock(); defer { lock.unlock() }; return n }
    func bump() { lock.lock(); n += 1; lock.unlock() }
}
