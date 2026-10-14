import Foundation

/// JSON indenté comme `JsonSerializer` avec `WriteIndented = true` et l'encodeur par défaut : deux espaces par niveau, `\n`,
/// `"clé": valeur`, `[]` et `{}` vides sur une ligne, pas de saut de ligne final. Les scalaires sont déjà du JSON (`FiscalJSON`).
enum IndentedJSON {
    case scalar(String)
    case object([(String, IndentedJSON)])
    case array([IndentedJSON])

    var text: String { render(level: 0) }

    private func render(level: Int) -> String {
        let inner = String(repeating: "  ", count: level + 1)
        let outer = String(repeating: "  ", count: level)
        switch self {
        case .scalar(let json):
            return json
        case .object(let fields):
            guard !fields.isEmpty else { return "{}" }
            let lines = fields.map { inner + FiscalJSON.string($0.0) + ": " + $0.1.render(level: level + 1) }
            return "{\n" + lines.joined(separator: ",\n") + "\n" + outer + "}"
        case .array(let items):
            guard !items.isEmpty else { return "[]" }
            let lines = items.map { inner + $0.render(level: level + 1) }
            return "[\n" + lines.joined(separator: ",\n") + "\n" + outer + "]"
        }
    }

    static func string(_ value: String?) -> IndentedJSON { .scalar(value.map(FiscalJSON.string) ?? "null") }
    static func guid(_ value: UUID?) -> IndentedJSON { .scalar(value.map(FiscalJSON.guid) ?? "null") }
    static func int(_ value: Int?) -> IndentedJSON { .scalar(value.map { "\($0)" } ?? "null") }
    static func bool(_ value: Bool) -> IndentedJSON { .scalar(value ? "true" : "false") }
    static func date(_ value: Date?) -> IndentedJSON { .scalar(value.map(FiscalJSON.dateTime) ?? "null") }

    /// `Money` du .NET (`readonly record struct`) : `{ "AmountInCents": …, "Currency": "EUR" }`.
    static func money(_ cents: Int?) -> IndentedJSON {
        .object([("AmountInCents", .int(cents ?? 0)), ("Currency", .string("EUR"))])
    }
}
