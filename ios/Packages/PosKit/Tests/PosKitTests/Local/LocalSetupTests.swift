import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : base vierge, première configuration, réseau")
struct LocalSetupTests {
    private static let setup = LocalSetup(
        managerName: "Marie Curie", managerPin: "4321", companyName: "Chez Marie", address: "1 rue de la Paix\n75002 Paris",
        siret: "12345678901234", vatNumber: "FR00123456789"
    )

    @Test func blankDatabaseHoldsNoDemoData() async throws {
        let api = try LocalPosAPI(path: ":memory:", seed: .blank)
        #expect(try await api.needsSetup())
        for pin in ["1234", "2468", "5678", "9999"] {
            #expect(try await api.login(pin: pin).success == false)
        }
        #expect(try await api.staff().isEmpty)
        #expect(try await api.categories().isEmpty)
        #expect(try await api.tables().isEmpty)
        #expect(try await api.printers().isEmpty)
        #expect(try await api.happyHourSchedules().isEmpty)
    }

    @Test func completeFirstRunCreatesTheManagerAndTheSettings() async throws {
        let api = try LocalPosAPI(path: ":memory:", seed: .blank)
        try await api.completeFirstRun(Self.setup)
        #expect(try await api.needsSetup() == false)

        let login = try await api.login(pin: "4321")
        #expect(login.success && login.role == .admin && login.operatorName == "Marie Curie")
        let settings = try await api.settings()
        #expect(settings.companyName == "Chez Marie" && settings.siret == "12345678901234" && settings.vatNumber == "FR00123456789")
        #expect(settings.addressLines == "1 rue de la Paix\n75002 Paris" && settings.receiptLanguage == "fr")
        #expect(try await api.staff().count == 1)
    }

    @Test func completeFirstRunRefusesAnAlreadyConfiguredDatabase() async throws {
        let configured = try LocalPosAPI(path: ":memory:", seed: .blank)
        try await configured.completeFirstRun(Self.setup)
        let refused = APIError.server(status: 409, message: "L'installation est déjà configurée.")
        await #expect(throws: refused) { try await configured.completeFirstRun(Self.setup) }
        #expect(try await configured.staff().count == 1)

        let demo = try LocalPosAPI(path: ":memory:")
        #expect(try await demo.needsSetup() == false)
        await #expect(throws: refused) { try await demo.completeFirstRun(Self.setup) }
        #expect(try await demo.staff().count == 4)
    }

    @Test func completeFirstRunValidatesBeforeWriting() async throws {
        let api = try LocalPosAPI(path: ":memory:", seed: .blank)
        var noName = Self.setup; noName.managerName = "  "
        var shortPin = Self.setup; shortPin.managerPin = "12"
        var letterPin = Self.setup; letterPin.managerPin = "12ab"
        var noCompany = Self.setup; noCompany.companyName = ""
        var shortSiret = Self.setup; shortSiret.siret = "123"
        var letterSiret = Self.setup; letterSiret.siret = "1234567890123A"
        let pinMessage = "Le code PIN doit comporter 4 chiffres."
        let siretMessage = "Le SIRET doit comporter 14 chiffres."
        let cases: [(LocalSetup, String)] = [
            (noName, "Le nom du responsable est requis."), (shortPin, pinMessage), (letterPin, pinMessage),
            (noCompany, "Le nom de l'établissement est requis."), (shortSiret, siretMessage), (letterSiret, siretMessage),
        ]
        for (setup, message) in cases {
            await #expect(throws: APIError.server(status: 400, message: message)) { try await api.completeFirstRun(setup) }
        }
        #expect(try await api.needsSetup())
        #expect(try await api.staff().isEmpty)
    }

    @Test func firstRunSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        do {
            let first = try LocalPosAPI(path: path, seed: .blank)
            try await first.completeFirstRun(Self.setup)
        }
        let reopened = try LocalPosAPI(path: path, seed: .blank)
        #expect(try await reopened.needsSetup() == false)
        #expect(try await reopened.login(pin: "4321").success)
        #expect(try await reopened.staff().count == 1)
        #expect(try await reopened.login(pin: "1234").success == false)
    }

    @Test func demoSeedIncludesPrintersAndTheAfterworkSchedule() async throws {
        let api = try await makeLocalAPI()
        let printers = try await api.printers()
        #expect(printers.map(\.name).sorted() == ["Imprimante Bar & Boissons", "Imprimante Caisse Comptoir", "Imprimante Cuisine Chaude"])
        let cash = try #require(printers.first { $0.name == "Imprimante Caisse Comptoir" })
        #expect(cash.openCashDrawerOnReceipt && cash.assignedStationIds == ["RECEIPT"] && cash.ipAddress == "192.168.1.200")

        let schedules = try await api.happyHourSchedules()
        #expect(schedules.count == 1)
        let afterwork = try #require(schedules.first)
        #expect(afterwork.name == "Afterwork Standard" && afterwork.daysOfWeek.sorted() == [1, 2, 3, 4, 5] && afterwork.priceRules.count == 2)
    }

    @Test func networkAndSyncReportStandalone() async throws {
        let api = try await makeLocalAPI()
        let info = try await api.networkInfo()
        #expect(info.serverName == "Mode autonome (cet iPad)" && info.status == "Standalone" && info.primaryIp == nil && info.hostName == nil)
        let sync = try await api.syncStatus()
        #expect(sync.totalMessages == 0 && sync.completedMessages == 0 && sync.pendingMessages == 0 && sync.status == "Standalone" && sync.lastSyncUtc == nil)
        try await api.forceSync()
        #expect(try await api.health() == 0)
    }
}
