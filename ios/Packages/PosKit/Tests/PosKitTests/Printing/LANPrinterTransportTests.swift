import Foundation
import Network
import Testing
@testable import PosKit

/// Serveur TCP local (127.0.0.1, port éphémère) qui lit jusqu'à la fermeture de la connexion.
final class LoopbackServer: @unchecked Sendable {
    private let listener: NWListener
    private let queue = DispatchQueue(label: "loopback-server")
    private let lock = NSLock()
    private var received = Data()
    private var connections: [NWConnection] = []
    private var continuation: CheckedContinuation<Data, Never>?
    private var finished: Data?
    let readsData: Bool

    private init(listener: NWListener, readsData: Bool) {
        self.listener = listener
        self.readsData = readsData
    }

    static func start(readsData: Bool = true) async throws -> LoopbackServer {
        let params = NWParameters.tcp
        params.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
        let listener = try NWListener(using: params)
        let server = LoopbackServer(listener: listener, readsData: readsData)
        listener.newConnectionHandler = { server.accept($0) }
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
            let once = Once()
            listener.stateUpdateHandler = { state in
                switch state {
                case .ready: if once.take() { c.resume() }
                case .failed(let e): if once.take() { c.resume(throwing: e) }
                default: break
                }
            }
            listener.start(queue: server.queue)
        }
        return server
    }

    var port: UInt16 { listener.port?.rawValue ?? 0 }

    private func accept(_ connection: NWConnection) {
        lock.lock(); connections.append(connection); lock.unlock()
        connection.start(queue: queue)
        if readsData { readLoop(connection) }
    }

    private func readLoop(_ connection: NWConnection) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { [self] data, _, isComplete, error in
            if let data { lock.lock(); received.append(data); lock.unlock() }
            if isComplete || error != nil { finish() } else { readLoop(connection) }
        }
    }

    private func finish() {
        lock.lock()
        finished = received
        let c = continuation
        continuation = nil
        let data = received
        lock.unlock()
        c?.resume(returning: data)
    }

    /// Octets reçus une fois la connexion fermée par le client.
    func receivedAll() async -> Data {
        await withCheckedContinuation { c in
            lock.lock()
            if let finished { lock.unlock(); c.resume(returning: finished) } else { continuation = c; lock.unlock() }
        }
    }

    func stop() {
        listener.cancel()
        lock.lock(); let all = connections; lock.unlock()
        all.forEach { $0.cancel() }
    }
}

final class Once: @unchecked Sendable {
    private let lock = NSLock()
    private var done = false
    func take() -> Bool { lock.lock(); defer { lock.unlock() }; if done { return false }; done = true; return true }
}

@Suite("LANPrinterTransport")
struct LANPrinterTransportTests {
    private func printer(port: UInt16, host: String = "127.0.0.1") -> Printer {
        Printer(name: "Test", ipAddress: host, port: Int(port))
    }

    @Test("envoie exactement les octets (100 Ko) puis ferme la connexion")
    func sendsAllBytes() async throws {
        let server = try await LoopbackServer.start()
        defer { server.stop() }
        let payload = (0..<100_000).map { UInt8(truncatingIfNeeded: $0 &* 31 &+ ($0 >> 8)) }
        try await LANPrinterTransport().send(payload, to: printer(port: server.port))
        let received = await server.receivedAll()
        #expect([UInt8](received) == payload)
    }

    @Test("port fermé : erreur connection immédiate")
    func closedPort() async throws {
        let server = try await LoopbackServer.start()
        let port = server.port
        server.stop()
        let start = Date()
        do {
            try await LANPrinterTransport(connectTimeout: 3).send([1], to: printer(port: port))
            Issue.record("aurait dû échouer")
        } catch let PrinterTransportError.connection(message) {
            #expect(!message.isEmpty)
        } catch {
            Issue.record("erreur inattendue : \(error)")
        }
        // Borne large : le minuteur du transport tourne sur sa propre file ; sous charge, c'est la tâche de test qui est reprogrammée tard.
        // Le délai TCP du système (~75 s) reste exclu.
        #expect(Date().timeIntervalSince(start) < 30)
    }

    @Test("adresse non joignable : connectTimeout rapide")
    func unreachable() async throws {
        let start = Date()
        do {
            try await LANPrinterTransport(connectTimeout: 0.3).send([1], to: printer(port: 9100, host: "192.0.2.1"))
            Issue.record("aurait dû échouer")
        } catch let error as PrinterTransportError {
            // Un bac à sable sans route peut répondre « réseau injoignable » avant le délai.
            if case .connection = error {} else { #expect(error == .connectTimeout) }
        }
        // Borne large : le minuteur du transport tourne sur sa propre file ; sous charge, c'est la tâche de test qui est reprogrammée tard.
        // Le délai TCP du système (~75 s) reste exclu.
        #expect(Date().timeIntervalSince(start) < 30)
    }

    @Test("écriture bloquée : writeTimeout")
    func blockedWrite() async throws {
        let server = try await LoopbackServer.start(readsData: false)
        defer { server.stop() }
        let payload = [UInt8](repeating: 0x41, count: 64 * 1024 * 1024)
        let start = Date()
        await #expect(throws: PrinterTransportError.writeTimeout) {
            try await LANPrinterTransport(connectTimeout: 3, writeTimeout: 0.5).send(payload, to: printer(port: server.port))
        }
        // Borne large : le minuteur du transport tourne sur sa propre file ; sous charge, c'est la tâche de test qui est reprogrammée tard.
        // Le délai TCP du système (~75 s) reste exclu.
        #expect(Date().timeIntervalSince(start) < 30)
    }
}
