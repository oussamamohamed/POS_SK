import Foundation
import Observation

/// Réservations du jour affiché, liste d'attente et réservations à placer.
@MainActor @Observable
public final class BookingsStore {
    public private(set) var reservations: [Reservation] = []
    public private(set) var isLoading = false
    /// Début du jour affiché (calendrier local).
    public private(set) var day: Date
    /// Réservations en retard + clients en liste d'attente aujourd'hui : pastille de la navigation.
    public private(set) var attentionCount = 0
    /// Heure de la prochaine réservation attendue de chaque table, aujourd'hui : repère « Réservée hh:mm » du plan de salle.
    public private(set) var heldByTable: [String: Date] = [:]

    private let api: PosAPI
    private let notifier: Notifier
    private let session: SessionStore
    private let calendar: Calendar
    private let now: () -> Date

    public init(api: PosAPI, notifier: Notifier, session: SessionStore, calendar: Calendar = .autoupdatingCurrent, now: @escaping () -> Date = { Date() }) {
        self.api = api
        self.notifier = notifier
        self.session = session
        self.calendar = calendar
        self.now = now
        day = calendar.startOfDay(for: now())
    }

    public var isToday: Bool { calendar.isDate(day, inSameDayAs: now()) }

    /// Réservations attendues ou installées (hors liste d'attente), par heure.
    public var scheduled: [Reservation] {
        reservations.filter { !$0.isWaitlist && ($0.status == .booked || $0.status == .seated) }
    }
    public var waitlist: [Reservation] { reservations.filter { $0.isWaitlist && $0.status == .booked } }
    /// Attendues mais sans table.
    public var unplaced: [Reservation] { reservations.filter { !$0.isWaitlist && $0.tableNumber == nil && $0.status == .booked } }
    public var lateCount: Int { reservations.filter(\.isLate).count }
    public var coversCount: Int { scheduled.reduce(0) { $0 + $1.covers } }

    private func bounds(of day: Date) -> (Date, Date) {
        let start = calendar.startOfDay(for: day)
        return (start, calendar.date(byAdding: .day, value: 1, to: start) ?? start.addingTimeInterval(86_400))
    }

    public func load() async {
        isLoading = true
        defer { isLoading = false }
        let (from, to) = bounds(of: day)
        do {
            reservations = try await api.reservations(from: from, to: to)
            if isToday { apply(today: reservations) }
        } catch {
            notifier.error(L10n.string("bookings.load_error"))
        }
    }

    /// Met à jour la pastille d'aujourd'hui sans changer le jour affiché.
    public func refreshAttention() async {
        let (from, to) = bounds(of: now())
        guard let today = try? await api.reservations(from: from, to: to) else { return }
        apply(today: today)
    }

    private func apply(today: [Reservation]) {
        attentionCount = today.filter { $0.isLate || ($0.isWaitlist && $0.status == .booked) }.count
        let current = now()
        var held: [String: Date] = [:]
        for r in today.sorted(by: { $0.startsAtUtc < $1.startsAtUtc }) where !r.isWaitlist && r.status == .booked && r.endsAtUtc > current {
            if let table = r.tableNumber, held[table] == nil { held[table] = r.startsAtUtc }
        }
        heldByTable = held
    }

    public func shiftDay(by days: Int) async {
        day = calendar.date(byAdding: .day, value: days, to: day) ?? day
        await load()
    }

    public func goToToday() async {
        day = calendar.startOfDay(for: now())
        await load()
    }

    public func setDay(_ date: Date) async {
        day = calendar.startOfDay(for: date)
        await load()
    }

    /// Valide comme le serveur (mêmes messages) puis crée (`editing` nil) ou modifie. Retourne `nil` et prévient si refus.
    @discardableResult
    public func save(_ draft: ReservationDraft, editing id: UUID?) async -> Reservation? {
        do {
            _ = try ReservationRules.validate(draft)
        } catch let error as ReservationError {
            notifier.warning(error.apiError.errorDescription ?? "")
            return nil
        } catch { return nil }
        do {
            let saved: Reservation
            if let id { saved = try await api.updateReservation(id: id, draft) } else { saved = try await api.createReservation(draft) }
            notifier.success(L10n.string("bookings.saved_toast"))
            await load()
            return saved
        } catch {
            notifier.error(error)
            return nil
        }
    }

    @discardableResult
    public func seat(_ reservation: Reservation, table: String?) async -> Bool {
        do {
            let seated = try await api.seatReservation(id: reservation.id, tableNumber: table, operatorId: session.currentOperator?.id, waiterName: session.currentOperator?.name)
            notifier.success(String(format: L10n.string("bookings.seated_toast"), seated.tableNumber ?? ""))
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    @discardableResult
    public func setStatus(_ reservation: Reservation, _ status: ReservationStatus) async -> Bool {
        do {
            _ = try await api.setReservationStatus(id: reservation.id, status: status)
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }
}

public enum MenuRange: Int, CaseIterable, Identifiable, Sendable {
    case today = 1, week = 7, month = 30
    public var id: Int { rawValue }
}

/// Analyse du menu (popularité × marge) sur la période choisie ; réservée aux responsables.
@MainActor @Observable
public final class MenuAnalysisStore {
    public var range: MenuRange = .week
    public var filter: MenuClass?
    public private(set) var report: MenuAnalysisReport?
    public private(set) var isLoading = false
    public private(set) var isForbidden = false

    private let api: PosAPI
    private let notifier: Notifier
    private let calendar: Calendar
    private let now: () -> Date

    public init(api: PosAPI, notifier: Notifier, calendar: Calendar = .autoupdatingCurrent, now: @escaping () -> Date = { Date() }) {
        self.api = api
        self.notifier = notifier
        self.calendar = calendar
        self.now = now
    }

    public var items: [MenuAnalysisItem] {
        guard let report else { return [] }
        guard let filter else { return report.items }
        return report.items.filter { $0.class == filter }
    }

    public func count(_ menuClass: MenuClass) -> Int { report?.items.filter { $0.class == menuClass }.count ?? 0 }

    public func toggleFilter(_ menuClass: MenuClass) { filter = filter == menuClass ? nil : menuClass }

    public func load() async {
        isLoading = true
        isForbidden = false
        defer { isLoading = false }
        let end = now()
        let start = range == .today ? calendar.startOfDay(for: end) : (calendar.date(byAdding: .day, value: -range.rawValue, to: end) ?? end)
        do {
            report = try await api.menuAnalysis(from: start, to: end)
        } catch let error as APIError {
            if case .forbidden = error { isForbidden = true; report = nil } else { notifier.error(error) }
        } catch {
            notifier.error(error)
        }
    }
}
