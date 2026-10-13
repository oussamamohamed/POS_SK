import Foundation
import Testing
@testable import PosKit

/// Rejoue la « journée simulée » du .NET (`Fixtures/day_scenario.json`, générée par `DaySimulationTests`) sur le mode autonome.
/// Le poste autonome est toujours `T01` : chaque poste du scénario (A, B, C) est donc rejoué sur sa propre base, et ses X et Z
/// doivent égaler le grand livre indépendant du .NET (`expected`).
@Suite("LocalPosAPI : journée simulée (parité .NET)")
struct LocalDaySimulationTests {
    // MARK: - Scénario

    private struct Scenario: Decodable {
        var seed: Int
        var terminals: [Terminal]
        var operations: [Operation]
        var expected: [String: Expected]
        var total: Expected
    }

    private struct Terminal: Decodable { var key: String }

    private struct Line: Decodable {
        var name: String
        var quantity: Int
        var unitPriceCents: Int
        var taxRatePercent: String
        var taxRateTakeawayPercent: String?
        var modifiersExtraCents: Int
        var comp: Bool
    }

    private struct Tender: Decodable {
        var method: String
        var amountCents: Int
        var extraTenderedCents: Int?
    }

    private struct Discount: Decodable {
        var type: String
        var value: String
    }

    private struct Operation: Decodable {
        var type: String
        var terminal: String
        var waiter: String?
        var table: String?
        var covers: Int?
        var destination: String?
        var lines: [Line]?
        var discount: Discount?
        var tipCents: Int?
        var payments: [[Tender]]?
        var voidPaymentIndex: Int?
        var reprint: Bool?
        var holdAndRecall: Bool?
        var ref: String?
    }

    private struct Expected: Decodable {
        var ttcCents: Int
        var htCents: Int
        var receiptCount: Int
        var vatByRate: [String: Int]
        var tenderByMethod: [String: Int]
    }

    private static func scenario() throws -> Scenario {
        try JSONDecoder().decode(Scenario.self, from: ContractDecodingTests.fixture("day_scenario"))
    }

    // MARK: - Rejeu d'un poste

    private struct Replay {
        var xReports: [(op: Int, report: FiscalReport)] = []
        var zClosures: [(op: Int, report: FiscalReport)] = []
        var voidRefusals: [(op: Int, status: Int?)] = []
    }

    private static let managerPin = "1234"

    private static func payment(_ method: String) -> PaymentMethod {
        switch method {
        case "CreditCard": .creditCard
        case "MealVoucher": .mealVoucher
        case "GiftCard": .giftCard
        default: .cash
        }
    }

