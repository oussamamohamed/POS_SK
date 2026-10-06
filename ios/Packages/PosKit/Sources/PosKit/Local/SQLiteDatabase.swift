import Foundation
import SQLite3

/// Valeur liée à un paramètre SQL ou lue dans une colonne.
enum SQLValue: Equatable, Sendable {
    case null
    case int(Int64)
    case double(Double)
    case text(String)
    case blob(Data)

    static func integer(_ v: Int?) -> SQLValue { v.map { .int(Int64($0)) } ?? .null }
    static func bool(_ v: Bool) -> SQLValue { .int(v ? 1 : 0) }
    static func string(_ v: String?) -> SQLValue { v.map { .text($0) } ?? .null }
    static func real(_ v: Double?) -> SQLValue { v.map { .double($0) } ?? .null }
    static func uuid(_ v: UUID?) -> SQLValue { v.map { .text($0.uuidString) } ?? .null }
    static func decimal(_ v: Decimal?) -> SQLValue { v.map { .text("\($0)") } ?? .null }
    static func date(_ v: Date?) -> SQLValue { v.map { .text(SQLDate.format($0)) } ?? .null }
}

/// Dates stockées en texte ISO 8601 UTC (`2026-10-06T19:00:00.123Z`) : l'ordre alphabétique est l'ordre chronologique.
enum SQLDate {
    private static let withFraction = Date.ISO8601FormatStyle(includingFractionalSeconds: true)
    private static let plain = Date.ISO8601FormatStyle()

    static func format(_ date: Date) -> String { date.formatted(withFraction) }

    static func parse(_ text: String) -> Date? {
        (try? withFraction.parse(text)) ?? (try? plain.parse(text))
    }
}

/// Une ligne de résultat. Les accesseurs renvoient `nil` pour NULL ou un type inattendu.
struct SQLRow {
    let values: [String: SQLValue]

    subscript(_ column: String) -> SQLValue { values[column] ?? .null }

    func string(_ c: String) -> String? { if case .text(let s) = self[c] { s } else { nil } }
    func int(_ c: String) -> Int? { if case .int(let v) = self[c] { Int(v) } else { nil } }
    func double(_ c: String) -> Double? {
        switch self[c] {
        case .double(let d): d
        case .int(let i): Double(i)
        default: nil
        }
    }
    func bool(_ c: String) -> Bool { (int(c) ?? 0) != 0 }
    func uuid(_ c: String) -> UUID? { string(c).flatMap { UUID(uuidString: $0) } }
    func decimal(_ c: String) -> Decimal? { string(c).flatMap { Decimal(string: $0, locale: nil) } }
    func date(_ c: String) -> Date? { string(c).flatMap(SQLDate.parse) }
}

struct SQLiteError: Error, CustomStringConvertible {
    let code: Int32
    let message: String
    var description: String { "SQLite \(code) : \(message)" }
}

/// Pour que les stores affichent le message SQLite dans un toast plutôt qu'un texte générique.
extension SQLiteError: LocalizedError {
    var errorDescription: String? { message }
}

private let sqliteTransient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

/// Mince enveloppe autour de `sqlite3`. Non thread-safe : chaque instance appartient à un seul acteur (`LocalPosAPI`).
final class SQLiteDatabase {
    private var handle: OpaquePointer?

    /// `path` : chemin d'un fichier, ou `":memory:"`.
    init(path: String) throws {
        var db: OpaquePointer?
        let rc = sqlite3_open_v2(path, &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nil)
        guard rc == SQLITE_OK, let db else {
            let message = db.map { String(cString: sqlite3_errmsg($0)) } ?? "ouverture impossible"
            sqlite3_close(db)
            throw SQLiteError(code: rc, message: message)
        }
        handle = db
        try exec("PRAGMA foreign_keys = ON")
        try exec("PRAGMA journal_mode = WAL")
    }

    deinit { sqlite3_close(handle) }

