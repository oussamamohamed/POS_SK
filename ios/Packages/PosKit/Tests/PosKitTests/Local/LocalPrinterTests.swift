import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : configuration des imprimantes")
struct LocalPrinterTests {
    private static let notFound = APIError.notFound("Imprimante introuvable.")

    @Test func createUpdateAndListPrinters() async throws {
        let api = try await makeLocalAPI()
        let baseline = try await api.printers().count
        let bar = Printer(name: "Bar", ipAddress: "192.168.1.50", assignedStationIds: ["BAR"])
        var kitchen = Printer(name: "Cuisine", ipAddress: "192.168.1.51", port: 9101, paperWidthMm: 58, assignedStationIds: ["HOT_KITCHEN", "GRILL"], textMode: true)
        try await api.savePrinter(bar, isNew: true)
        try await api.savePrinter(kitchen, isNew: true)

        let listed = try await api.printers()
        #expect(listed.count == baseline + 2)
        #expect(listed.first { $0.id == kitchen.id } == kitchen)

        kitchen.name = "Cuisine chaude"
        kitchen.isActive = false
        kitchen.openCashDrawerOnReceipt = true
        kitchen.assignedStationIds = ["HOT_KITCHEN"]
        try await api.savePrinter(kitchen, isNew: false)
        #expect(try await api.printers().first { $0.id == kitchen.id } == kitchen)
        let names = try await api.printers().map(\.name)
        #expect(names == names.sorted())
    }

    @Test func savePrinterNormalizesLikeTheServer() async throws {
        let api = try await makeLocalAPI()
        try await api.savePrinter(Printer(name: "  Caisse  ", ipAddress: " 10.0.0.9 ", port: 0, paperWidthMm: 72), isNew: true)
        let stored = try #require(try await api.printers().first { $0.name == "Caisse" })
        #expect(stored.ipAddress == "10.0.0.9" && stored.port == 9100 && stored.paperWidthMm == 80 && stored.isActive)

        let inactive = Printer(name: "Inactive", ipAddress: "10.0.0.10", isActive: false)
        try await api.savePrinter(inactive, isNew: true)
        #expect(try await api.printers().first { $0.id == inactive.id }?.isActive == true)
    }

    @Test func savePrinterRefusals() async throws {
        let api = try await makeLocalAPI()
        let required = APIError.server(status: 400, message: "Le nom et l'adresse IP de l'imprimante sont requis.")
        await #expect(throws: required) { try await api.savePrinter(Printer(name: "  ", ipAddress: "10.0.0.1"), isNew: true) }
        await #expect(throws: required) { try await api.savePrinter(Printer(name: "X", ipAddress: ""), isNew: true) }
        await #expect(throws: Self.notFound) { try await api.savePrinter(Printer(name: "X", ipAddress: "10.0.0.1"), isNew: false) }
        let known = Printer(name: "Y", ipAddress: "10.0.0.2")
        try await api.savePrinter(known, isNew: true)
        await #expect(throws: APIError.server(status: 409, message: "Imprimante déjà enregistrée.")) { try await api.savePrinter(known, isNew: true) }
    }

    @Test func writingNeedsLoginButListingDoesNot() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        _ = try await api.printers()
        await #expect(throws: APIError.unauthorized) { try await api.savePrinter(Printer(name: "X", ipAddress: "10.0.0.1"), isNew: true) }
        await #expect(throws: APIError.unauthorized) { try await api.printerStatuses() }
        await #expect(throws: APIError.unauthorized) { try await api.testPrinter(id: UUID()) }
    }

    @Test func statusesJobsAndTestPrintAreInertUntilDirectPrinting() async throws {
        let api = try await makeLocalAPI()
        let printer = Printer(name: "Z", ipAddress: "10.0.0.3")
        try await api.savePrinter(printer, isNew: true)

        let status = try #require(try await api.printerStatuses().first { $0.printerId == printer.id })
        #expect(status.name == "Z" && status.isActive && status.isOnline == nil && status.pendingCount == 0 && status.failedCount == 0)
        #expect(try await api.printJobs(printerId: printer.id).isEmpty)
        await #expect(throws: Self.notFound) { try await api.printJobs(printerId: UUID()) }

        let test = try await api.testPrinter(id: printer.id)
        #expect(test.success == false && test.message == "L'impression directe n'est pas encore disponible en mode autonome.")
        await #expect(throws: Self.notFound) { try await api.testPrinter(id: UUID()) }
        await #expect(throws: APIError.notFound("Impression introuvable.")) { try await api.retryPrintJob(id: UUID()) }
        await #expect(throws: APIError.notFound("Impression introuvable.")) { try await api.cancelPrintJob(id: UUID()) }

        let waiter = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await waiter.printJobs(printerId: printer.id) }
    }
}
