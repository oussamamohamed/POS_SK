import Foundation

/// Résultat unique d'un callback Epson converti en `async` : première valeur gagnante, reprise unique.
/// `finish` est idempotent ; l'annulation de l'attente et `close()` de la session reprennent avec un code de délai.
public final class EpsonCallbackGate: @unchecked Sendable {
    private let lock = NSLock()
    private var code: Int32?
    private var continuation: CheckedContinuation<Int32, Never>?

    public init() {}

    /// Efface un résultat précédent (avant un nouvel envoi).
    public func reset() { lock.lock(); code = nil; lock.unlock() }

    /// Dépose le résultat et reprend l'attente éventuelle ; sans effet si un résultat existe déjà.
    public func finish(_ value: Int32) {
        lock.lock()
        guard code == nil else { lock.unlock(); return }
        code = value
        let c = continuation
        continuation = nil
        lock.unlock()
        c?.resume(returning: value)
    }

    /// Valable si `finish` arrive avant ou après l'attente ; annulée, rend `onCancel`.
    public func result(onCancel: Int32) async -> Int32 {
        await withTaskCancellationHandler {
            await withCheckedContinuation { c in
                lock.lock()
                if let code { lock.unlock(); c.resume(returning: code) } else { continuation = c; lock.unlock() }
            }
        } onCancel: {
            finish(onCancel)
        }
    }
}
