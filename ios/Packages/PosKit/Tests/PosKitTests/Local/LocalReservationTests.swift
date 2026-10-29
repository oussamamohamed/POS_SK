import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : réservations")
struct LocalReservationTests {
    private func draft(_ clock: LocalTestClock, hours: Double = 1, table: String? = "T1", covers: Int = 2, phone: String? = nil, waitlist: Bool = false, name: String = "Martin") -> ReservationDraft {
        ReservationDraft(guestName: name, covers: covers, startsAt: clock.now.addingTimeInterval(hours * 3600), tableNumber: table, phone: phone, isWaitlist: waitlist)
    }
    private func window(_ clock: LocalTestClock) -> (Date, Date) { (clock.now.addingTimeInterval(-86_400 * 200), clock.now.addingTimeInterval(86_400 * 10)) }
    private func list(_ api: LocalPosAPI, _ clock: LocalTestClock) async throws -> [Reservation] {
        let (from, to) = window(clock)
        return try await api.reservations(from: from, to: to)
    }

    @Test(arguments: [("", 2, 90, nil as String?), ("Martin", 0, 90, nil), ("Martin", 31, 90, nil), ("Martin", 2, 10, nil), ("Martin", 2, 90, "abc")])
    func rejectsInvalidInput(name: String, covers: Int, duration: Int, phone: String?) async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        var d = draft(clock, phone: phone, name: name)
        d.covers = covers
        d.durationMinutes = duration
        await #expect(throws: APIError.self) { try await api.createReservation(d) }
    }

    @Test func rejectsUnknownAndTooSmallTables() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        await #expect(throws: ReservationError.unknownTable.apiError) { try await api.createReservation(draft(clock, table: "T99")) }
        await #expect(throws: ReservationError.tableCapacity.apiError) { try await api.createReservation(draft(clock, table: "T1", covers: 3)) }
    }

    @Test func overlapOnTheSameTableIsRefusedButBackToBackAndWaitlistAreFine() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        _ = try await api.createReservation(draft(clock, hours: 1))
        await #expect(throws: ReservationError.tableConflict.apiError) { try await api.createReservation(draft(clock, hours: 2)) }
        _ = try await api.createReservation(draft(clock, hours: 2.5))
        _ = try await api.createReservation(draft(clock, hours: 1, table: nil, waitlist: true))
        #expect(try await list(api, clock).count == 3)
    }

    @Test func phoneIsHiddenFromNonManagersAndTheirEditsKeepIt() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        let created = try await api.createReservation(draft(clock, phone: "+33 6 12 34 56 78"))
        #expect(created.phone == "+33 6 12 34 56 78")

        _ = try await api.login(pin: "2468")
        let asWaiter = try #require(try await list(api, clock).first)
        #expect(asWaiter.phone == nil && asWaiter.hasPhone)
        var edit = ReservationDraft(asWaiter)
        edit.covers = 3
        edit.tableNumber = "T2"
        _ = try await api.updateReservation(id: created.id, edit)

        _ = try await api.login(pin: "1234")
        let asManager = try #require(try await list(api, clock).first)
        #expect(asManager.phone == "+33 6 12 34 56 78" && asManager.covers == 3)

        var cleared = ReservationDraft(asManager)
        cleared.phone = nil
        _ = try await api.updateReservation(id: created.id, cleared)
        #expect(try await list(api, clock).first?.phone == nil)
    }

    @Test func lateFlagFollowsTheClock() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        _ = try await api.createReservation(draft(clock, hours: 0.25))
        #expect(try await list(api, clock).first?.isLate == false)
        clock.advance(30 * 60)
        #expect(try await list(api, clock).first?.isLate == false)
        clock.advance(10 * 60)
        #expect(try await list(api, clock).first?.isLate == true)
    }

    @Test func seatingOpensTheTableOnceAndRefusesAnOccupiedOne() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        let first = try await api.createReservation(draft(clock, hours: 1))
        let second = try await api.createReservation(draft(clock, hours: 5))

        let seated = try await api.seatReservation(id: first.id, tableNumber: nil, operatorId: nil, waiterName: "Sophie")
        #expect(seated.status == .seated)
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .occupied && table.coversCount == 2)

        await #expect(throws: ReservationError.invalidState.apiError) { try await api.seatReservation(id: first.id, tableNumber: nil, operatorId: nil, waiterName: nil) }
        await #expect(throws: ReservationError.tableOccupied.apiError) { try await api.seatReservation(id: second.id, tableNumber: "T1", operatorId: nil, waiterName: nil) }
    }

    @Test func waitlistEntryNeedsATableToBeSeated() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        let waiting = try await api.createReservation(draft(clock, hours: 0, table: nil, waitlist: true))
        await #expect(throws: ReservationError.tableRequired.apiError) { try await api.seatReservation(id: waiting.id, tableNumber: nil, operatorId: nil, waiterName: nil) }
        #expect(try await api.seatReservation(id: waiting.id, tableNumber: "T2", operatorId: nil, waiterName: nil).tableNumber == "T2")
    }

    @Test func statusTransitionsAreLimited() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        let r = try await api.createReservation(draft(clock))
        await #expect(throws: ReservationError.invalidState.apiError) { try await api.setReservationStatus(id: r.id, status: .done) }
        #expect(try await api.setReservationStatus(id: r.id, status: .noShow).status == .noShow)
        await #expect(throws: ReservationError.invalidState.apiError) { try await api.setReservationStatus(id: r.id, status: .cancelled) }
        await #expect(throws: ReservationError.invalidState.apiError) { try await api.updateReservation(id: r.id, draft(clock)) }
        await #expect(throws: ReservationError.notFound.apiError) { try await api.setReservationStatus(id: UUID(), status: .cancelled) }
    }

    @Test func purgeErasesOldPhonesThenOldRows() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        _ = try await api.createReservation(draft(clock, hours: 1, phone: "0612345678"))

        clock.advance(86_400)
        #expect(try await list(api, clock).first?.phone == "0612345678")
        clock.advance(86_400 * 2)
        #expect(try await list(api, clock).first?.phone == nil)
        clock.advance(86_400 * 92)
        #expect(try await list(api, clock).isEmpty)
    }

    @Test func requiresAnAuthenticatedOperator() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.reservations(from: Date(), to: Date()) }
    }
}
