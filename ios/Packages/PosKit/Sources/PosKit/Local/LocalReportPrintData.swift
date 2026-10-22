import Foundation

/// Tri de `StringComparer.Create(CultureInfo("fr"), ignoreCase: true)` du .NET (ICU du système), indépendant de la locale de l'appareil.
/// Foundation (`.caseInsensitive`, locale « fr » figée) donne le même ordre, sauf trois règles propres au .NET, prouvées par
/// `report_print_data_reference.json` (sections `collation` et `collationPairs`) :
/// - les lettres pleine chasse (U+FF01…U+FF5E) se trient juste après leur lettre ASCII (« Ｃ » après « Cz », avant « D ») ;
/// - ß / ss et ſ / s restent distincts (ß après ss), alors que Foundation les confond ;
/// - les autres variantes de compatibilité (ﬁ / fi, µ / μ, ², ª, №…) et la casse restent égales.
/// ponytail: modèle limité à ces écarts mesurés (latin, chiffres, ponctuation) ; une autre écriture exigerait d'étendre la fixture.
enum FrenchCollation {
    private static let locale = Locale(identifier: "fr")

    static func compare(_ a: String, _ b: String) -> ComparisonResult {
        let (x, y) = (widened(a), widened(b))
        let primary = x.compare(y, options: .caseInsensitive, range: nil, locale: locale)
        guard primary == .orderedSame, variantKey(x) != variantKey(y) else { return primary }
        return x.lowercased().compare(y.lowercased(), options: [], range: nil, locale: locale)
    }

    /// Tri stable, comme `OrderBy` : les égalités gardent l'ordre d'entrée.
    static func sorted<T>(_ items: [T], by key: (T) -> String) -> [T] {
        items.enumerated().sorted { l, r in
            switch compare(key(l.element), key(r.element)) {
            case .orderedAscending: true
            case .orderedDescending: false
            case .orderedSame: l.offset < r.offset
            }
        }.map(\.element)
    }

    /// Lettre pleine chasse → lettre ASCII suivie d'un caractère privé, trié après toute lettre.
    private static func widened(_ s: String) -> String {
        guard s.unicodeScalars.contains(where: { (0xFF01...0xFF5E).contains($0.value) }) else { return s }
        var out = String.UnicodeScalarView()
        for scalar in s.unicodeScalars {
            if (0xFF01...0xFF5E).contains(scalar.value), let ascii = Unicode.Scalar(scalar.value - 0xFEE0) {
                out.append(ascii)
                out.append("\u{F8FF}")
            } else {
                out.append(scalar)
            }
        }
        return String(out)
    }

    /// Clé d'égalité du .NET : casse et compatibilité (NFKC) ignorées, sauf ß et ſ.
    private static func variantKey(_ s: String) -> String {
        var out = String.UnicodeScalarView()
        for scalar in s.lowercased().unicodeScalars {
            switch scalar {
            case "ß": out.append("\u{E000}")
            case "ſ": out.append("\u{E001}")
            default: out.append(scalar)
            }
        }
        return String(out).precomposedStringWithCompatibilityMapping.lowercased()
    }
}