    /// Exécute un ou plusieurs ordres sans paramètres (scripts de schéma, PRAGMA).
    func exec(_ sql: String) throws {
        var message: UnsafeMutablePointer<CChar>?
        guard sqlite3_exec(handle, sql, nil, nil, &message) == SQLITE_OK else {
            let text = message.map { String(cString: $0) } ?? "échec"
            sqlite3_free(message)
            throw SQLiteError(code: sqlite3_errcode(handle), message: text)
        }
    }

    /// Exécute un ordre d'écriture ; renvoie le nombre de lignes modifiées.
    @discardableResult
    func run(_ sql: String, _ params: [SQLValue] = []) throws -> Int {
        let stmt = try prepare(sql, params)
        defer { sqlite3_finalize(stmt) }
        let rc = sqlite3_step(stmt)
        guard rc == SQLITE_DONE || rc == SQLITE_ROW else { throw lastError() }
        return Int(sqlite3_changes(handle))
    }

    func query(_ sql: String, _ params: [SQLValue] = []) throws -> [SQLRow] {
        let stmt = try prepare(sql, params)
        defer { sqlite3_finalize(stmt) }
        var rows: [SQLRow] = []
        while true {
            let rc = sqlite3_step(stmt)
            if rc == SQLITE_DONE { break }
            guard rc == SQLITE_ROW else { throw lastError() }
            var values: [String: SQLValue] = [:]
            for i in 0..<sqlite3_column_count(stmt) {
                let name = String(cString: sqlite3_column_name(stmt, i))
                switch sqlite3_column_type(stmt, i) {
                case SQLITE_INTEGER: values[name] = .int(sqlite3_column_int64(stmt, i))
                case SQLITE_FLOAT: values[name] = .double(sqlite3_column_double(stmt, i))
                case SQLITE_TEXT: values[name] = .text(sqlite3_column_text(stmt, i).map { String(cString: $0) } ?? "")
                case SQLITE_BLOB:
                    let count = Int(sqlite3_column_bytes(stmt, i))
                    values[name] = .blob(count == 0 ? Data() : Data(bytes: sqlite3_column_blob(stmt, i), count: count))
                default: values[name] = .null
                }
            }
            rows.append(SQLRow(values: values))
        }
        return rows
    }

    /// Transaction atomique : COMMIT si `body` réussit, ROLLBACK sinon. Pas d'imbrication.
    @discardableResult
    func transaction<T>(_ body: () throws -> T) throws -> T {
        try exec("BEGIN IMMEDIATE")
        do {
            let result = try body()
            try exec("COMMIT")
            return result
        } catch {
            try? exec("ROLLBACK")
            throw error
        }
    }

    func userVersion() throws -> Int { try query("PRAGMA user_version").first?.int("user_version") ?? 0 }

    private func prepare(_ sql: String, _ params: [SQLValue]) throws -> OpaquePointer {
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &stmt, nil) == SQLITE_OK, let stmt else { throw lastError() }
        for (offset, param) in params.enumerated() {
            let index = Int32(offset + 1)
            let rc: Int32
            switch param {
            case .null: rc = sqlite3_bind_null(stmt, index)
            case .int(let v): rc = sqlite3_bind_int64(stmt, index, v)
            case .double(let v): rc = sqlite3_bind_double(stmt, index, v)
            case .text(let v): rc = sqlite3_bind_text(stmt, index, v, -1, sqliteTransient)
            case .blob(let v): rc = v.withUnsafeBytes { sqlite3_bind_blob(stmt, index, $0.baseAddress, Int32(v.count), sqliteTransient) }
            }
            guard rc == SQLITE_OK else {
                sqlite3_finalize(stmt)
                throw lastError()
            }
        }
        return stmt
    }

    private func lastError() -> SQLiteError {
        SQLiteError(code: sqlite3_errcode(handle), message: String(cString: sqlite3_errmsg(handle)))
    }
}
