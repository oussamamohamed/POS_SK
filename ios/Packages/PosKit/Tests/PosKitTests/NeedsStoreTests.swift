import Foundation
import Testing
@testable import PosKit

@MainActor
@Suite("À traiter : cloche et pastilles")
struct NeedsStoreTests {
    @Test func collectsLateBookingsAndOverdueWaitlistFromTheDemoBook() async throws {
        let api = InMemoryPosAPI()
        _ = try await api.login(pin: "1234")
        let store = NeedsStore(api: api)
        await store.refresh()
        #expect(store.count(.lateBooking) == 1)
        #expect(store.count(.waitlist) == 1)
        #expect(store.bookingCount == 2)
        #expect(store.billCount == 0)
        // Du plus ancien au plus récent.
        #expect(store.items.map(\.since) == store.items.map(\.since).sorted())
    }

    @Test func aWaitlistEntryWithinItsQuoteIsNotUrgent() async throws {
        let api = InMemoryPosAPI(seed: false)
        _ = try await api.login(pin: "1234")
        _ = try await api.createReservation(ReservationDraft(guestName: "Okafor", startsAt: Date().addingTimeInterval(-5 * 60), isWaitlist: true, quoteMinutes: 20))
        let store = NeedsStore(api: api)
        await store.refresh()
        #expect(store.items.isEmpty)
    }

    @Test func rejectsAnInvalidQuote() async throws {
        let api = InMemoryPosAPI()
        _ = try await api.login(pin: "1234")
        await #expect(throws: ReservationError.invalidQuote.apiError) {
            try await api.createReservation(ReservationDraft(guestName: "X", isWaitlist: true, quoteMinutes: 0))
        }
    }
}
