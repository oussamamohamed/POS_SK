import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : imprimantes par transport")
struct LocalPrinterTransportAdminTests {
    private func api() async throws -> LocalPosAPI {
        try await makePrintAPI(clock: LocalTestClock(), transport: FakePrinterTransport())
    }

    private func invalid(_ id: String) -> APIError { .server(status: 400, message: "Identifiant d'imprimante invalide : \(id)") }

    @Test func starBluetoothWithoutIpIsSavedAndReadBack() async throws {
        let api = try await api()
        let p = Printer(name: "Star BT", ipAddress: "", transport: .star, deviceIdentifier: "bluetooth:00:11:62:AA:BB:CC")
        try await api.savePrinter(p, isNew: true)
        let back = try #require(try await api.printers().first { $0.id == p.id })
        #expect(back.transport == .star)
        #expect(back.deviceIdentifier == "bluetooth:00:11:62:AA:BB:CC")
        #expect(back.ipAddress == "")
    }

    @Test func epsonTcpIsSaved() async throws {
        let api = try await api()
        let p = Printer(name: "Epson", ipAddress: "", transport: .epson, deviceIdentifier: "TM_T88;TCP:192.168.1.60")
        try await api.savePrinter(p, isNew: true)
        #expect(try await api.printers().first { $0.id == p.id }?.deviceIdentifier == "TM_T88;TCP:192.168.1.60")
    }

    @Test func lanWithoutIpIsRejected() async throws {
        let api = try await api()
        await #expect(throws: APIError.server(status: 400, message: "Le nom et l'adresse IP de l'imprimante sont requis.")) {
            try await api.savePrinter(Printer(name: "LAN", ipAddress: ""), isNew: true)
        }
    }

    @Test func invalidIdentifiersAreRejected() async throws {
        let api = try await api()
        await #expect(throws: invalid("bluetooth:")) {
            try await api.savePrinter(Printer(name: "S", ipAddress: "", transport: .star, deviceIdentifier: "bluetooth:"), isNew: true)
        }
        await #expect(throws: invalid("XYZ:1")) {
            try await api.savePrinter(Printer(name: "E", ipAddress: "", transport: .epson, deviceIdentifier: "XYZ:1"), isNew: true)
        }
    }

    @Test func nonLanWithNeitherIdentifierNorIpIsRejected() async throws {
        let api = try await api()
        await #expect(throws: APIError.self) {
            try await api.savePrinter(Printer(name: "S", ipAddress: "", transport: .star), isNew: true)
        }
    }

    @Test func updatingLanToStarKeepsId() async throws {
        let api = try await api()
        var p = textPrinter("Caisse")
        try await api.savePrinter(p, isNew: true)
        p.transport = .star
        p.deviceIdentifier = "usb:SN42"
        try await api.savePrinter(p, isNew: false)
        let all = try await api.printers().filter { $0.id == p.id }
        #expect(all.count == 1)
        #expect(all.first?.transport == .star)
    }

    @Test func testPrinterMessageCitesDeviceForNonLan() async throws {
        let api = try await api()
        let p = Printer(name: "Star BT", ipAddress: "", textMode: true, transport: .star, deviceIdentifier: "bluetooth:AA")
        try await api.savePrinter(p, isNew: true)
        let result = try await api.testPrinter(id: p.id)
        #expect(result.message.contains("Star BT") && result.message.contains("bluetooth:AA"))
        #expect(!result.message.contains(":9100"))
    }
}
