import Foundation
import Testing
@testable import PosKit

@Suite("Imprimantes : transport et identifiant d'appareil")
struct LocalPrinterTransportTests {
    private func makeRepo() throws -> LocalPrinterRepository {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return LocalPrinterRepository(db: db)
    }

    @Test func repositoryRoundTripsTransportAndDeviceIdentifier() throws {
        let repo = try makeRepo()
        var p = Printer(name: "Bar", ipAddress: "10.0.0.9")
        p.transport = .star
        p.deviceIdentifier = "BT:00:11"
        try repo.insert(p)
        #expect(try repo.printer(id: p.id) == p)
        p.transport = .epson
        p.deviceIdentifier = nil
        #expect(try repo.update(p))
        #expect(try repo.printer(id: p.id) == p)
        let plain = Printer(name: "Caisse", ipAddress: "10.0.0.8")
        try repo.insert(plain)
        #expect(try repo.printer(id: plain.id)?.transport == .lan)
    }

    @Test func codableDecodesOldPayloadsAndOmitsDefaults() throws {
        let old = #"{"id":"6F9619FF-8B86-D011-B42D-00C04FC964FF","name":"A","ipAddress":"1.2.3.4","port":9100,"paperWidthMm":80,"openCashDrawerOnReceipt":false,"assignedStationIds":[],"isActive":true}"#
        let p = try JSONDecoder().decode(Printer.self, from: Data(old.utf8))
        #expect(p.transport == .lan && p.deviceIdentifier == nil)
        let plainJSON = String(decoding: try JSONEncoder().encode(p), as: UTF8.self)
        #expect(!plainJSON.contains("transport") && !plainJSON.contains("deviceIdentifier"))
        var star = p
        star.transport = .star
        star.deviceIdentifier = "X"
        let back = try JSONDecoder().decode(Printer.self, from: try JSONEncoder().encode(star))
        #expect(back == star)
    }

    @Test func savePrinterKeepsTransportAndDeviceIdentifier() async throws {
        let api = try await makeLocalAPI()
        var p = Printer(name: "Bar", ipAddress: "10.0.0.9")
        p.transport = .epson
        p.deviceIdentifier = "TCP:10.0.0.9"
        try await api.savePrinter(p, isNew: true)
        #expect(try await api.printers().first { $0.id == p.id } == p)
    }
}
