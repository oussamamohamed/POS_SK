import Foundation

/// CRC-32 IEEE 802.3 (polynôme réfléchi 0xEDB88320), celui du format ZIP.
enum CRC32 {
    private static let table: [UInt32] = (0..<256).map { index in
        var c = UInt32(index)
        for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    static func checksum(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xFFFF_FFFF
        for byte in data { crc = table[Int((crc ^ UInt32(byte)) & 0xFF)] ^ (crc >> 8) }
        return crc ^ 0xFFFF_FFFF
    }
}

/// Archive ZIP « stockée » (méthode 0, sans compression) : en-têtes locaux, répertoire central, fin de répertoire. Lisible par tout outil.
/// ponytail: ni ZIP64 ni compression ; limites 4 Gio (donnée, archive) et 65 535 entrées / octets de nom, refusées par `ZipStoreError`
/// (une archive fiscale mensuelle pèse quelques Mo). Upgrade : ZIP64 si une archive dépasse ces bornes.
enum ZipStoreError: Error, Equatable {
    case tooLarge(String)
    case tooManyEntries
    case nameTooLong(String)
}

enum ZipStoreWriter {
    static func archive(_ entries: [(name: String, data: Data)], modifiedAt: Date) throws -> Data {
        guard entries.count <= Int(UInt16.max) else { throw ZipStoreError.tooManyEntries }
        var total = 0
        for entry in entries {
            guard entry.name.utf8.count <= Int(UInt16.max) else { throw ZipStoreError.nameTooLong(entry.name) }
            total += entry.data.count + entry.name.utf8.count * 2 + 100
            guard entry.data.count <= Int(UInt32.max), total <= Int(UInt32.max) else { throw ZipStoreError.tooLarge(entry.name) }
        }
        let (time, date) = dosDateTime(modifiedAt)
        var body = ByteWriter(), central = ByteWriter()
        for entry in entries {
            let name = Array(entry.name.utf8)
            let flags: UInt16 = name.contains(where: { $0 >= 0x80 }) ? 0x0800 : 0 // bit 11 : nom en UTF-8
            let size = UInt32(entry.data.count)
            let crc = CRC32.checksum(entry.data)
            let offset = UInt32(body.bytes.count)

            body.u32(0x0403_4B50); body.u16(20); body.u16(flags); body.u16(0); body.u16(time); body.u16(date)
            body.u32(crc); body.u32(size); body.u32(size); body.u16(UInt16(name.count)); body.u16(0)
            body.raw(name); body.raw([UInt8](entry.data))

            central.u32(0x0201_4B50); central.u16(20); central.u16(20); central.u16(flags); central.u16(0); central.u16(time); central.u16(date)
            central.u32(crc); central.u32(size); central.u32(size); central.u16(UInt16(name.count)); central.u16(0); central.u16(0)
            central.u16(0); central.u16(0); central.u32(0); central.u32(offset)
            central.raw(name)
        }
        let centralOffset = UInt32(body.bytes.count)
        body.raw(central.bytes)
        body.u32(0x0605_4B50); body.u16(0); body.u16(0); body.u16(UInt16(entries.count)); body.u16(UInt16(entries.count))
        body.u32(UInt32(central.bytes.count)); body.u32(centralOffset); body.u16(0)
        return Data(body.bytes)
    }

    /// Date et heure MS-DOS, en UTC (secondes paires).
    private static func dosDateTime(_ date: Date) -> (time: UInt16, date: UInt16) {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .gmt
        let c = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        let year = min(max((c.year ?? 1980) - 1980, 0), 127)
        let time = ((c.hour ?? 0) << 11) | ((c.minute ?? 0) << 5) | ((c.second ?? 0) / 2)
        let day = (year << 9) | ((c.month ?? 1) << 5) | (c.day ?? 1)
        return (UInt16(time), UInt16(day))
    }
}

/// Lecture minimale d'un ZIP stocké, pour retrouver `manifest.json` à la vérification. Ne vérifie pas le CRC (le .NET non plus).
/// Toute incohérence (signature absente, décalage hors limites, entrée compressée) donne `nil`, jamais de plantage.
enum ZipStoreReader {
    private struct Entry {
        let name: String
        let method: UInt16
        let size: Int
        let localOffset: Int
    }

    static func names(in zip: Data) -> [String]? {
        centralEntries([UInt8](zip))?.map(\.name)
    }

    static func entry(named name: String, in zip: Data) -> Data? {
        let bytes = [UInt8](zip)
        guard let entry = centralEntries(bytes)?.first(where: { $0.name == name }), entry.method == 0,
              u32(bytes, entry.localOffset) == 0x0403_4B50,
              let nameLength = u16(bytes, entry.localOffset + 26), let extraLength = u16(bytes, entry.localOffset + 28)
        else { return nil }
        let start = entry.localOffset + 30 + Int(nameLength) + Int(extraLength)
        guard start + entry.size <= bytes.count else { return nil }
        return Data(bytes[start..<(start + entry.size)])
    }

    private static func centralEntries(_ bytes: [UInt8]) -> [Entry]? {
        guard bytes.count >= 22 else { return nil }
        let lowest = max(0, bytes.count - 22 - 0xFFFF)
        var end = bytes.count - 22
        while end >= lowest, u32(bytes, end) != 0x0605_4B50 { end -= 1 }
        guard end >= lowest, let count = u16(bytes, end + 10), let directory = u32(bytes, end + 16) else { return nil }
        var offset = Int(directory)
        var entries: [Entry] = []
        for _ in 0..<Int(count) {
            guard u32(bytes, offset) == 0x0201_4B50,
                  let method = u16(bytes, offset + 10), let size = u32(bytes, offset + 20),
                  let nameLength = u16(bytes, offset + 28), let extraLength = u16(bytes, offset + 30), let commentLength = u16(bytes, offset + 32),
                  let local = u32(bytes, offset + 42), offset + 46 + Int(nameLength) <= bytes.count
            else { return nil }
            let name = String(decoding: bytes[(offset + 46)..<(offset + 46 + Int(nameLength))], as: UTF8.self)
            entries.append(Entry(name: name, method: method, size: Int(size), localOffset: Int(local)))
            offset += 46 + Int(nameLength) + Int(extraLength) + Int(commentLength)
        }
        return entries
    }

    private static func u16(_ b: [UInt8], _ at: Int) -> UInt16? {
        guard at >= 0, at + 2 <= b.count else { return nil }
        return UInt16(b[at]) | UInt16(b[at + 1]) << 8
    }

    private static func u32(_ b: [UInt8], _ at: Int) -> UInt32? {
        guard at >= 0, at + 4 <= b.count else { return nil }
        return UInt32(b[at]) | UInt32(b[at + 1]) << 8 | UInt32(b[at + 2]) << 16 | UInt32(b[at + 3]) << 24
    }
}

/// Écriture petit-boutiste, sans longues chaînes de `+` (temps de vérification de types).
private struct ByteWriter {
    var bytes: [UInt8] = []

    mutating func u16(_ v: UInt16) { bytes.append(UInt8(v & 0xFF)); bytes.append(UInt8(v >> 8)) }
    mutating func u32(_ v: UInt32) { for shift in stride(from: 0, to: 32, by: 8) { bytes.append(UInt8((v >> UInt32(shift)) & 0xFF)) } }
    mutating func raw(_ b: [UInt8]) { bytes.append(contentsOf: b) }
}
