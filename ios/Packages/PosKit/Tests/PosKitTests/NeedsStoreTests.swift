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

@MainActor
@Suite("Analyse du menu : tri, famille et plat choisi")
struct MenuAnalysisStoreTests {
    private func loadedStore(_ api: InMemoryPosAPI) async throws -> MenuAnalysisStore {
        _ = try await api.login(pin: "1234")
        let store = MenuAnalysisStore(api: api, notifier: Notifier())
        await store.load()
        return store
    }

    @Test func sortsByQuantityDescendingThenTogglesAndSortsByName() async throws {
        let store = try await loadedStore(InMemoryPosAPI())
        #expect(store.items.map(\.quantity) == store.items.map(\.quantity).sorted(by: >))
        store.sort(by: .quantity)
        #expect(store.items.map(\.quantity) == store.items.map(\.quantity).sorted())
        store.sort(by: .name)
        #expect(store.sortAscending)
        let names = store.items.map(\.name)
        #expect(names == names.sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending })
    }

    @Test func pickTogglesAndCategoryFilterNarrowsTheReport() async throws {
        let api = InMemoryPosAPI()
        let store = try await loadedStore(api)
        let first = try #require(store.items.first)
        store.togglePick(first.productId)
        #expect(store.pickedItem?.productId == first.productId)
        store.togglePick(first.productId)
        #expect(store.pickedItem == nil)

        let all = store.items.count
        let category = try #require(try await api.products().first { $0.id == first.productId }?.categoryId)
        store.categoryId = category
        await store.load()
        #expect(store.items.count < all)
        #expect(store.items.contains { $0.productId == first.productId })
    }
}
