import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : reçus fiscaux, annulation et duplicata")
struct LocalFiscalPaymentTests {
    private func seat(_ api: LocalPosAPI, _ table: String = "T1", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func payingWritesAChainedFiscalReceipt() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950, tendered: 2000)]))
        #expect(result.receiptNumber == "T01-000001" && result.changeGiven == Money(cents: 50))
        let receipt = try #require(try db.query("SELECT * FROM FiscalReceipts").first)
        #expect(receipt.int("TotalTtcAmount") == 1950 && receipt.string("PreviousSignatureHash") == FiscalHashing.genesisHash)
        #expect(receipt.string("SignatureHash") == result.fiscalSignature && receipt.string("TerminalId") == "T01")
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders").first)
        #expect(tender.int("Amount") == 1950 && tender.int("Tendered") == 2000 && tender.int("ChangeGiven") == 50)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'NonFiscalPayments'").isEmpty)
    }

    @Test func everyReceiptStoresItsHtPerRate_AndSumsToTheReceiptHt() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        let rows = try db.query("SELECT * FROM FiscalReceipts ORDER BY SequenceNumber")
        #expect(rows.count == 2)
        for row in rows {
            let json = try #require(row.string("HtBreakdownJson"))
            #expect(FiscalReceiptMathTests.vatSum(json) == row.int("TotalHtAmount"))
        }
    }

    @Test func splitPaymentsProduceLinkedReceipts() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        let first = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        let second = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        #expect(first.receiptNumber == "T01-000001" && second.receiptNumber == "T01-000002" && second.remainingBalance == .zero)
        let rows = try db.query("SELECT * FROM FiscalReceipts ORDER BY SequenceNumber")
        #expect(rows[1].string("PreviousSignatureHash") == first.fiscalSignature)
        #expect(rows.map { $0.int("TotalTtcAmount") ?? 0 }.reduce(0, +) == 1950)
    }

    @Test func aFreeOrderIsClosedWithAZeroReceipt() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        let line = try #require(order.lines.first)
        try await api.compItem(orderId: order.orderId, lineId: line.lineId, reason: "Geste commercial", operatorId: nil)
        let free = try #require(try await api.activeOrder(table: "T1"))
        let result = try await api.pay(localPayment(free, table: "T1", tenders: []))
        #expect(result.receiptNumber == "T01-000001" && result.totalPaid == .zero && result.remainingBalance == .zero)
        #expect(try db.query("SELECT TotalTtcAmount FROM FiscalReceipts").first?.int("TotalTtcAmount") == 0)
        #expect(try await api.tables().first { $0.tableNumber == "T1" }?.status == .free)
    }

    @Test func emptyTendersStayRefusedWhenSomethingIsDue() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        await #expect(throws: APIError.server(status: 400, message: "Échec de l'encaissement.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: []))
        }
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 0)
    }

    @Test func chargingARoomWritesAFiscalReceiptWithARoomTender() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api, "T3")
        let request = RoomChargeRequest(
            orderId: order.orderId, tableNumber: "T3", roomNumber: "101", guestName: "Jean Dujardin", amount: Money(cents: 1950),
            tipAmount: Money(cents: 100), signatureDataUrl: nil, notes: nil
        )
        _ = try await api.chargeRoom(request)
        let receipt = try #require(try db.query("SELECT * FROM FiscalReceipts").first)
        #expect(receipt.string("ReceiptNumber") == "T01-000001" && receipt.int("TotalTtcAmount") == 1950)
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders").first)
        #expect(tender.int("Method") == PaymentMethod.roomCharge.rawValue && tender.int("Amount") == 2050)
        #expect(try await api.activeOrder(table: "T3") == nil)
    }

    @Test func voidingAReceiptCancelsTheOrderAndIsJournaled() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let id = try #require(try db.query("SELECT Id FROM FiscalReceipts").first?.uuid("Id"))
        let result = try await api.voidReceipt(receiptId: id)
        #expect(result.success == true && result.message?.contains("T01-VOID-000002") == true)
        #expect(try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(order.orderId)]).first?.int("Status") == LocalOrderStatus.cancelled.rawValue)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = 'RECEIPT_VOIDED'").first?.int("n") == 1)
        await #expect(throws: APIError.self) { try await api.voidReceipt(receiptId: id) }
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func aRoomChargeReceiptCannotBeVoided() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api, "T3")
        _ = try await api.chargeRoom(RoomChargeRequest(
            orderId: order.orderId, tableNumber: "T3", roomNumber: "101", guestName: "Jean Dujardin", amount: Money(cents: 1950),
            tipAmount: .zero, signatureDataUrl: nil, notes: nil
        ))
        let id = try #require(try db.query("SELECT Id FROM FiscalReceipts").first?.uuid("Id"))
        let balance = try db.query("SELECT CurrentBalance FROM HotelRooms WHERE RoomNumber = '101'").first?.string("CurrentBalance")
        let status = try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(order.orderId)]).first?.int("Status")
        let refused = APIError.server(status: 409, message: "Annulation impossible : ce reçu comprend une facturation chambre (la note de chambre ne serait pas rétablie).")
        await #expect(throws: refused) { try await api.voidReceipt(receiptId: id) }
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 1)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = 'RECEIPT_VOIDED'").first?.int("n") == 0)
        #expect(try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(order.orderId)]).first?.int("Status") == status)
        #expect(try db.query("SELECT CurrentBalance FROM HotelRooms WHERE RoomNumber = '101'").first?.string("CurrentBalance") == balance)
    }

    @Test func onlyAManagerCanVoidOrReprint() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let id = try #require(try db.query("SELECT Id FROM FiscalReceipts").first?.uuid("Id"))
        _ = try await api.login(pin: "2468") // serveur
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.voidReceipt(receiptId: id) }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.reprintReceipt(receiptIdentifier: id.uuidString) }
    }

    @Test func reprintingNumbersTheDuplicatesAndJournalsThemWithoutAPrinter() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        // Sans imprimante : les imprimantes de démonstration (dont celle du poste RECEIPT) sont désactivées.
        for printer in try await api.printers() { try await api.setTestPrinterActive(printer.id, false) }
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let first = try await api.reprintReceipt(receiptIdentifier: "T01-000001")
        let second = try await api.reprintReceipt(receiptIdentifier: "T01-000001")
        #expect(first == ReprintResult(printQueued: false, duplicateNumber: 1) && second.duplicateNumber == 2)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = 'DUPLICATE_PRINTED'").first?.int("n") == 2)
        await #expect(throws: APIError.self) { try await api.reprintReceipt(receiptIdentifier: "inconnu") }
    }

    @Test func concurrentPaymentsKeepAContiguousChain() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let first = try await seat(api, "T1"), second = try await seat(api, "T2")
        async let a = api.pay(localPayment(first, table: "T1", tenders: [localTender(.cash, 1950)]))
        async let b = api.pay(localPayment(second, table: "T2", tenders: [localTender(.cash, 1950)]))
        _ = try await (a, b)
        #expect(try db.query("SELECT SequenceNumber FROM FiscalReceipts ORDER BY SequenceNumber").compactMap { $0.int("SequenceNumber") } == [1, 2])
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }
}
