import Foundation
import Testing
@testable import PosKit

@Suite("Clôtures de période : parité avec la fixture .NET")
struct LocalPeriodClosureReferenceTests {
    private struct Reference: Decodable {
        var timeZone: String
        var cases: [Case]
    }

    private struct Case: Decodable {
        var name: String
        var fiscalYearStartMonth: Int
        var fiscalYearStartDay: Int
        var dailyClosures: [Daily]
        var steps: [Step]
    }

    private struct Daily: Decodable {
        var sequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesJson: String
        var tendersJson: String
        var perpetualCents: Int
    }

    private struct Step: Decodable {
        var type: String
        var key: String
        var nowUtc: String
        var error: String?
        var days: [String]?
        var months: [String]?
        var result: Result?
    }

    private struct Result: Decodable {
        var sequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesJson: String
        var tendersJson: String
        var perpetualCents: Int
        var dailyClosureCount: Int
        var previousSignatureHash: String
        var signatureHash: String
    }

    private static let manager = UUID()

    private static func reference() throws -> Reference {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("period_closure_reference"))
    }

    private static func insertDaily(_ db: SQLiteDatabase, _ d: Daily) throws {
        try db.run(
            """
            INSERT INTO DailyFiscalClosures (Id, TerminalId, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalSalesTtc, TotalSalesHt,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, PreviousSignatureHash, SignatureHash, SealedByUserName, SealedByUserId, CreatedAtUtc)
            VALUES (?, 'T01', ?, ?, ?, ?, ?, ?, ?, ?, '', '', '', ?, ?)
            """,
            [.uuid(UUID()), .integer(d.sequence), .text(d.periodStartUtc), .text(d.periodEndUtc), .integer(d.ttcCents), .integer(d.htCents),
             .text(d.taxesJson), .text(d.tendersJson), .integer(d.perpetualCents), .uuid(manager), .text(d.periodEndUtc)]
        )
    }

    @Test func everyCaseOfTheDotNetFixtureReplaysIdentically() throws {
        let reference = try Self.reference()
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try #require(TimeZone(identifier: reference.timeZone))

        for c in reference.cases {
            let db = try SQLiteDatabase(path: ":memory:")
            try LocalMigrator.migrate(db)
            for d in c.dailyClosures { try Self.insertDaily(db, d) }
            let repo = LocalFiscalRepository(db: db)

            for step in c.steps {
                let context = Comment(rawValue: "\(c.name) — \(step.type) \(step.key)")
                let type: FiscalPeriodType = step.type == "monthly" ? .monthly : .annual
                let now = try #require(SQLDate.parse(step.nowUtc))
                func run() throws -> PeriodClosureRecord {
                    try repo.executePeriodClosure(
                        terminalId: "T01", type: type, key: step.key, managerId: Self.manager, managerName: "Responsable",
                        fiscalYearStartMonth: c.fiscalYearStartMonth, fiscalYearStartDay: c.fiscalYearStartDay, calendar: calendar, now: now
                    )
                }

                if let error = step.error {
                    let expected: PeriodClosureError = switch error {
                    case "period_not_ended": .notEnded
                    case "period_already_closed": .alreadyClosed
                    case "missing_monthly_closures": .missingMonthlyClosures(step.months ?? [])
                    case "missing_daily_closures": .missingDailyClosures(step.days ?? [])
                    default: .invalidKey
                    }
                    #expect(throws: expected) { try run() }
                    continue
                }

                let expected = try #require(step.result, context)
                let actual = try run()
                #expect(actual.sequence == expected.sequence, context)
                #expect(FiscalHashing.storedText(actual.periodStartUtc) == expected.periodStartUtc, context)
                #expect(FiscalHashing.storedText(actual.periodEndUtc) == expected.periodEndUtc, context)
                #expect(actual.totalTtcCents == expected.ttcCents, context)
                #expect(actual.totalHtCents == expected.htCents, context)
                #expect(actual.taxesSummaryJson == expected.taxesJson, context)
                #expect(actual.tenderTotalsJson == expected.tendersJson, context)
                #expect(actual.perpetualGrandTotalCents == expected.perpetualCents, context)
                #expect(actual.dailyClosureCount == expected.dailyClosureCount, context)
                #expect(actual.previousSignatureHash == expected.previousSignatureHash, context)
                #expect(actual.signatureHash == expected.signatureHash, context)
            }
        }
    }

    @Test func parseIntObjectKeepsTheKeyOrder() {
        #expect(FiscalJSON.parseIntObject("{\"5.5\":900,\"10\":835}")?.map(\.0) == ["5.5", "10"])
        #expect(FiscalJSON.parseIntObject("{}")?.isEmpty == true)
        #expect(FiscalJSON.parseIntObject("pas du json") == nil)
    }

    @Test func aKeyThatIsNotADateIsRefused() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        let repo = LocalFiscalRepository(db: db)
        let calendar = Calendar(identifier: .gregorian)
        for key in ["2025-13", "2025-1", "abcd-01", "", "0225-01", "1025-10", "+202", "2025-00", "2025-01\n", "3025-01"] {
            #expect(throws: PeriodClosureError.invalidKey) {
                try repo.executePeriodClosure(terminalId: "T01", type: .monthly, key: key, managerId: UUID(), managerName: "M",
                                              fiscalYearStartMonth: 1, fiscalYearStartDay: 1, calendar: calendar, now: Date())
            }
        }
        #expect(throws: PeriodClosureError.invalidKey) {
            try repo.executePeriodClosure(terminalId: "T01", type: .annual, key: "2025", managerId: UUID(), managerName: "M",
                                          fiscalYearStartMonth: 2, fiscalYearStartDay: 30, calendar: calendar, now: Date())
        }
    }

    @Test func annualKeysOutsideTheDotNetPatternAreRefused() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        let repo = LocalFiscalRepository(db: db)
        for key in ["0225", "+202", "1025", "202", "20255", "3025"] {
            #expect(throws: PeriodClosureError.invalidKey) {
                try repo.executePeriodClosure(terminalId: "T01", type: .annual, key: key, managerId: UUID(), managerName: "M",
                                              fiscalYearStartMonth: 1, fiscalYearStartDay: 1, calendar: Calendar(identifier: .gregorian), now: Date())
            }
        }
        #expect(try repo.periodClosures(terminalId: "T01", type: nil).isEmpty)
    }

    @Test func aValidOldKeyPassesKeyValidation() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        let repo = LocalFiscalRepository(db: db)
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try #require(TimeZone(identifier: "Europe/Paris"))
        // 1999-12 est une clé valide : la période est finie et vide, la clôture s'écrit.
        let record = try repo.executePeriodClosure(terminalId: "T01", type: .monthly, key: "1999-12", managerId: UUID(), managerName: "M",
                                                   fiscalYearStartMonth: 1, fiscalYearStartDay: 1, calendar: calendar, now: Date())
        #expect(record.periodKey == "1999-12")
        #expect(throws: PeriodClosureError.alreadyClosed) {
            try repo.executePeriodClosure(terminalId: "T01", type: .monthly, key: "1999-12", managerId: UUID(), managerName: "M",
                                          fiscalYearStartMonth: 1, fiscalYearStartDay: 1, calendar: calendar, now: Date())
        }
    }

    @Test func aNonGregorianDeviceCalendarGivesTheSameResult() throws {
        let reference = try Self.reference()
        let paris = try #require(TimeZone(identifier: reference.timeZone))
        func replay(_ identifier: Calendar.Identifier) throws -> [String] {
            var calendar = Calendar(identifier: identifier)
            calendar.timeZone = paris
            var signatures: [String] = []
            for c in reference.cases {
                let db = try SQLiteDatabase(path: ":memory:")
                try LocalMigrator.migrate(db)
                for d in c.dailyClosures { try Self.insertDaily(db, d) }
                let repo = LocalFiscalRepository(db: db)
                for step in c.steps where step.error == nil {
                    let record = try repo.executePeriodClosure(
                        terminalId: "T01", type: step.type == "monthly" ? .monthly : .annual, key: step.key, managerId: Self.manager, managerName: "R",
                        fiscalYearStartMonth: c.fiscalYearStartMonth, fiscalYearStartDay: c.fiscalYearStartDay, calendar: calendar,
                        now: try #require(SQLDate.parse(step.nowUtc)))
                    signatures.append(record.signatureHash)
                }
            }
            return signatures
        }
        let gregorian = try replay(.gregorian)
        #expect(!gregorian.isEmpty)
        #expect(try replay(.japanese) == gregorian)
        #expect(try replay(.buddhist) == gregorian)
    }
}
