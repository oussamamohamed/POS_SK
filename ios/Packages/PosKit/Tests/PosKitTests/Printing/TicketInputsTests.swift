import Foundation
import Testing
@testable import PosKit

/// Sonde de décodage des `input` de la fixture (ce n'est pas `TicketScenarioInput`, de la tâche 5).
private struct FixtureInputProbe: Decodable {
    var receipt: TicketReceipt?
    var order: TicketOrder?
    var settings: TicketSettings?
    var kitchen: TicketKitchen?
    var summary: TicketReportSummary?
    var closure: TicketDailyClosure?
    var periodClosure: TicketPeriodClosure?
    var reportData: ReportPrintData?

    var decodedMembers: Int {
        [receipt != nil, order != nil, settings != nil, kitchen != nil,
         summary != nil, closure != nil, periodClosure != nil, reportData != nil].filter { $0 }.count
    }
}

@Suite struct TicketInputsTests {
    @Test func everyFixtureInputDecodesIntoTheTicketStructs() throws {
        let f = try TicketReferenceFixture.load()
        #expect(!f.documents.isEmpty)
        var perKind: [String: Int] = [:]
        var probes: [String: FixtureInputProbe] = [:]
        for d in f.documents {
            let probe = try TicketJSON.decoder().decode(FixtureInputProbe.self, from: d.inputData)
            perKind[d.kind, default: 0] += probe.decodedMembers
            probes[d.name] = probe
        }
        // testPage (printer, nowUtc) et withDuplicateNotice (document) n'ont aucun de ces membres.
        for kind in Set(f.documents.map(\.kind)).subtracting(["testPage", "withDuplicateNotice"]) {
            #expect((perKind[kind] ?? 0) > 0, "aucun membre décodé pour \(kind)")
        }

        #expect(probes["period_monthly"]?.periodClosure?.isMonthly == true)
        #expect(probes["x_first_report"]?.summary?.periodStartUtc == nil)
        #expect(probes["x_first_report"]?.summary?.periodEndUtc != nil)
        let rates = try #require(probes["z_rates"]?.closure?.vatBreakdownCents.map(\.rate))
        #expect(rates.contains(5.5)); #expect(rates.contains(0.125))
        #expect(probes["receipt_fr_full"]?.order?.items.isEmpty == false)
    }
}
