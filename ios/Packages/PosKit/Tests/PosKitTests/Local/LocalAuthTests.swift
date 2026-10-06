import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : authentification")
struct LocalAuthTests {
    @Test func conformsToPosAPI() throws {
        let api: PosAPI = try LocalPosAPI(path: ":memory:")
        _ = api
    }

    @Test func validPinReturnsOperatorAndToken() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        let result = try await api.login(pin: "1234")
        #expect(result.success)
        #expect(result.role == .floorManager)
        #expect(result.token != nil)
    }

    @Test func wrongPinFailsWithServerMessage() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        let result = try await api.login(pin: "0000")
        #expect(!result.success)
        #expect(result.errorMessage == "Code PIN ou identifiants incorrects")
    }

    @Test func fiveFailuresLockLoginEvenWithTheRightPin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        for _ in 0..<5 { _ = try await api.login(pin: "0000") }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
    }

    @Test func successResetsTheFailureCounter() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        for _ in 0..<4 { _ = try await api.login(pin: "0000") }
        #expect(try await api.login(pin: "1234").success)
        for _ in 0..<4 { _ = try await api.login(pin: "0000") }
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func protectedCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.createStaff(name: "X", role: .waiter, pin: "1357") }
    }

    @Test func waiterCannotUseManagerActions() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            try await api.createStaff(name: "X", role: .waiter, pin: "1357")
        }
    }

    @Test func clearingTheTokenLocksTheSession() async throws {
        let api = try await makeLocalAPI()
        await api.setToken(nil)
        await #expect(throws: APIError.unauthorized) { try await api.createStaff(name: "X", role: .waiter, pin: "1357") }
    }

    @Test func unsupportedMethodsAnswer501() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: APIError.server(status: 501, message: "Non disponible en mode autonome : kitchenTickets()")) {
            try await api.kitchenTickets()
        }
    }
}
