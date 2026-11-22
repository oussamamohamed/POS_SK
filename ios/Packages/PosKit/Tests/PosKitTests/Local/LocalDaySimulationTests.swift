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
        var expectedLineVoids: [ExpectedLineVoid]
        var expectedLineVoidTotals: [String: [Int?]]
        var expectedTicketLines: [String: [TicketLine]]
    }

    /// Annulation ou correction d'une ligne (`DayLineVoid` du .NET) : phase, index de ligne (rajouts en fin de liste), attente.
    private struct LineVoid: Decodable {
        var phase: String
        var line: Int
        var quantity: Int
        var reason: String?
        var by: String
        var expect: Int
        var code: String?
        var reAdd: Int
        var legacy: Bool
    }

    private struct ExpectedLineVoid: Decodable, Hashable {
        var terminal: String
        var ref: String
        var table: String
        var productName: String
        var quantity: Int
        var unitPriceCents: Int
        var reason: String
        var serverName: String
        var hasTicket: Bool
    }

    private struct TicketLine: Decodable {
        var line: Int
        var sent: Int
        var cancelled: Int
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
        var lineVoids: [LineVoid]?
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
        /// Articles vendus et pourboires de chaque Z, sur la période signée de la clôture (comme `ReportPrintDataReferenceFixtureTests`).
        var zReportData: [ReportPrintData] = []
        var voidRefusals: [(op: Int, status: Int?)] = []
        var lineVoids: [LineVoidAttempt] = []
        /// Identifiants des lignes (rajouts compris) des commandes qui annulent ou corrigent, par `ref`.
        var lineIds: [String: [UUID]] = [:]
        var managerName = ""
        var printerId = UUID()
        var api: LocalPosAPI?
        var db: SQLiteDatabase?
    }

    /// Réponse d'une étape d'annulation : statut (200 si acceptée), message d'erreur, total TTC de la commande après une étape acceptée,
    /// reçus actifs de la commande à cet instant, X du poste juste avant et juste après.
    private struct LineVoidAttempt {
        var ref: String
        var step: Int
        var status: Int
        var message: String?
        var totalCents: Int?
        var activeReceipts: [String]
        var xBefore: FiscalReport
        var xAfter: FiscalReport
    }

    /// État d'une commande qui annule ou corrige : lignes (produit et ligne locale), reçus émis et reçus annulés.
    private final class VoidContext {
        let op: Operation
        let table: String
        let orderId: UUID
        let waiter: String
        var lines: [(line: Line, productId: UUID, lineId: UUID)]
        var receipts: [String] = []
        var voided: [String] = []
        init(op: Operation, table: String, orderId: UUID, waiter: String, lines: [(line: Line, productId: UUID, lineId: UUID)]) {
            self.op = op; self.table = table; self.orderId = orderId; self.waiter = waiter; self.lines = lines
        }
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
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar, transport: FakePrinterTransport())
        let db = try SQLiteDatabase(path: path)
        let managerLogin = try await api.login(pin: managerPin)
        let manager = try #require(managerLogin.operatorId)

        var pins: [String: String] = [:]
        var receiptsByRef: [String: [UUID]] = [:]
        var replay = Replay()
        replay.managerName = managerLogin.operatorName ?? ""
        replay.api = api
        replay.db = db
        // Comme le DayRunner : une seule imprimante active, du poste chaud (station par défaut des lignes), pour les bons d'annulation.
        for seeded in try await api.printers() { try await api.setTestPrinterActive(seeded.id, false) }
        let printer = Printer(name: "Cuisine chaude", ipAddress: "10.0.0.9", openCashDrawerOnReceipt: false, assignedStationIds: ["HOT_KITCHEN"], textMode: true)
        try await api.addTestPrinter(printer)
        replay.printerId = printer.id

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

        func input(_ line: Line, productId: UUID = UUID()) -> OrderItemInput {
            OrderItemInput(
                productId: productId, productName: line.name, quantity: line.quantity, unitPrice: Money(cents: line.unitPriceCents),
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

        /// Ajoute les lignes avec des produits connus : l'index de chaque ligne retrouve sa ligne locale.
        func addLines(_ op: Operation, table: String) async throws -> (ActiveOrder, [(line: Line, productId: UUID, lineId: UUID)]) {
            let lines = (op.lines ?? []).map { ($0, UUID()) }
            let order = try await api.addItems(table: table, items: lines.map { input($0.0, productId: $0.1) })
            let mapped = try lines.map { line, productId in
                (line: line, productId: productId, lineId: try #require(order.lines.first { $0.productId == productId }?.lineId))
            }
            return (order, mapped)
        }

        func status(of error: Error) -> (Int, String?) {
            switch error as? APIError {
            case .forbidden(let message)?: (403, message)
            case .notFound(let message)?: (404, message)
            case .server(let status, let message)?: (status, message)
            default: (-1, "\(error)")
            }
        }

        /// Joue les étapes d'une phase (refus compris) et note la réponse et le X avant / après ; le serveur se reconnecte ensuite.
        func steps(_ ctx: VoidContext, _ phase: String) async throws {
            guard let all = ctx.op.lineVoids, !all.isEmpty, let ref = ctx.op.ref else { return }
            for (i, step) in all.enumerated() where step.phase == phase {
                let line = ctx.lines[step.line]
                if step.legacy {
                    // Ligne envoyée avant `OrderItemId` : seule écriture directe en base du rejeu (aucune route ne produit ce cas).
                    try db.run("UPDATE KitchenTicketItems SET OrderItemId = NULL WHERE OrderItemId = ?", [.uuid(line.lineId)])
                }
                _ = try await api.login(pin: managerPin)
                let before = try await api.xReport(terminalId: "T01")
                if step.by == "waiter" { _ = try await login(waiter: ctx.waiter) }
                var result: (Int, String?) = (200, nil)
                do {
                    try await api.voidLine(orderId: ctx.orderId, lineId: line.lineId, quantity: step.quantity, reason: step.reason, operatorId: nil)
                } catch {
                    result = status(of: error)
                }
                let total = result.0 == 200 ? try await api.activeOrder(table: ctx.table)?.totalTtcAmount?.cents : nil
                _ = try await api.login(pin: managerPin)
                let after = try await api.xReport(terminalId: "T01")
                replay.lineVoids.append(LineVoidAttempt(
                    ref: ref, step: i, status: result.0, message: result.1, totalCents: total,
                    activeReceipts: ctx.receipts.filter { !ctx.voided.contains($0) }, xBefore: before, xAfter: after))
                _ = try await login(waiter: ctx.waiter)
                if step.reAdd > 0 {
                    // Même produit (pas de fusion avec la ligne envoyée), puis second envoi : second bon.
                    let known = Set(ctx.lines.map(\.lineId))
                    var readded = line.line
                    readded.quantity = step.reAdd
                    readded.comp = false
                    let order = try await api.addItems(table: ctx.table, items: [input(readded, productId: line.productId)])
                    let added = try #require(order.lines.first { !known.contains($0.lineId) }?.lineId)
                    ctx.lines.append((line: readded, productId: line.productId, lineId: added))
                    try await api.dispatch(table: ctx.table)
                }
            }
            replay.lineIds[ref] = ctx.lines.map(\.lineId)
        }

        /// Après le premier règlement : commande rouverte (reçu annulé puis étapes `reopened`), puis étapes `part_paid`.
        func afterPayment(_ ctx: VoidContext, _ payment: Int) async throws {
            guard payment == 0, ctx.op.lineVoids?.isEmpty == false else { return }
            if ctx.op.lineVoids?.contains(where: { $0.phase == "reopened" }) == true {
                _ = try await api.login(pin: managerPin)
                _ = try await api.voidReceipt(receiptId: try receiptId(ctx.receipts[0]))
                ctx.voided.append(ctx.receipts[0])
                _ = try await login(waiter: ctx.waiter)
                try await steps(ctx, "reopened")
            }
            try await steps(ctx, "part_paid")
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
                } else {
                    #expect(order.destination == .eatIn, "table \(table) ouverte sans mode : sur place par défaut")
                }
                let added = try await addLines(op, table: table)
                order = added.0
                try await discountAndComps(op, table: table, order: order, waiterName: waiterName)
                let ctx = VoidContext(op: op, table: table, orderId: order.orderId, waiter: waiterName, lines: added.1)
                try await steps(ctx, "draft")
                try await api.dispatch(table: table)
                try await steps(ctx, "sent")
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
                    ctx.receipts.append(try #require(try await api.pay(request).receiptNumber))
                    try await afterPayment(ctx, p)
                }
                try await steps(ctx, "paid")
                try await afterPayments(op, numbers: ctx.receipts)

            case "counter_order", "hold_then_void":
                let waiterName = try #require(op.waiter)
                _ = try await login(waiter: waiterName)
                let destination: OrderDestination = op.destination == "EatIn" ? .eatIn : .takeaway
                _ = try await api.openCounterOrder(terminalId: "T01", destination: destination)
                let (order, lines) = try await addLines(op, table: DiningTable.counterNumber)
                if op.type == "hold_then_void" {
                    try await api.holdOrder(orderId: order.orderId, terminalId: "T01", label: "Client")
                    let held = try #require(try await api.heldOrders(terminalId: "T01").first { $0.orderId == order.orderId })
                    try await api.voidHeldOrder(holdId: held.holdId, supervisorPin: managerPin, reason: "Client parti", terminalId: "T01")
                    continue
                }
                try await discountAndComps(op, table: DiningTable.counterNumber, order: order, waiterName: waiterName)
                let ctx = VoidContext(op: op, table: DiningTable.counterNumber, orderId: order.orderId, waiter: waiterName, lines: lines)
                try await steps(ctx, "draft")
                if op.lineVoids?.contains(where: { $0.phase != "draft" }) == true {
                    // Une vente comptoir n'est envoyée en cuisine que si le scénario y annule une ligne envoyée (comme le DayRunner).
                    try await api.dispatch(table: DiningTable.counterNumber)
                    try await steps(ctx, "sent")
                }
                if op.holdAndRecall == true {
                    try await api.holdOrder(orderId: order.orderId, terminalId: "T01", label: "Client")
                    let held = try #require(try await api.heldOrders(terminalId: "T01").first { $0.orderId == order.orderId })
                    _ = try await api.recallHeldOrder(holdId: held.holdId)
                }
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
                    ctx.receipts.append(try #require(try await api.counterCheckout(request).receiptNumber))
                    try await afterPayment(ctx, p)
                }
                try await steps(ctx, "paid")
                try await afterPayments(op, numbers: ctx.receipts)

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
                let fiscal = LocalFiscalRepository(db: db)
                let closure = try #require(try fiscal.latestClosure(terminalId: "T01"))
                replay.zReportData.append(try fiscal.reportPrintData(terminalId: "T01", periodStart: closure.periodStartUtc, periodEnd: closure.periodEndUtc))

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

    @Test(arguments: ["A", "B", "C"])
    func lesArticlesEtPourboiresDeChaqueZEgalentLeNet(key: String) async throws {
        let scenario = try Self.scenario()
        let reference = try LocalReportPrintDataTests.reference().day
        #expect(reference.seed == scenario.seed, "report_print_data_reference.json et day_scenario.json viennent de la même graine")
        let expected = try #require(reference.terminals[key])
        let replay = try await Self.replay(scenario, terminal: key)
        let context = Comment(rawValue: "graine \(scenario.seed), poste \(key)")

        #expect(replay.zReportData.count == expected.count, context)
        for (actual, wanted) in zip(replay.zReportData, expected) {
            #expect(actual.categories.map(\.categoryName) == wanted.categories.map(\.categoryName), context)
            for (a, w) in zip(actual.categories, wanted.categories) { #expect(a.items == w.items, context) }
            #expect(actual.tips == wanted.tips, context)
            #expect(actual == wanted, context)
        }
    }

    // MARK: - Annulations et corrections de ligne

    private static func sameMoney(_ a: FiscalReport, _ b: FiscalReport) -> Bool {
        a.totalSalesTtc == b.totalSalesTtc && a.totalSalesHt == b.totalSalesHt && a.receiptCount == b.receiptCount
            && a.vatBreakdown == b.vatBreakdown && a.paymentTotals == b.paymentTotals && a.perpetualGrandTotal == b.perpetualGrandTotal
    }

    /// Une annulation telle que la ligne `OrderLineVoids` (ou le JET) la porte, sans la référence de commande (absente des deux).
    private static func key(_ v: ExpectedLineVoid) -> String {
        "\(v.table)|\(v.productName)|\(v.quantity)|\(v.unitPriceCents)|\(v.reason)|\(v.serverName)|\(v.hasTicket)"
    }

    @Test(arguments: ["A", "B", "C"])
    func lesAnnulationsEtCorrectionsDeLigneSuiventLeNet(key: String) async throws {
        let scenario = try Self.scenario()
        let replay = try await Self.replay(scenario, terminal: key)
        let api = try #require(replay.api)
        let db = try #require(replay.db)
        let context = Comment(rawValue: "graine \(scenario.seed), poste \(key)")
        let orders = scenario.operations.filter { $0.terminal == key && $0.lineVoids?.isEmpty == false }
        #expect(!orders.isEmpty, context)
        #expect(replay.lineVoids.count == orders.reduce(0) { $0 + ($1.lineVoids?.count ?? 0) }, context)

        // Réponse de chaque étape, total après une étape acceptée, aucun effet sur le X (ni reçu, ni montant).
        for op in orders {
            let ref = try #require(op.ref)
            let steps = try #require(op.lineVoids)
            let totals = try #require(scenario.expectedLineVoidTotals[ref])
            let attempts = replay.lineVoids.filter { $0.ref == ref }
            #expect(attempts.map(\.step) == Array(steps.indices), context)
            for attempt in attempts {
                let step = steps[attempt.step]
                let c = Comment(rawValue: "\(context.rawValue), \(ref) étape \(attempt.step) (\(step.phase)) : \(attempt.message ?? "")")
                #expect(attempt.status == step.expect, c)
                #expect(attempt.totalCents == totals[attempt.step], c)
                #expect(Self.sameMoney(attempt.xBefore, attempt.xAfter), c)
                switch step.code {
                case "order_has_receipts":
                    #expect(attempt.message?.contains("a déjà des reçus") == true && !attempt.activeReceipts.isEmpty, c)
                    for number in attempt.activeReceipts { #expect(attempt.message?.contains(number) == true, c) }
                case "order_not_modifiable":
                    #expect(attempt.message == "Cette commande n'est plus modifiable.", c)
                default:
                    break
                }
            }
        }

        // Une ligne OrderLineVoids et une écriture du JET par annulation acceptée (R2 serveur, R9 prix + suppléments), rien pour une correction.
        let expected = scenario.expectedLineVoids.filter { $0.terminal == key }.map(Self.key).sorted()
        let rows = try db.query("SELECT * FROM OrderLineVoids")
        let actual = rows.map {
            Self.key(ExpectedLineVoid(
                terminal: key, ref: "", table: $0.string("TableNumber") ?? "", productName: $0.string("ProductName") ?? "", quantity: $0.int("Quantity") ?? 0,
                unitPriceCents: $0.int("UnitPrice") ?? 0, reason: $0.string("Reason") ?? "", serverName: $0.string("ServerName") ?? "", hasTicket: $0.uuid("TicketId") != nil))
        }.sorted()
        #expect(actual == expected, context)
        #expect(rows.allSatisfy { $0.string("OperatorName") == replay.managerName }, context)

        let jet = try await api.journalRows(LocalJournalEvents.orderLineVoided).map { row -> String in
            let p = (try? JSONSerialization.jsonObject(with: Data(row.payload.utf8))) as? [String: Any] ?? [:]
            #expect(p["operatorName"] as? String == replay.managerName, context)
            return Self.key(ExpectedLineVoid(
                terminal: key, ref: "", table: p["tableNumber"] as? String ?? "", productName: p["productName"] as? String ?? "",
                quantity: p["quantity"] as? Int ?? 0, unitPriceCents: p["unitPriceCents"] as? Int ?? 0, reason: p["reason"] as? String ?? "",
                serverName: "", hasTicket: !(p["ticketId"] is NSNull)))
        }.sorted()
        // Le JET ne porte pas le serveur : on compare sans lui.
        let withoutServer = scenario.expectedLineVoids.filter { $0.terminal == key }.map { v -> String in
            var v = v
            v.serverName = ""
            return Self.key(v)
        }.sorted()
        #expect(jet == withoutServer, context)

        // Articles de bon : quantité envoyée et annulée, annulé quand tout l'est ; rien pour une ligne jamais envoyée ou ancienne.
        for op in orders {
            let ref = try #require(op.ref)
            let wanted = try #require(scenario.expectedTicketLines[ref])
            for (index, lineId) in try #require(replay.lineIds[ref]).enumerated() {
                let items = try db.query("SELECT Quantity, CancelledQuantity, Status FROM KitchenTicketItems WHERE OrderItemId = ?", [.uuid(lineId)])
                let c = Comment(rawValue: "\(context.rawValue), \(ref) ligne \(index)")
                guard let want = wanted.first(where: { $0.line == index }) else {
                    #expect(items.isEmpty, c)
                    continue
                }
                #expect(items.count == 1, c)
                let item = try #require(items.first, c)
                #expect(item.int("Quantity") == want.sent && item.int("CancelledQuantity") == want.cancelled, c)
                let status = want.cancelled >= want.sent ? TicketItemStatus.cancelled : TicketItemStatus.pending
                #expect(item.int("Status") == status.rawValue, c)
            }
        }

        // Un bon d'annulation par annulation acceptée sur l'imprimante du poste chaud, aucun pour une correction ou un refus.
        let reasons = try db.query("SELECT DocumentJson FROM PrintJobs WHERE PrinterId = ?", [.uuid(replay.printerId)]).compactMap { row -> String? in
            let document = try TicketDocumentJSON.deserialize(row.string("DocumentJson") ?? "")
            return document.lines.compactMap { line -> String? in
                if case .text(let text, _, _, _) = line, text.hasPrefix("Motif : ") { return text }
                return nil
            }.first
        }.sorted()
        #expect(reasons == scenario.expectedLineVoids.filter { $0.terminal == key }.map { "Motif : \($0.reason)" }.sorted(), context)

        // Commande vidée par les annulations : reste sur sa table, à 0, sans reçu ; la Z est passée quand même.
        for op in orders where op.payments?.isEmpty == true {
            let table = try #require(op.table, context)
            let order = try #require(try await api.activeOrder(table: table), context)
            #expect(order.lines.isEmpty && (order.totalTtcAmount?.cents ?? 0) == 0, context)
            #expect(try db.query("SELECT Id FROM FiscalReceipts WHERE OrderId = ?", [.uuid(order.orderId)]).isEmpty, context)
            #expect(replay.zClosures.count == 1, context)
        }
        #expect(try await api.verifyChains().isValid, context)
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
