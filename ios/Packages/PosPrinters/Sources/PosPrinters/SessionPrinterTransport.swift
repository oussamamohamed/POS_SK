import Foundation
import PosKit

/// Transport commun Star/Epson : parse l'adresse, crée la session, open → send → close (close toujours), traduit les erreurs.
public struct SessionPrinterTransport: PrinterTransport {
    private let connectTimeout: TimeInterval
    private let writeTimeout: TimeInterval
    private let makeSession: @Sendable (PrinterAddress) throws -> any PrinterSession

    public init(connectTimeout: TimeInterval = 3, writeTimeout: TimeInterval = 10,
                makeSession: @escaping @Sendable (PrinterAddress) throws -> any PrinterSession) {
        self.connectTimeout = connectTimeout
        self.writeTimeout = writeTimeout
        self.makeSession = makeSession
    }

    public func send(_ payload: [UInt8], to printer: Printer) async throws {
        let session = try makeSession(PrinterAddress.parse(printer))
        do {
            try await Self.race(connectTimeout, onTimeout: .connectTimeout) { try await session.open(timeout: connectTimeout) }
            try await Self.race(writeTimeout, onTimeout: .writeTimeout) { try await session.send(payload, timeout: writeTimeout) }
        } catch {
            await closeBounded(session)
            throw error
        }
        await closeBounded(session)
    }

    /// Fermeture bornée : un `close()` muet du SDK ne doit ni bloquer la file de l'imprimante ni changer le résultat de l'envoi.
    private func closeBounded(_ session: any PrinterSession) async {
        try? await Self.race(connectTimeout, onTimeout: .connectTimeout) { await session.close() }
    }

    /// Course entre l'appel de session et un délai. Le premier qui finit gagne ; l'autre est annulé mais
    /// jamais attendu : une session muette qui ignore l'annulation ne bloque pas l'appelant
    /// (un `withThrowingTaskGroup` attendrait ses enfants en sortie).
    private static func race(_ seconds: TimeInterval, onTimeout: PrinterTransportError,
                             _ operation: @escaping @Sendable () async throws -> Void) async throws {
        let race = Race()
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (c: CheckedContinuation<Void, Error>) in
                race.start(c, operation: {
                    do { try await operation() } catch { throw translate(error, timeout: onTimeout) }
                }, timeout: seconds, onTimeout: onTimeout)
            }
        } onCancel: {
            race.finish(.failure(CancellationError()))
        }
    }

    private static func translate(_ error: Error, timeout: PrinterTransportError) -> Error {
        switch error {
        case PrinterSessionFailure.timeout: timeout
        case let PrinterSessionFailure.failed(message): PrinterTransportError.connection(message)
        case is CancellationError, is PrinterTransportError: error
        default: PrinterTransportError.connection(error.localizedDescription)
        }
    }
}

/// État d'une course ; `lock` protège `continuation` et `tasks` (d'où le `@unchecked Sendable`).
private final class Race: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Void, Error>?
    private var tasks: [Task<Void, Never>] = []
    private var done = false

    func start(_ c: CheckedContinuation<Void, Error>, operation: @escaping @Sendable () async throws -> Void,
               timeout: TimeInterval, onTimeout: PrinterTransportError) {
        lock.lock()
        if done { lock.unlock(); c.resume(throwing: CancellationError()); return }
        continuation = c
        lock.unlock()
        let work = Task { [self] in
            do { try await operation(); finish(.success(())) } catch { finish(.failure(error)) }
        }
        let timer = Task { [self] in
            try? await Task.sleep(for: .seconds(timeout))
            finish(.failure(onTimeout))
        }
        lock.lock()
        let late = done
        tasks = [work, timer]
        lock.unlock()
        if late { work.cancel(); timer.cancel() }
    }

    func finish(_ result: Result<Void, Error>) {
        lock.lock()
        let c = continuation
        let pending = tasks
        let first = !done
        done = true
        continuation = nil
        lock.unlock()
        guard first else { return }
        pending.forEach { $0.cancel() }
        c?.resume(with: result)
    }
}
