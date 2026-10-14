import Foundation
import Testing
@testable import PosKit

@Suite("ZIP stocké (sans compression)")
struct ZipStoreTests {
    private static let entries: [(name: String, data: Data)] = [
        ("receipts.json", Data("[]".utf8)),
        ("journal.json", Data("[\n  {\n    \"A\": \"\\u00E9\"\n  }\n]".utf8)),
        ("vide.json", Data()),
    ]
    private static let date = SQLDate.parse("2026-10-14T09:30:42Z")!

    @Test func crc32MatchesTheStandardCheckValue() {
        #expect(CRC32.checksum(Data("123456789".utf8)) == 0xCBF4_3926)
        #expect(CRC32.checksum(Data()) == 0)
    }

    @Test func entriesAreReadBackByNameInOrder() throws {
        let zip = try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        #expect(ZipStoreReader.names(in: zip) == ["receipts.json", "journal.json", "vide.json"])
        for entry in Self.entries {
            #expect(ZipStoreReader.entry(named: entry.name, in: zip) == entry.data)
        }
        #expect(ZipStoreReader.entry(named: "absent.json", in: zip) == nil)
    }

    @Test func theLayoutIsAStoredZipWithAUtcDosTimestamp() throws {
        let zip = [UInt8](try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date))
        #expect(Array(zip.prefix(4)) == [0x50, 0x4B, 0x03, 0x04])
        #expect(zip[8] == 0 && zip[9] == 0) // méthode 0 : stocké
        #expect(zip[10] == 0xD5 && zip[11] == 0x4B) // 09:30:42 → (9 << 11) | (30 << 5) | 21 = 0x4BD5
        #expect(zip[12] == 0x4E && zip[13] == 0x5D) // 2026-10-14 → (46 << 9) | (10 << 5) | 14 = 0x5D4E
        #expect(Array(zip.suffix(22).prefix(4)) == [0x50, 0x4B, 0x05, 0x06])
    }

    @Test func garbageAndTruncatedDataAreNotReadAndDoNotCrash() throws {
        #expect(ZipStoreReader.names(in: Data()) == nil)
        #expect(ZipStoreReader.entry(named: "manifest.json", in: Data("ceci n'est pas un zip, mais un texte assez long".utf8)) == nil)
        var truncated = try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        truncated.removeLast(10)
        #expect(ZipStoreReader.entry(named: "receipts.json", in: truncated) == nil)
        var headless = try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        headless.removeFirst(40) // décalages du répertoire central désormais hors limites
        #expect(ZipStoreReader.entry(named: "journal.json", in: headless) == nil)
    }

    @Test func nonAsciiNamesSetTheUtf8FlagInBothHeadersAndRoundTrip() throws {
        let zip = try ZipStoreWriter.archive([("éà.json", Data("x".utf8)), ("a.json", Data())], modifiedAt: Self.date)
        let b = [UInt8](zip)
        #expect(b[6] == 0x00 && b[7] == 0x08) // en-tête local, premier fichier
        let centralOffset = Int(b[b.count - 6]) | Int(b[b.count - 5]) << 8 | Int(b[b.count - 4]) << 16
        #expect(b[centralOffset + 8] == 0x00 && b[centralOffset + 9] == 0x08)
        #expect(ZipStoreReader.names(in: zip) == ["éà.json", "a.json"])
        #expect(ZipStoreReader.entry(named: "éà.json", in: zip) == Data("x".utf8))
        let ascii = [UInt8](try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date))
        #expect(ascii[6] == 0 && ascii[7] == 0)
    }

    @Test func writerRefusesNamesAndEntryCountsBeyondTheFormatLimits() {
        let longName = String(repeating: "a", count: 65_536)
        #expect(throws: ZipStoreError.nameTooLong(longName)) { try ZipStoreWriter.archive([(longName, Data())], modifiedAt: Self.date) }
        let many = [(name: String, data: Data)](repeating: ("a", Data()), count: 65_536)
        #expect(throws: ZipStoreError.tooManyEntries) { try ZipStoreWriter.archive(many, modifiedAt: Self.date) }
        #expect(throws: Never.self) { try ZipStoreWriter.archive([(name: String, data: Data)](repeating: ("a", Data()), count: 65_535), modifiedAt: Self.date) }
    }

    @Test func sameInputGivesIdenticalBytes() throws {
        #expect(try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date) == ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date))
    }

    @Test func corruptedDirectoriesAreRejectedWithoutCrashing() throws {
        let zip = [UInt8](try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date))
        let end = zip.count - 22
        let central = Int(zip[end + 16]) | Int(zip[end + 17]) << 8 | Int(zip[end + 18]) << 16
        func patched(_ edit: (inout [UInt8]) -> Void) -> Data { var b = zip; edit(&b); return Data(b) }
        // taille énorme
        let hugeSize = patched { for i in 0..<4 { $0[central + 20 + i] = 0xFF } }
        #expect(ZipStoreReader.entry(named: "receipts.json", in: hugeSize) == nil)
        // décalage local énorme
        let hugeOffset = patched { for i in 0..<4 { $0[central + 42 + i] = 0xFF } }
        #expect(ZipStoreReader.entry(named: "receipts.json", in: hugeOffset) == nil)
        // méthode ≠ 0
        let deflated = patched { $0[central + 10] = 8 }
        #expect(ZipStoreReader.entry(named: "receipts.json", in: deflated) == nil)
        #expect(ZipStoreReader.names(in: deflated)?.first == "receipts.json")
        // compte de l'enregistrement final supérieur au réel
        let tooMany = patched { $0[end + 10] = 50 }
        #expect(ZipStoreReader.names(in: tooMany) == nil)
        // tronqué à chaque longueur : jamais de plantage
        for length in 0..<zip.count { _ = ZipStoreReader.entry(named: "journal.json", in: Data(zip.prefix(length))) }
    }

    #if os(macOS)
    @Test(.enabled(if: FileManager.default.isExecutableFile(atPath: "/usr/bin/unzip")))
    func anIndependentUnzipAcceptsAndExtractsTheArchive() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("zip-\(UUID().uuidString).zip")
        try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        #expect(try runUnzip(["-t", url.path]).status == 0)
        #expect(try runUnzip(["-p", url.path, "journal.json"]).output == Self.entries[1].data)
    }

    @Test(.enabled(if: FileManager.default.isExecutableFile(atPath: "/usr/bin/unzip")))
    func anIndependentUnzipReadsNonAsciiNames() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("zip-\(UUID().uuidString).zip")
        try ZipStoreWriter.archive([("éà.json", Data("{}".utf8))], modifiedAt: Self.date).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        #expect(try runUnzip(["-t", url.path]).status == 0)
        // motif générique : unzip compare le nom selon la locale du processus, pas fiable pour les accents
        let r = try runUnzip(["-p", url.path, "*.json"])
        #expect(r.output == Data("{}".utf8))
    }
    #endif
}

#if os(macOS)
/// Lance `/usr/bin/unzip`, lecteur ZIP indépendant de PosKit ; renvoie le code de sortie et la sortie standard brute.
func runUnzip(_ arguments: [String]) throws -> (status: Int32, output: Data) {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
    process.arguments = arguments
    let pipe = Pipe()
    process.standardOutput = pipe
    process.standardError = FileHandle.nullDevice
    try process.run()
    let output = pipe.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    return (process.terminationStatus, output)
}
#endif
