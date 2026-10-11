import Foundation
import Testing
@testable import PosKit

@Suite("LocalFiscalRepository : chaîne fiscale")
struct LocalFiscalRepositoryTests {
    private func makeRepository() throws -> (LocalFiscalRepository, SQLiteDatabase) {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return (LocalFiscalRepository(db: db), db)
    }

    private static let amounts = FiscalReceiptAmounts(ttcCents: 1250, htCents: 1136, taxBreakdownJson: "{\"10\":114}", htBreakdownJson: "{\"10\":1136}")
    private let cash = [FiscalTenderInput(method: .cash, amount: 1250, tendered: 2000, change: 750)]
    private let at = Date(timeIntervalSince1970: 1_760_087_880.123)

    @discardableResult
    private func append(_ repo: LocalFiscalRepository, order: UUID = UUID(), terminal: String = "T01", now: Date? = nil) throws -> FiscalReceiptRecord {
        try repo.appendReceipt(orderId: order, terminalId: terminal, amounts: Self.amounts, tenders: cash, now: now ?? at)
    }

    @Test func receiptsAreChainedAndSignedLikeTheBackend() throws {
        let (repo, db) = try makeRepository()
        let first = try append(repo)
        let second = try append(repo)
        #expect(first.receiptNumber == "T01-000001" && second.receiptNumber == "T01-000002" && second.sequence == 2)
        let rows = try db.query("SELECT * FROM FiscalReceipts ORDER BY SequenceNumber")
        #expect(rows[0].string("PreviousSignatureHash") == FiscalHashing.genesisHash)
        #expect(rows[1].string("PreviousSignatureHash") == first.signature)
        let expected = FiscalHashing.receiptHash(
            previousHash: FiscalHashing.genesisHash, terminalId: "T01", sequence: 1, amountCents: 1250,
            timestamp: FiscalHashing.timestampText(FiscalHashing.roundedToMilliseconds(at)), taxBreakdownJson: "{\"10\":114}"
        )
        #expect(first.signature == expected && rows[0].string("SignatureHash") == expected)
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ?", [.uuid(first.id)]).first)
        #expect(tender.int("Amount") == 1250 && tender.int("Tendered") == 2000 && tender.int("ChangeGiven") == 750 && tender.int("Method") == 0)
    }

