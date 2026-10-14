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

    @Test func entriesAreReadBackByNameInOrder() {
        let zip = ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        #expect(ZipStoreReader.names(in: zip) == ["receipts.json", "journal.json", "vide.json"])
        for entry in Self.entries {
            #expect(ZipStoreReader.entry(named: entry.name, in: zip) == entry.data)
        }
        #expect(ZipStoreReader.entry(named: "absent.json", in: zip) == nil)
    }

    @Test func theLayoutIsAStoredZipWithAUtcDosTimestamp() {
        let zip = [UInt8](ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date))
        #expect(Array(zip.prefix(4)) == [0x50, 0x4B, 0x03, 0x04])
        #expect(zip[8] == 0 && zip[9] == 0) // méthode 0 : stocké
        #expect(zip[10] == 0xD5 && zip[11] == 0x4B) // 09:30:42 → (9 << 11) | (30 << 5) | 21 = 0x4BD5
        #expect(zip[12] == 0x4E && zip[13] == 0x5D) // 2026-10-14 → (46 << 9) | (10 << 5) | 14 = 0x5D4E
        #expect(Array(zip.suffix(22).prefix(4)) == [0x50, 0x4B, 0x05, 0x06])
    }

    @Test func garbageAndTruncatedDataAreNotReadAndDoNotCrash() {
        #expect(ZipStoreReader.names(in: Data()) == nil)
        #expect(ZipStoreReader.entry(named: "manifest.json", in: Data("ceci n'est pas un zip, mais un texte assez long".utf8)) == nil)
        var truncated = ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        truncated.removeLast(10)
        #expect(ZipStoreReader.entry(named: "receipts.json", in: truncated) == nil)
        var headless = ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        headless.removeFirst(40) // décalages du répertoire central désormais hors limites
        #expect(ZipStoreReader.entry(named: "journal.json", in: headless) == nil)
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
