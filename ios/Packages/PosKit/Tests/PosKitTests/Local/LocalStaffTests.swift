import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : personnel")
struct LocalStaffTests {
    @Test func seedsFourAccounts() async throws {
        let api = try await makeLocalAPI()
        let staff = try await api.staff()
        #expect(staff.map(\.role) == [.floorManager, .waiter, .kitchenStaff, .admin])
    }

    @Test func createdMemberCanLogIn() async throws {
        let api = try await makeLocalAPI()
        try await api.createStaff(name: "Léa", role: .cashier, pin: "4321")
        let result = try await api.login(pin: "4321")
        #expect(result.success && result.operatorName == "Léa" && result.role == .cashier)
    }

    @Test func rejectsDuplicateAndMalformedPins() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: APIError.server(status: 400, message: "Ce code PIN est déjà attribué.")) {
            try await api.createStaff(name: "Copie", role: .waiter, pin: "2468")
        }
        for bad in ["123", "12345", "12a4", "١٢٣٤"] {
            await #expect(throws: APIError.server(status: 400, message: "Le code PIN doit comporter 4 chiffres.")) {
                try await api.createStaff(name: "Mauvais", role: .waiter, pin: bad)
            }
        }
    }

    @Test func pinsKeepLeadingZeros() async throws {
        let api = try await makeLocalAPI()
        try await api.createStaff(name: "Zéro", role: .waiter, pin: "0042")
        #expect(try await api.login(pin: "0042").success)
        #expect(try await api.login(pin: "42").success == false)
    }

    @Test func nonLatinNamesRoundTrip() async throws {
        let api = try await makeLocalAPI()
        try await api.createStaff(name: "ليلى 🍓", role: .waiter, pin: "3141")
        #expect(try await api.staff().last?.name == "ليلى 🍓")
        #expect(try await api.login(pin: "3141").operatorName == "ليلى 🍓")
    }

    @Test func updateChangesRoleAndPin() async throws {
        let api = try await makeLocalAPI()
        let waiter = try #require(try await api.staff().first { $0.role == .waiter })
        try await api.updateStaff(id: waiter.id, name: "Sophie", role: .cashier, pin: "1111", isActive: true)
        #expect(try await api.login(pin: "2468").success == false)
        let result = try await api.login(pin: "1111")
        #expect(result.success && result.role == .cashier)
    }

    @Test func deactivatedMemberCannotLogIn() async throws {
        let api = try await makeLocalAPI()
        let waiter = try #require(try await api.staff().first { $0.role == .waiter })
        try await api.deactivateStaff(id: waiter.id)
        #expect(try await api.login(pin: "2468").success == false)
    }

    @Test func lastActiveManagerCannotBeRemoved() async throws {
        let api = try await makeLocalAPI()
        let staff = try await api.staff()
        let floorManager = try #require(staff.first { $0.role == .floorManager })
        let admin = try #require(staff.first { $0.role == .admin })
        // Connecté en responsable (1234) : retirer l'admin est permis tant qu'un autre responsable reste actif…
        try await api.deactivateStaff(id: admin.id)
        // …mais pas se retirer soi-même (désactiver ou rétrograder) : ce serait le dernier.
        let blocked = APIError.server(status: 400, message: "Au moins un responsable actif est requis.")
        await #expect(throws: blocked) { try await api.deactivateStaff(id: floorManager.id) }
        await #expect(throws: blocked) { try await api.updateStaff(id: floorManager.id, name: floorManager.name, role: .waiter, pin: nil, isActive: true) }
        #expect(try await api.login(pin: "1234").success)
        #expect(try await api.login(pin: "9999").success == false)
    }
}