    @Test func theHtBreakdownIsStoredUnsignedAndMirroredByTheVoid() throws {
        let (repo, db) = try makeRepository()
        let sale = try append(repo)
        #expect(sale.htBreakdownJson == "{\"10\":1136}")
        #expect(try db.query("SELECT HtBreakdownJson FROM FiscalReceipts WHERE Id = ?", [.uuid(sale.id)]).first?.string("HtBreakdownJson") == "{\"10\":1136}")
        // hors signature : même hash qu'avec la seule TVA
        #expect(sale.signature == FiscalHashing.receiptHash(
            previousHash: FiscalHashing.genesisHash, terminalId: "T01", sequence: 1, amountCents: 1250,
            timestamp: FiscalHashing.timestampText(FiscalHashing.roundedToMilliseconds(at)), taxBreakdownJson: "{\"10\":114}"
        ))
        let voided = try #require(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at))
        #expect(voided.htBreakdownJson == "{\"10\":-1136}")
        #expect(try repo.receipt(id: voided.id)?.htBreakdownJson == "{\"10\":-1136}")
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func aReceiptWithoutHtBreakdownKeepsNullAndItsVoidToo() throws {
        let (repo, db) = try makeRepository()
        let legacy = FiscalReceiptAmounts(ttcCents: 1250, htCents: 1136, taxBreakdownJson: "{\"10\":114}")
        let sale = try repo.appendReceipt(orderId: UUID(), terminalId: "T01", amounts: legacy, tenders: cash, now: at)
        let voided = try #require(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at))
        #expect(sale.htBreakdownJson == nil && voided.htBreakdownJson == nil)
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts WHERE HtBreakdownJson IS NULL").first?.int("n") == 2)
    }

    @Test func eachTerminalHasItsOwnChain() throws {
        let (repo, _) = try makeRepository()
        try append(repo)
        let other = try append(repo, terminal: "T02")
        #expect(other.sequence == 1 && other.receiptNumber == "T02-000001")
    }

    @Test func twoReceiptsInTheSameMillisecondStayOrderedBySequence() throws {
        let (repo, _) = try makeRepository()
        let a = try append(repo, now: at), b = try append(repo, now: at)
        #expect(a.sequence == 1 && b.sequence == 2)
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func paidAmountIsTheNetOfTendersAndVoids() throws {
        let (repo, _) = try makeRepository()
        let order = UUID()
        #expect(try repo.paidCents(orderId: order) == 0 && repo.priorFiscalTtcCents(orderId: order) == 0)
        let sale = try append(repo, order: order)
        #expect(try repo.paidCents(orderId: order) == 1250 && repo.priorFiscalTtcCents(orderId: order) == 1250)
        _ = try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at)
        #expect(try repo.paidCents(orderId: order) == 0 && repo.priorFiscalTtcCents(orderId: order) == 0)
    }

    @Test func voidingWritesANegativeLinkedReceiptAndAJournalEntry() throws {
        let (repo, db) = try makeRepository()
        let sale = try append(repo)
        let operatorId = UUID()
        let voided = try #require(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: operatorId, now: at))
        #expect(voided.receiptNumber == "T01-VOID-000002" && voided.ttcCents == -1250 && voided.htCents == -1136 && voided.taxBreakdownJson == "{\"10\":-114}")
        let row = try #require(try db.query("SELECT * FROM FiscalReceipts WHERE Id = ?", [.uuid(voided.id)]).first)
        #expect(row.uuid("VoidedReceiptId") == sale.id && row.int("IsVoid") == 0)
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ?", [.uuid(voided.id)]).first)
        #expect(tender.int("Amount") == -1250 && tender.int("Tendered") == -2000 && tender.int("ChangeGiven") == -750)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries").first)
        #expect(entry.string("EventType") == "RECEIPT_VOIDED" && entry.int("ChainSequence") == 1 && entry.uuid("OperatorId") == operatorId)
        let payload = try #require(entry.string("PayloadJson"))
        #expect(payload.contains("\"OriginalReceiptNumber\":\"T01-000001\"") && payload.contains("\"VoidReceiptNumber\":\"T01-VOID-000002\"") && payload.contains("\"TotalTtcCents\":1250"))
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func voidsAreRefusedWithoutWritingAnything() throws {
        let (repo, db) = try makeRepository()
        let sale = try append(repo)
        let otherTerminal = try append(repo, terminal: "T02")
        #expect(try repo.voidReceipt(originalId: otherTerminal.id, terminalId: "T01", operatorId: UUID(), now: at) == nil) // autre terminal
        #expect(try repo.voidReceipt(originalId: UUID(), terminalId: "T01", operatorId: UUID(), now: at) == nil) // inconnu
        let voided = try #require(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at))
        #expect(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at) == nil) // déjà annulé
        #expect(try repo.voidReceipt(originalId: voided.id, terminalId: "T01", operatorId: UUID(), now: at) == nil) // annulation d'une annulation
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 3)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries").first?.int("n") == 1)
    }

    @Test func aFailureWhileWritingTendersRollsTheWholeReceiptBack() throws {
        let (repo, db) = try makeRepository()
        try db.exec("CREATE TEMP TRIGGER fail_tender BEFORE INSERT ON main.PaymentTenders BEGIN SELECT RAISE(ABORT, 'boom'); END;")
        #expect(throws: SQLiteError.self) { try db.transaction { try append(repo) } }
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 0)
    }

    @Test func journalEntriesAreChainedAndSignedLikeTheBackend() throws {
        let (repo, db) = try makeRepository()
        let operatorId = UUID()
        try repo.appendJournal(eventType: "TEST", payloadJson: FiscalJSON.object([("Reason", FiscalJSON.string("Dérogation +10 %"))]), terminalId: "T01", operatorId: operatorId, now: at)
        try repo.appendJournal(eventType: "TEST2", payloadJson: "{}", terminalId: nil, operatorId: nil, now: at)
        let rows = try db.query("SELECT * FROM JournalEntries ORDER BY ChainSequence")
        let firstHash = FiscalHashing.jetHash(
            previousHash: FiscalHashing.genesisHash, chainSequence: 1, eventType: "TEST",
            timestamp: FiscalHashing.timestampText(FiscalHashing.roundedToMilliseconds(at)), terminalId: "T01", operatorId: operatorId,
            payloadJson: "{\"Reason\":\"D\\u00E9rogation \\u002B10 %\"}"
        )
        #expect(rows[0].string("EntryHash") == firstHash && rows[1].string("PreviousHash") == firstHash && rows[1].int("ChainSequence") == 2)
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func duplicatesAreNumberedFromTheJournalPerDocument() throws {
        let (repo, db) = try makeRepository()
        let a = try append(repo), b = try append(repo)
        #expect(try repo.nextDuplicateNumber(documentId: a.id) == 1)
        #expect(try repo.recordDuplicate(of: a, operatorId: UUID(), now: at) == 1)
        #expect(try repo.recordDuplicate(of: a, operatorId: UUID(), now: at) == 2)
        #expect(try repo.nextDuplicateNumber(documentId: b.id) == 1)
        let payload = try #require(try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'DUPLICATE_PRINTED' ORDER BY ChainSequence").first?.string("PayloadJson"))
        #expect(payload.contains("\"documentType\":\"receipt\"") && payload.contains("\"duplicateNumber\":1") && payload.contains("\"printQueued\":false") && payload.contains("\"receiptNumber\":\"T01-000001\""))
    }

    @Test func receiptsAreFoundByIdOrNumber() throws {
        let (repo, _) = try makeRepository()
        let sale = try append(repo)
        #expect(try repo.receipt(identifier: sale.id.uuidString)?.id == sale.id)
        #expect(try repo.receipt(identifier: sale.id.uuidString.lowercased())?.id == sale.id)
        #expect(try repo.receipt(identifier: "T01-000001")?.id == sale.id)
        #expect(try repo.receipt(identifier: "nope") == nil)
    }

    @Test func verifyChainsDetectsTamperingAndGaps() throws {
        let (repo, db) = try makeRepository()
        try append(repo)
        try append(repo)
        try append(repo)
        try repo.appendJournal(eventType: "TEST", payloadJson: "{}", terminalId: "T01", operatorId: nil, now: at)
        #expect(try repo.verifyChains().isEmpty)

        try db.exec("DROP TRIGGER TR_FiscalReceipts_NoUpdate; DROP TRIGGER TR_FiscalReceipts_NoDelete; DROP TRIGGER TR_PaymentTenders_NoDelete; DROP TRIGGER TR_JournalEntries_NoUpdate")
        try db.run("UPDATE FiscalReceipts SET TotalTtcAmount = 1 WHERE SequenceNumber = 2")
        try db.run("UPDATE JournalEntries SET PayloadJson = '{\"x\":1}'")
        let issues = try repo.verifyChains()
        #expect(issues.contains { $0.contains("signature_mismatch") && $0.contains("T01") })
        #expect(issues.contains { $0.contains("jet") && $0.contains("signature_mismatch") })

        try db.run("DELETE FROM PaymentTenders WHERE FiscalReceiptId IN (SELECT Id FROM FiscalReceipts WHERE SequenceNumber = 2)")
        try db.run("DELETE FROM FiscalReceipts WHERE SequenceNumber = 2")
        #expect(try repo.verifyChains().contains { $0.contains("sequence_gap") })
    }

    @Test func storedDatesSurviveTheRoundTripOverManyTimestamps() throws {
        let (repo, _) = try makeRepository()
        var generator = SplitMix64(seed: 0x5EED_0525)
        for i in 0..<320 {
            let base = 1_760_000_000 + Double.random(in: 0..<1_000_000, using: &generator)
            let now = i % 3 == 0 ? Date(timeIntervalSince1970: floor(base) + 0.002 - 1e-9) : Date(timeIntervalSince1970: base)
            try append(repo, now: now)
            try repo.appendJournal(eventType: "TEST", payloadJson: "{}", terminalId: "T01", operatorId: nil, now: now)
        }
        #expect(try repo.verifyChains().isEmpty)
    }
}

/// Générateur déterministe (SplitMix64) : le test d'aller-retour des dates reste reproductible.
struct SplitMix64: RandomNumberGenerator {
    private var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}
