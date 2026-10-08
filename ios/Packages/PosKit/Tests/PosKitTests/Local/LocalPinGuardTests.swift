import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : verrouillage des PIN persistant")
struct LocalPinGuardTests {
    private func failLogin(_ api: LocalPosAPI, times: Int) async throws {
        for _ in 0..<times { _ = try await api.login(pin: "0000") }
    }

    private func cleanup(_ path: String) {
        for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) }
    }

    @Test func schemaV4CreatesTheNewTables() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 4)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["PrinterConfigurations", "HappyHourSchedules", "HappyHourPriceRules", "HappyHourOverrideSessions", "LocalPinFailures", "LocalPinLockout"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func fiveFailuresLockUntilThirtySecondsHavePassed() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await failLogin(api, times: 5)
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        clock.advance(29)
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        clock.advance(2)
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func failuresOlderThanAMinuteDoNotAccumulate() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await failLogin(api, times: 4)
        clock.advance(61)
        try await failLogin(api, times: 4)
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func aSuccessfulLoginClearsTheFailures() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await failLogin(api, times: 4)
        #expect(try await api.login(pin: "1234").success)
        try await failLogin(api, times: 4)
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func lockoutSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { cleanup(path) }
        let clock = LocalTestClock()
        do {
            let first = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
            try await failLogin(first, times: 5)
        }
        let reopened = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        await #expect(throws: APIError.rateLimited(nil)) { try await reopened.login(pin: "1234") }
        clock.advance(31)
        #expect(try await reopened.login(pin: "1234").success)
    }

    @Test func failureCountSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { cleanup(path) }
        let clock = LocalTestClock()
        do {
            let first = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
            try await failLogin(first, times: 3)
        }
        let reopened = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        try await failLogin(reopened, times: 2)
        await #expect(throws: APIError.rateLimited(nil)) { try await reopened.login(pin: "1234") }
    }
}