    private static func replay(_ scenario: Scenario, terminal key: String) async throws -> Replay {
        let clock = LocalTestClock()
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        let db = try SQLiteDatabase(path: path)
        let manager = try #require(try await api.login(pin: managerPin).operatorId)

        var pins: [String: String] = [:]
        var receiptsByRef: [String: [UUID]] = [:]
        var replay = Replay()

        func login(waiter: String) async throws -> UUID {
            if pins[waiter] == nil {
                let pin = String(7001 + pins.count)
                _ = try await api.login(pin: managerPin)
                try await api.createStaff(name: waiter, role: .waiter, pin: pin)
                pins[waiter] = pin
            }
            return try #require(try await api.login(pin: pins[waiter]!).operatorId)
        }

        func receiptId(_ number: String) throws -> UUID {
            let rows = try db.query("SELECT Id FROM FiscalReceipts WHERE ReceiptNumber = ?", [.text(number)])
            return try #require(rows.first?.uuid("Id"))
        }

        func input(_ line: Line) -> OrderItemInput {
            OrderItemInput(
                productId: UUID(), productName: line.name, quantity: line.quantity, unitPrice: Money(cents: line.unitPriceCents),
                taxRatePercent: Decimal(string: line.taxRatePercent)!, preparationStationId: nil, modifiers: [], course: .direct,
                modifiersPriceExtra: Money(cents: line.modifiersExtraCents),
                taxRateTakeawayPercent: line.taxRateTakeawayPercent.flatMap { Decimal(string: $0) },
                isHappyHourApplied: false, originalUnitPrice: nil, appliedHappyHourScheduleId: nil
            )
        }

        /// Remise et gratuités : réservées au responsable ; le serveur se reconnecte ensuite.
        func discountAndComps(_ op: Operation, table: String, order: ActiveOrder, waiterName: String) async throws {
            _ = try await api.login(pin: managerPin)
            if let discount = op.discount {
                try await api.applyDiscount(
                    orderId: order.orderId, type: discount.type == "Percentage" ? .percentage : .fixedAmount,
                    value: Decimal(string: discount.value)!, reason: "Geste commercial", operatorId: manager
                )
            }
            let comps = (op.lines ?? []).enumerated().filter { $0.element.comp }.map(\.offset)
            if !comps.isEmpty {
                let current = try #require(try await api.activeOrder(table: table))
                for index in comps {
                    try await api.compItem(orderId: order.orderId, lineId: current.lines[index].lineId, reason: "Offert", operatorId: manager)
                }
            }
            _ = try await login(waiter: waiterName)
        }

        func afterPayments(_ op: Operation, numbers: [String]) async throws {
            let ids = try numbers.map(receiptId)
            if let ref = op.ref { receiptsByRef[ref] = ids }
            _ = try await api.login(pin: managerPin)
            if op.reprint == true {
                for number in numbers { _ = try await api.reprintReceipt(receiptIdentifier: number) }
            }
            if let index = op.voidPaymentIndex { _ = try await api.voidReceipt(receiptId: ids[index]) }
        }

        for (index, op) in scenario.operations.enumerated() where op.terminal == key {
            clock.advance(5)
            switch op.type {
            case "table_order":
                let waiterName = try #require(op.waiter)
                let table = try #require(op.table)
                let waiterId = try await login(waiter: waiterName)
                try await api.openTable(number: table, covers: op.covers ?? 0, operatorId: waiterId, waiterName: waiterName)
                var order = try #require(try await api.activeOrder(table: table))
                if let destination = op.destination {
                    try await api.setDestination(orderId: order.orderId, destination: destination == "EatIn" ? .eatIn : .takeaway)
                }
                order = try await api.addItems(table: table, items: (op.lines ?? []).map(input))
                try await discountAndComps(op, table: table, order: order, waiterName: waiterName)
                var numbers: [String] = []
                let payments = op.payments ?? []
                for (p, list) in payments.enumerated() {
                    let tenders = list.map {
                        let extra = $0.extraTenderedCents ?? 0
                        return TenderInput(method: payment($0.method), amount: Money(cents: $0.amountCents), tendered: Money(cents: $0.amountCents + extra), changeGiven: Money(cents: extra))
                    }
                    let request = PaymentRequest(
                        orderId: order.orderId, tableNumber: table, operatorId: waiterId, terminalId: "T01", tenders: tenders,
                        requestReceiptPrint: false, tipAmount: Money(cents: p == payments.count - 1 ? (op.tipCents ?? 0) : 0)
                    )
                    numbers.append(try #require(try await api.pay(request).receiptNumber))
                }
                try await afterPayments(op, numbers: numbers)

            case "counter_order", "hold_then_void":
                let waiterName = try #require(op.waiter)
                _ = try await login(waiter: waiterName)
                let destination: OrderDestination = op.destination == "EatIn" ? .eatIn : .takeaway
                _ = try await api.openCounterOrder(terminalId: "T01", destination: destination)
                let order = try await api.addItems(table: DiningTable.counterNumber, items: (op.lines ?? []).map(input))
                if op.type == "hold_then_void" {
                    try await api.holdOrder(orderId: order.orderId, terminalId: "T01", label: "Client")
                    let held = try #require(try await api.heldOrders(terminalId: "T01").first { $0.orderId == order.orderId })
                    try await api.voidHeldOrder(holdId: held.holdId, supervisorPin: managerPin, reason: "Client parti", terminalId: "T01")
                    continue
                }
                try await discountAndComps(op, table: DiningTable.counterNumber, order: order, waiterName: waiterName)
                if op.holdAndRecall == true {
                    try await api.holdOrder(orderId: order.orderId, terminalId: "T01", label: "Client")
                    let held = try #require(try await api.heldOrders(terminalId: "T01").first { $0.orderId == order.orderId })
                    _ = try await api.recallHeldOrder(holdId: held.holdId)
                }
                var numbers: [String] = []
                let payments = op.payments ?? []
                for (p, list) in payments.enumerated() {
                    let request = CounterCheckoutRequest(
                        orderId: order.orderId, terminalId: "T01", destination: destination, pickupBuzzer: nil,
                        tipAmount: Money(cents: p == payments.count - 1 ? (op.tipCents ?? 0) : 0), requestFiscalReceiptPrint: false,
                        mealVoucherPolicy: .capAtBalance,
                        tenders: list.map {
                            CounterTender(method: payment($0.method), amount: Money(cents: $0.amountCents), tendered: Money(cents: $0.amountCents + ($0.extraTenderedCents ?? 0)))
                        }
                    )
                    numbers.append(try #require(try await api.counterCheckout(request).receiptNumber))
                }
                try await afterPayments(op, numbers: numbers)

            case "void_receipt":
                // Après la Z du poste : l'annulation doit être refusée (409) et sans effet fiscal.
                _ = try await api.login(pin: managerPin)
                let ref = try #require(op.ref)
                let paymentIndex = try #require(op.voidPaymentIndex)
                let ids = try #require(receiptsByRef[ref])
                do {
                    _ = try await api.voidReceipt(receiptId: ids[paymentIndex])
                    replay.voidRefusals.append((index, nil))
                } catch let APIError.server(status, _) {
                    replay.voidRefusals.append((index, status))
                }

            case "x_report":
                _ = try await api.login(pin: managerPin)
                replay.xReports.append((index, try await api.xReport(terminalId: "T01")))

            case "z_closure":
                _ = try await api.login(pin: managerPin)
                replay.zClosures.append((index, try await api.zClosure(terminalId: "T01", managerId: manager, managerName: "Responsable")))

            default:
                Issue.record("Opération inconnue : \(op.type)")
            }
        }
        return replay
    }

    // MARK: - Comparaison au grand livre

    /// Les taux et moyens de paiement à zéro n'ont pas d'écriture côté Swift : on compare les valeurs non nulles.
    private static func nonZero(_ values: [String: Int]) -> [String: Int] { values.filter { $0.value != 0 } }

    private static func vatSum(_ report: FiscalReport) -> Int { report.vatBreakdown.values.reduce(0) { $0 + $1.cents } }

    private static func check(_ report: FiscalReport, _ expected: Expected, _ context: Comment) {
        #expect(report.totalSalesTtc.cents == expected.ttcCents, context)
        #expect(report.totalSalesHt.cents == expected.htCents, context)
        #expect(report.receiptCount == expected.receiptCount, context)
        #expect(nonZero(report.vatBreakdown.mapValues(\.cents)) == nonZero(expected.vatByRate), context)
        #expect(nonZero(report.paymentTotals.mapValues(\.cents)) == nonZero(expected.tenderByMethod), context)
        #expect(report.totalSalesTtc.cents - report.totalSalesHt.cents == vatSum(report), context)
    }

    @Test(arguments: ["A", "B", "C"])
    func tousLesRapportsEgalentLeGrandLivreDuNet(key: String) async throws {
        let scenario = try Self.scenario()
        let expected = try #require(scenario.expected[key])
        let replay = try await Self.replay(scenario, terminal: key)
        let context = Comment(rawValue: "graine \(scenario.seed), poste \(key)")

        #expect(replay.zClosures.count == 1, context)
        let z = try #require(replay.zClosures.first, context)
        Self.check(z.report, expected, context)
        #expect(z.report.closureSequence == 1, context)
        #expect(z.report.perpetualGrandTotal.cents == expected.ttcCents, context)

        // Le dernier X avant la Z égale la Z ; chaque X reste cohérent (TTC − HT = Σ TVA).
        let xBefore = try #require(replay.xReports.last { $0.op < z.op }, context)
        Self.check(xBefore.report, expected, context)
        for x in replay.xReports {
            #expect(x.report.totalSalesTtc.cents - x.report.totalSalesHt.cents == Self.vatSum(x.report), context)
        }
    }

    @Test(arguments: ["A", "B", "C"])
    func apresLaZLeXRepartDeZeroEtLesAnnulationsTardivesSontRefusees(key: String) async throws {
        let scenario = try Self.scenario()
        let replay = try await Self.replay(scenario, terminal: key)
        let context = Comment(rawValue: "graine \(scenario.seed), poste \(key)")

        let z = try #require(replay.zClosures.first, context)
        let xAfter = try #require(replay.xReports.first { $0.op > z.op }, context)
        #expect(xAfter.report.totalSalesTtc == .zero, context)
        #expect(xAfter.report.receiptCount == 0, context)
        #expect(xAfter.report.perpetualGrandTotal == z.report.perpetualGrandTotal, context)

        #expect(!replay.voidRefusals.isEmpty, context)
        for attempt in replay.voidRefusals { #expect(attempt.status == 409, context) }
    }

    @Test func laSommeDesPostesEgaleLeTotalDuNet() async throws {
        let scenario = try Self.scenario()
        var ttc = 0
        for terminal in scenario.terminals {
            let replay = try await Self.replay(scenario, terminal: terminal.key)
            ttc += try #require(replay.zClosures.first).report.totalSalesTtc.cents
        }
        #expect(ttc == scenario.total.ttcCents)
    }
}
