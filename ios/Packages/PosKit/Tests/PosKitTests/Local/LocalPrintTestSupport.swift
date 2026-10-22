import Foundation
@testable import PosKit

/// Accès de test à la file et aux imprimantes, sur l'acteur (la base n'en sort pas).
extension LocalPosAPI {
    func addTestPrinter(_ printer: Printer) throws { try printerRepository.insert(printer) }

    func setTestPrinterActive(_ id: UUID, _ active: Bool) throws {
        guard var printer = try printerRepository.printer(id: id) else { return }
        printer.isActive = active
        _ = try printerRepository.update(printer)
    }

    @discardableResult
    func enqueueTestJob(printer: UUID, marker: String = "JOB", kind: PrintJobKind = .receipt, json: String? = nil, drawer: Bool = false) throws -> PrintJobRecord {
        let doc = TicketDocument(language: "fr", rightToLeft: false, lines: [.text(marker)])
        return try LocalPrintQueueRepository(db: db).enqueue(
            printerId: printer, kind: kind, documentJson: try json ?? TicketDocumentJSON.serialize(doc), openCashDrawer: drawer,
            duplicateOfDocumentId: nil, duplicateNumber: nil, now: clock())
    }

    func testJob(_ id: UUID) throws -> PrintJobRecord? { try LocalPrintQueueRepository(db: db).job(id: id) }

    func saveTestJob(_ job: PrintJobRecord) throws { try LocalPrintQueueRepository(db: db).save(job) }

    func allTestJobs() throws -> [PrintJobRecord] {
        try db.query("SELECT Id FROM PrintJobs ORDER BY CreatedAtUtc, Id").compactMap { $0.uuid("Id") }.compactMap { try testJob($0) }
    }

    func journalRows(_ eventType: String) throws -> [(payload: String, terminalId: String?, operatorId: UUID?)] {
        try db.query("SELECT PayloadJson, TerminalId, OperatorId FROM JournalEntries WHERE EventType = ? ORDER BY ChainSequence", [.text(eventType)])
            .map { ($0.string("PayloadJson") ?? "", $0.string("TerminalId"), $0.uuid("OperatorId")) }
    }

    var testInFlightCount: Int { printersInFlight.count }
}

/// Imprimante texte (rendu rapide, octets lisibles).
func textPrinter(_ name: String, ip: String = "10.0.0.5", drawer: Bool = false) -> Printer {
    Printer(name: name, ipAddress: ip, openCashDrawerOnReceipt: drawer, textMode: true)
}

func makePrintAPI(clock: LocalTestClock, transport: FakePrinterTransport, path: String = ":memory:", pin: String = "1234") async throws -> LocalPosAPI {
    let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar, transport: transport)
    _ = try await api.login(pin: pin)
    return api
}

/// Le marqueur ASCII figure dans les octets (rendu texte PC858 : ASCII inchangé).
func payload(_ bytes: [UInt8], contains marker: String) -> Bool {
    let needle = Array(marker.utf8)
    guard needle.count <= bytes.count else { return false }
    return (0...(bytes.count - needle.count)).contains { Array(bytes[$0..<($0 + needle.count)]) == needle }
}

/// Porte qui suspend `send` jusqu'à `open()` ; `entered` passe à vrai dès qu'un envoi l'atteint.
actor SendLatch {
    private(set) var entered = 0
    private var waiters: [CheckedContinuation<Void, Never>] = []
    private var isOpen = false

    func wait() async {
        entered += 1
        if isOpen { return }
        await withCheckedContinuation { waiters.append($0) }
    }

    func open() {
        isOpen = true
        waiters.forEach { $0.resume() }
        waiters.removeAll()
    }

    /// Cède la main jusqu'à ce qu'un envoi soit suspendu (sans dormir).
    func waitUntilEntered() async {
        for _ in 0..<100_000 where entered == 0 { await Task.yield() }
    }
}

/// Cède la main jusqu'à ce que la condition soit vraie, sans dormir et sans jamais bloquer le test indéfiniment.
func yieldUntil(_ condition: () async -> Bool) async {
    for _ in 0..<100_000 { if await condition() { return }; await Task.yield() }
}

actor Flag {
    private(set) var isSet = false
    func set() { isSet = true }
}
