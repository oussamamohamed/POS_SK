import Foundation
@testable import PosKit

/// Horloge de test, avancée à la main. Par défaut : lundi 12 octobre 2026, 18 h 30 UTC.
final class LocalTestClock: @unchecked Sendable {
    private let lock = NSLock()
    private var current: Date

    init(_ iso: String = "2026-10-12T18:30:00Z") { current = SQLDate.parse(iso) ?? Date() }

    var now: Date {
        lock.lock()
        defer { lock.unlock() }
        return current
    }

    func advance(_ seconds: TimeInterval) {
        lock.lock()
        current = current.addingTimeInterval(seconds)
        lock.unlock()
    }

    func set(_ iso: String) {
        lock.lock()
        current = SQLDate.parse(iso) ?? current
        lock.unlock()
    }
}

/// Calendrier grégorien en UTC : l'heure « locale » des tests ne dépend pas du fuseau de la machine.
let localTestCalendar: Calendar = {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = .gmt
    return calendar
}()

/// API locale en mémoire pilotée par une horloge de test, déjà déverrouillée avec le PIN donné.
func makeLocalAPI(pin: String = "1234", clock: LocalTestClock) async throws -> LocalPosAPI {
    let api = try LocalPosAPI(path: ":memory:", clock: { clock.now }, calendar: localTestCalendar)
    _ = try await api.login(pin: pin)
    return api
}
