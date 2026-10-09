import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : installation autonome")
struct LocalStandaloneModeTests {
    private static let setup = LocalSetup(
        managerName: "Marie Curie", managerPin: "4321", companyName: "Chez Marie", address: "1 rue de la Paix",
        siret: "12345678901234", vatNumber: ""
    )

    @Test func standaloneCredentialsIdentifyTheFixedTerminal() {
        let credentials = DeviceCredentials.standalone
        #expect(credentials.terminalId == LocalPosAPI.standaloneTerminalId && credentials.terminalId == "T01")
        #expect(!credentials.token.isEmpty && !credentials.name.isEmpty)
    }

    @Test func openStandaloneNeverSeedsDemoAccounts() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        do {
            let first = try LocalPosAPI.openStandalone(path: path)
            #expect(try await first.needsSetup())
            for pin in ["1234", "2468", "5678", "9999"] { #expect(try await first.login(pin: pin).success == false) }
            #expect(try await first.staff().isEmpty)
            #expect(try await first.printers().isEmpty)
        }
        // Une réouverture n'amorce toujours rien.
        let reopened = try LocalPosAPI.openStandalone(path: path)
        #expect(try await reopened.needsSetup())
        #expect(try await reopened.staff().isEmpty)
    }

    @Test func needsSetupFollowsTheFirstRunGuard() async throws {
        let blank = try LocalPosAPI.openStandalone(path: ":memory:")
        #expect(try await blank.needsSetup())
        try await blank.completeFirstRun(Self.setup)
        #expect(try await blank.needsSetup() == false)
        await #expect(throws: APIError.server(status: 409, message: "L'installation est déjà configurée.")) { try await blank.completeFirstRun(Self.setup) }

        let demo = try LocalPosAPI(path: ":memory:")
        #expect(try await demo.needsSetup() == false)
    }
}
