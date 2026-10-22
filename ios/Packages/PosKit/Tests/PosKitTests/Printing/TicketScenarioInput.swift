import Foundation
@testable import PosKit

/// `input` d'un scénario de `ticket_document_reference.json` : tous les membres sont optionnels,
/// chaque `kind` n'utilise que les siens (voir le rapport de la tâche 1).
struct TicketScenarioInput: Decodable {
    struct FixturePrinter: Decodable {
        var name: String
        var ipAddress: String
        var port: Int
        var paperWidthMm: Int
    }

    struct MissingMember: Error, CustomStringConvertible {
        let kind: String
        let member: String
        var description: String { "scénario \(kind) : membre `\(member)` absent" }
    }

    struct UnknownKind: Error, CustomStringConvertible {
        let kind: String
        var description: String { "kind inconnu : \(kind)" }
    }

    var receipt: TicketReceipt?
    var order: TicketOrder?
    var settings: TicketSettings?
    var duplicateNumber: Int?
    var pickupNumber: String?
    var buzzer: String?
    var nowUtc: Date?
    var kitchen: TicketKitchen?
    var summary: TicketReportSummary?
    var closure: TicketDailyClosure?
    var periodClosure: TicketPeriodClosure?
    var reportData: ReportPrintData?
    var printer: FixturePrinter?
    var document: TicketDocument?

    static func decode(_ data: Data) throws -> TicketScenarioInput {
        try TicketJSON.decoder().decode(TicketScenarioInput.self, from: data)
    }

    static func build(_ kind: String, _ language: String, _ input: TicketScenarioInput, _ context: TicketContext) throws -> TicketDocument {
        func need<T>(_ value: T?, _ member: String) throws -> T {
            guard let value else { throw MissingMember(kind: kind, member: member) }
            return value
        }
        let i = input
        switch kind {
        case "receipt":
            return TicketBuilder.receipt(try need(i.receipt, "receipt"), order: try need(i.order, "order"), language: language,
                                         settings: i.settings, duplicateNumber: i.duplicateNumber, context: context)
        case "receiptFallback":
            return TicketBuilder.receiptFallback(try need(i.receipt, "receipt"), language: language, settings: i.settings,
                                                 duplicateNumber: i.duplicateNumber, context: context)
        case "fiscalReceipt":
            return TicketBuilder.fiscalReceipt(try need(i.receipt, "receipt"), order: try need(i.order, "order"),
                                               pickupNumber: try need(i.pickupNumber, "pickupNumber"), buzzer: i.buzzer, language: language,
                                               settings: i.settings, duplicateNumber: i.duplicateNumber, context: context)
        case "pickupCoupon":
            return TicketBuilder.pickupCoupon(order: try need(i.order, "order"), pickupNumber: try need(i.pickupNumber, "pickupNumber"),
                                              buzzer: i.buzzer, language: language, now: try need(i.nowUtc, "nowUtc"))
        case "kitchenTicket":
            return TicketBuilder.kitchenTicket(try need(i.kitchen, "kitchen"), order: try need(i.order, "order"), language: language, context: context)
        case "xReport":
            return TicketBuilder.xReport(try need(i.summary, "summary"), data: try need(i.reportData, "reportData"), language: language,
                                         now: try need(i.nowUtc, "nowUtc"), settings: i.settings, context: context)
        case "zClosure":
            return TicketBuilder.zClosure(try need(i.closure, "closure"), data: try need(i.reportData, "reportData"), language: language,
                                          settings: i.settings, duplicateNumber: i.duplicateNumber, context: context)
        case "periodClosure":
            return TicketBuilder.periodClosure(try need(i.periodClosure, "periodClosure"), data: try need(i.reportData, "reportData"),
                                               language: language, settings: i.settings, context: context)
        case "testPage":
            let p = try need(i.printer, "printer")
            return TicketBuilder.testPage(printer: Printer(name: p.name, ipAddress: p.ipAddress, port: p.port, paperWidthMm: p.paperWidthMm),
                                          language: language, now: try need(i.nowUtc, "nowUtc"))
        case "withDuplicateNotice":
            return TicketBuilder.withDuplicateNotice(try need(i.document, "document"), duplicateNumber: try need(i.duplicateNumber, "duplicateNumber"))
        default:
            throw UnknownKind(kind: kind)
        }
    }
}
