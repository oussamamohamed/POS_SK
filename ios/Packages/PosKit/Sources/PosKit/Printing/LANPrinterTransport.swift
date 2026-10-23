import Foundation
import Network

/// TCP brut (port 9100 en général), un `NWConnection` par envoi, comme `EscPosSender` côté .NET.
public struct LANPrinterTransport: PrinterTransport {
    private let connectTimeout: TimeInterval
    private let writeTimeout: TimeInterval

    public init(connectTimeout: TimeInterval = 3, writeTimeout: TimeInterval = 10) {
        self.connectTimeout = connectTimeout
        self.writeTimeout = writeTimeout
    }

    public func send(_ payload: [UInt8], to printer: Printer) async throws {
        guard let port = NWEndpoint.Port(rawValue: UInt16(clamping: printer.port)), printer.port > 0 else {
            throw PrinterTransportError.connection("Port invalide : \(printer.port)")
        }
        let operation = Operation(
            connection: NWConnection(host: NWEndpoint.Host(printer.ipAddress), port: port, using: .tcp),
            payload: payload, connectTimeout: connectTimeout, writeTimeout: writeTimeout)
        try await withTaskCancellationHandler {
            try await operation.run()
        } onCancel: {
            operation.cancel()
        }
    }
}

/// Un envoi. Tout l'état est confiné à `queue` (file série) : d'où le `@unchecked Sendable`.
private final class Operation: @unchecked Sendable {
    private let connection: NWConnection
    private let payload: [UInt8]
    private let connectTimeout: TimeInterval
    private let writeTimeout: TimeInterval
    private let queue = DispatchQueue(label: "LANPrinterTransport")
    private var continuation: CheckedContinuation<Void, Error>?
    private var finished = false
    private var timer: DispatchWorkItem?

    init(connection: NWConnection, payload: [UInt8], connectTimeout: TimeInterval, writeTimeout: TimeInterval) {
        self.connection = connection
        self.payload = payload
        self.connectTimeout = connectTimeout
        self.writeTimeout = writeTimeout
    }

    func run() async throws {
        try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
            queue.async { [self] in
                if finished { c.resume(throwing: CancellationError()); return }
                continuation = c
                connection.stateUpdateHandler = { [self] state in handle(state) }
                arm(connectTimeout, error: .connectTimeout)
                connection.start(queue: queue)
            }
        }
    }

    func cancel() {
        queue.async { [self] in finish(CancellationError()) }
    }

    private func handle(_ state: NWConnection.State) {
        switch state {
        case .ready:
            arm(writeTimeout, error: .writeTimeout)
            connection.send(content: Data(payload), contentContext: .finalMessage, isComplete: true, completion: .contentProcessed { [self] error in
                finish(error.map { PrinterTransportError.connection($0.localizedDescription) })
            })
        case .waiting(let error):
            // Refus immédiat : inutile d'attendre le délai. Autres cas (pas de route…) : le délai tranche.
            if case .posix(let code) = error, code == .ECONNREFUSED {
                finish(PrinterTransportError.connection(error.localizedDescription))
            }
        case .failed(let error):
            finish(PrinterTransportError.connection(error.localizedDescription))
        case .cancelled:
            finish(CancellationError())
        default:
            break
        }
    }

    private func arm(_ seconds: TimeInterval, error: PrinterTransportError) {
        timer?.cancel()
        let item = DispatchWorkItem { [self] in finish(error) }
        timer = item
        queue.asyncAfter(deadline: .now() + seconds, execute: item)
    }

    private func finish(_ error: Error?) {
        guard !finished else { return }
        finished = true
        timer?.cancel()
        connection.stateUpdateHandler = nil
        connection.cancel()
        let c = continuation
        continuation = nil
        if let error { c?.resume(throwing: error) } else { c?.resume() }
    }
}
