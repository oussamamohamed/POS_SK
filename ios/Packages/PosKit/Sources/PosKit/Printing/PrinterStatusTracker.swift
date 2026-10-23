import Foundation

/// `isOnline == nil` : aucun envoi depuis le démarrage.
struct PrinterState: Equatable, Sendable {
    var isOnline: Bool?
    var sinceUtc: Date?
}

/// État en ligne des imprimantes, en mémoire (comme `PrinterStatusTracker` du .NET).
struct PrinterStatusTracker: Sendable {
    private var states: [UUID: PrinterState] = [:]

    func get(_ id: UUID) -> PrinterState { states[id] ?? PrinterState() }

    /// true si la transition doit être notifiée (en ligne ↔ hors ligne, ou premier échec) ; le premier succès est silencieux.
    mutating func record(_ id: UUID, success: Bool, at: Date) -> Bool {
        let previous = get(id)
        if previous.isOnline == success { return false }
        states[id] = PrinterState(isOnline: success, sinceUtc: at)
        return previous.isOnline != nil || !success
    }
}