extension LocalFiscalRepository {
    /// Port de `ReportPrintDataService.BuildAsync` : reçus du poste non annulés, hors annulations, TTC > 0, dans [début ; fin]
    /// (`nil` = sans borne basse) ; commandes non annulées. Informatif : n'entre ni dans les totaux fiscaux ni dans la signature Z.
    /// Sur l'iPad le poste est toujours `T01` (le « terminal principal » du .NET, qui couvre tous les postes, n'existe pas).
    func reportPrintData(terminalId: String, periodStart: Date?, periodEnd: Date) throws -> ReportPrintData {
        let receipts = try db.query(
            "SELECT Id, OrderId, TotalTtcAmount, CreatedAtUtc, VoidedReceiptId FROM FiscalReceipts WHERE TerminalId = ? ORDER BY CreatedAtUtc, SequenceNumber",
            [.text(terminalId)]
        )
        let voided = Set(receipts.compactMap { $0.string("VoidedReceiptId") })
        let start = periodStart.map(FiscalHashing.storedText)
        let end = FiscalHashing.storedText(periodEnd)
        var orderIds: [String] = []
        for r in receipts where r.string("VoidedReceiptId") == nil && !voided.contains(r.string("Id") ?? "") && (r.int("TotalTtcAmount") ?? 0) > 0 {
            let at = r.string("CreatedAtUtc") ?? ""
            guard at <= end, start.map({ at >= $0 }) ?? true, let id = r.string("OrderId"), !orderIds.contains(id) else { continue }
            orderIds.append(id)
        }

        let orders = LocalOrderRepository(db: db)
        let rows = try orderIds.compactMap { try db.query("SELECT * FROM Orders WHERE Id = ?", [.text($0)]).first }
            .filter { $0.int("Status") != LocalOrderStatus.cancelled.rawValue }
            .sorted { ($0.string("CreatedAtUtc") ?? "") < ($1.string("CreatedAtUtc") ?? "") }

        let productCategory = Dictionary(try db.query("SELECT Id, CategoryId FROM Products").compactMap { row in
            row.string("Id").map { ($0.uppercased(), row.string("CategoryId") ?? "") }
        }, uniquingKeysWith: { first, _ in first })
        let categoryName = Dictionary(try db.query("SELECT Id, Name FROM Categories").compactMap { row in
            row.string("Id").map { ($0, row.string("Name") ?? "") }
        }, uniquingKeysWith: { first, _ in first })
        let userName = Dictionary(try db.query("SELECT Id, Name FROM Users").compactMap { row in
            row.string("Id").map { ($0.uppercased(), row.string("Name") ?? "") }
        }, uniquingKeysWith: { first, _ in first })

        // Groupes dans l'ordre de première apparition (GroupBy), puis tri stable.
        var groups: [(name: String?, lines: [(name: String, quantity: Int, ttc: Int)])] = []
        for row in rows {
            guard let id = row.uuid("Id") else { continue }
            for line in try orders.lines(orderId: id) {
                let family = productCategory[line.productId.uuidString].flatMap { categoryName[$0] }
                let ttc = OrderMath.lineTotal(CartLine(serverLine: line)).cents
                if let g = groups.firstIndex(where: { $0.name == family }) {
                    groups[g].lines.append((line.productName, line.quantity, ttc))
                } else {
                    groups.append((family, [(line.productName, line.quantity, ttc)]))
                }
            }
        }
        let categories = groups.map { group -> ReportCategoryGroup in
            var items: [ReportItemLine] = []
            for line in group.lines {
                if let i = items.firstIndex(where: { $0.productName == line.name }) {
                    items[i].quantity += line.quantity
                    items[i].totalTtcCents += line.ttc
                } else {
                    items.append(ReportItemLine(productName: line.name, quantity: line.quantity, totalTtcCents: line.ttc))
                }
            }
            return ReportCategoryGroup(
                categoryName: group.name, items: FrenchCollation.sorted(items, by: \.productName),
                subtotalTtcCents: group.lines.reduce(0) { $0 + $1.ttc }
            )
        }

        var tipsByOperator: [(operatorId: String, cents: Int)] = []
        for row in rows where (row.int("TipAmount") ?? 0) > 0 {
            let op = (row.string("OperatorId") ?? "").uppercased()
            if let i = tipsByOperator.firstIndex(where: { $0.operatorId == op }) {
                tipsByOperator[i].cents += row.int("TipAmount") ?? 0
            } else {
                tipsByOperator.append((op, row.int("TipAmount") ?? 0))
            }
        }
        let tips = tipsByOperator.map { ReportTipLine(serverName: userName[$0.operatorId], tipCents: $0.cents) }

        return ReportPrintData(
            categories: Self.knownFirst(categories, name: \.categoryName),
            tips: Self.knownFirst(tips, name: \.serverName),
            totalTipsCents: tips.reduce(0) { $0 + $1.tipCents },
            hasGlobalDiscount: rows.contains { $0.int("GlobalDiscountType") != nil }
        )
    }

    /// `OrderBy(x => x.Name is null).ThenBy(x => x.Name, French)` : noms connus triés, puis les `nil` dans leur ordre d'apparition.
    private static func knownFirst<T>(_ items: [T], name: (T) -> String?) -> [T] {
        FrenchCollation.sorted(items.filter { name($0) != nil }, by: { name($0)! }) + items.filter { name($0) == nil }
    }
}
