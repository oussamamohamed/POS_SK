import Foundation
import Testing
@testable import PosKit

@Suite("Archives : contenu identique au .NET")
struct LocalArchiveContentReferenceTests {
    private static func text(_ data: Data) -> String { String(decoding: data, as: UTF8.self) }

    @Test func eachArchiveOfTheFixtureHasTheSameFiveFiles() throws {
        let reference = try ArchiveReference.load()
        #expect(reference.archives.count == 2)
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try reference.seed(db)
        let repo = LocalFiscalRepository(db: db)

        for expected in reference.archives {
            let context = Comment(rawValue: expected.fileName)
            let closure = try #require(try repo.periodClosure(id: expected.periodClosureId), context)
            let content = try repo.archiveContent(for: closure, archiveId: UUID(), createdAt: Date())
            #expect(Self.text(content.receipts) == expected.files.receipts, context)
            #expect(Self.text(content.closures) == expected.files.closures, context)
            #expect(Self.text(content.periodClosures) == expected.files.periodClosures, context)
            #expect(Self.text(content.journal) == expected.files.journal, context)
            #expect(FiscalHashing.sha256Hex(content.receipts) == expected.sha256.receipts, context)
            #expect(FiscalHashing.sha256Hex(content.closures) == expected.sha256.closures, context)
            #expect(FiscalHashing.sha256Hex(content.periodClosures) == expected.sha256.periodClosures, context)
            #expect(FiscalHashing.sha256Hex(content.journal) == expected.sha256.journal, context)
            #expect(maskedManifest(Self.text(content.manifest)) == expected.files.manifest, context)
            #expect(LocalFiscalRepository.archiveFileName(closure) == expected.fileName, context)
            #expect(content.entries.map(\.name) == ["receipts.json", "closures.json", "period-closures.json", "journal.json", "manifest.json"])
        }
    }

    @Test func theManifestCarriesTheArchiveIdAndCreationDate() throws {
        let reference = try ArchiveReference.load()
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try reference.seed(db)
        let repo = LocalFiscalRepository(db: db)
        let closure = try #require(try repo.periodClosure(id: reference.archives[0].periodClosureId))
        let archiveId = UUID()
        let manifest = Self.text(try repo.archiveContent(for: closure, archiveId: archiveId, createdAt: SQLDate.parse("2026-10-14T09:30:00.250Z")!).manifest)
        #expect(manifest.contains("\"archiveId\": \"\(FiscalHashing.guidText(archiveId))\""))
        #expect(manifest.contains("\"createdAtUtc\": \"2026-10-14T09:30:00.25+00:00\""))
    }

    @Test func indentationFollowsSystemTextJson() {
        let json = IndentedJSON.array([
            .object([("A", .int(1)), ("B", .array([])), ("C", .object([])), ("D", .object([("E", .string("é\""))])), ("F", .guid(nil))]),
        ])
        #expect(json.text == "[\n  {\n    \"A\": 1,\n    \"B\": [],\n    \"C\": {},\n    \"D\": {\n      \"E\": \"\\u00E9\\u0022\"\n    },\n    \"F\": null\n  }\n]")
        #expect(IndentedJSON.array([]).text == "[]")
        #expect(IndentedJSON.money(-250).text == "{\n  \"AmountInCents\": -250,\n  \"Currency\": \"EUR\"\n}")
    }

    @Test func unsafeCharactersOfTheFileNameAreReplaced() {
        let closure = PeriodClosureRecord(
            id: UUID(), terminalId: "T0/1 é", periodType: .annual, periodKey: "2025", sequence: 1, periodStartUtc: Date(), periodEndUtc: Date(),
            totalTtcCents: 0, totalHtCents: 0, taxesSummaryJson: "{}", tenderTotalsJson: "{}", perpetualGrandTotalCents: 0, dailyClosureCount: 0,
            previousSignatureHash: "", signatureHash: "", sealedByUserId: UUID(), sealedByUserName: "", createdAtUtc: Date()
        )
        #expect(LocalFiscalRepository.archiveFileName(closure) == "archive-T0_1__-A-2025.zip")
    }
}
