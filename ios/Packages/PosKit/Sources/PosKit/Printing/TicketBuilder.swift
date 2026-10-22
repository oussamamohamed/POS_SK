import Foundation

/// Contexte du poste : fuseau du restaurant (`ToLocalTime()` du .NET) et version du logiciel imprimée.
struct TicketContext: Equatable, Sendable {
    var timeZone: TimeZone
    var softwareVersion: String
}

/// Port ligne à ligne de `TicketDocumentBuilder` (.NET) ; la fixture `ticket_document_reference.json` en est la preuve.
enum TicketBuilder {
    // MARK: Tickets de vente

    static func pickupCoupon(order: TicketOrder, pickupNumber: String, buzzer: String?, language: String, now: Date) -> TicketDocument {
        let lang = resolve(language)
        var lines: [TicketLine] = []
        appendPickup(&lines, lang, order, pickupNumber, buzzer, now)
        return document(lang, lines)
    }

    static func receipt(_ receipt: TicketReceipt, order: TicketOrder, language: String, settings: TicketSettings?,
                        duplicateNumber: Int?, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        return document(lang, receiptLines(receipt, order, lang, settings, duplicateNumber, context))
    }

    static func receiptFallback(_ receipt: TicketReceipt, language: String, settings: TicketSettings?,
                                duplicateNumber: Int?, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        var lines = duplicateLines(duplicateNumber)
        lines.append(.separator())
        appendHeader(&lines, settings, context)
        lines.append(.separator())
        lines.append(.columns(label: text(lang, "receipt.ticket"), value: receipt.receiptNumber))
        lines.append(.columns(label: text(lang, "receipt.date"), value: TicketFormat.utcDateTime(receipt.createdAtUtc)))
        lines.append(.columns(label: text(lang, "receipt.terminal"), value: receipt.terminalId))
        lines.append(.separator())
        appendReceiptFooter(&lines, lang, receipt)
        return document(lang, lines)
    }

    static func fiscalReceipt(_ receipt: TicketReceipt, order: TicketOrder, pickupNumber: String, buzzer: String?, language: String,
                              settings: TicketSettings?, duplicateNumber: Int?, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        var lines = receiptLines(receipt, order, lang, settings, duplicateNumber, context)
        lines.append(.separator(cut: true))
        appendPickup(&lines, lang, order, pickupNumber, buzzer, receipt.createdAtUtc)
        return document(lang, lines)
    }

    // MARK: Rapports

    static func xReport(_ summary: TicketReportSummary, data: ReportPrintData, language: String, now: Date,
                        settings: TicketSettings?, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        let zone = context.timeZone
        var lines: [TicketLine] = [
            .text(text(lang, "report.x_title"), align: .center, large: true),
            .separator(),
            .columns(label: text(lang, "receipt.terminal"), value: summary.terminalId),
            .columns(label: text(lang, "report.period_start"), value: localDate(summary.periodStartUtc, zone)),
            .columns(label: text(lang, "report.period_end"), value: localDate(summary.periodEndUtc, zone)),
            .columns(label: text(lang, "report.printed_at"), value: localDate(now, zone)),
        ]
        appendReportTotals(&lines, lang, receiptCount: summary.receiptCount, ttc: summary.totalSalesTtcCents, ht: summary.totalSalesHtCents,
                           vat: summary.vatBreakdownCents.map { ($0.rate, $0.cents) }, payments: summary.paymentTotalsCents.map { ($0.method, $0.cents) },
                           grandTotal: summary.perpetualGrandTotalCents)
        appendReportDetails(&lines, lang, data)
        lines.append(.separator())
        appendSoftwareFooter(&lines, settings, context)
        return document(lang, lines)
    }

    static func zClosure(_ closure: TicketDailyClosure, data: ReportPrintData, language: String, settings: TicketSettings?,
                         duplicateNumber: Int?, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        let zone = context.timeZone
        var lines = duplicateLines(duplicateNumber)
        lines.append(.text(text(lang, "report.z_title", ["sequence": "\(closure.closureSequence)"]), align: .center, large: true))
        lines.append(.separator())
        lines.append(.columns(label: text(lang, "receipt.terminal"), value: closure.terminalId))
        lines.append(.columns(label: text(lang, "report.period_start"), value: localDate(closure.periodStartUtc, zone)))
        lines.append(.columns(label: text(lang, "report.closed_at"), value: localDate(closure.closedAtUtc, zone)))
        lines.append(.columns(label: text(lang, "report.manager"), value: closure.sealedByUserName))
        appendReportTotals(&lines, lang, receiptCount: closure.receiptCount, ttc: closure.totalSalesTtcCents, ht: closure.totalSalesHtCents,
                           vat: closure.vatBreakdownCents.map { ($0.rate, $0.cents) }, payments: closure.paymentTotalsCents.map { ($0.method, $0.cents) },
                           grandTotal: closure.perpetualGrandTotalCents)
        appendReportDetails(&lines, lang, data)
        appendSignatureFooter(&lines, lang, closure.signatureHash, settings, context)
        return document(lang, lines)
    }

    static func periodClosure(_ closure: TicketPeriodClosure, data: ReportPrintData, language: String,
                              settings: TicketSettings?, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        let zone = context.timeZone
        let title = closure.isMonthly
            ? "CLOTURE MENSUELLE \(closure.periodKey) - n\u{00B0}\(closure.closureSequence)"
            : "CLOTURE ANNUELLE \(closure.periodKey) - n\u{00B0}\(closure.closureSequence)"
        var lines: [TicketLine] = [
            .text(title, align: .center, large: true),
            .separator(),
            .columns(label: text(lang, "receipt.terminal"), value: closure.terminalId),
            .columns(label: text(lang, "report.period_start"), value: localDate(closure.periodStartUtc, zone)),
            .columns(label: text(lang, "report.closed_at"), value: localDate(closure.periodEndUtc, zone)),
            .columns(label: text(lang, "report.manager"), value: closure.sealedByUserName),
        ]
        var vat: [(Decimal, Int)] = []
        for (key, cents) in jsonDictionary(closure.taxesSummaryJson) {
            guard let rate = invariantDecimal(key) else { continue }
            if let index = vat.firstIndex(where: { $0.0 == rate }) { vat[index].1 = cents } else { vat.append((rate, cents)) }
        }
        var tenders: [Int: Int] = [:]
        for (key, cents) in jsonDictionary(closure.tenderTotalsJson) {
            if let method = paymentMethodValue(key) { tenders[method] = cents }
        }
        appendReportTotals(&lines, lang, receiptCount: closure.dailyClosureCount, ttc: closure.totalTtcCents, ht: closure.totalHtCents,
                           vat: vat, payments: tenders.map { ($0.key, $0.value) }, grandTotal: closure.perpetualGrandTotalCents)
        appendReportDetails(&lines, lang, data)
        appendSignatureFooter(&lines, lang, closure.signatureHash, settings, context)
        return document(lang, lines)
    }

    // MARK: Cuisine, page de test, duplicata

    static func kitchenTicket(_ ticket: TicketKitchen, order: TicketOrder, language: String, context: TicketContext) -> TicketDocument {
        let lang = resolve(language)
        var lines: [TicketLine] = [
            .text(order.destination == .takeaway
                  ? text(lang, "kitchen_ticket.takeaway")
                  : text(lang, "kitchen_ticket.table", ["table": order.tableNumber]), align: .center, large: true),
        ]
        if let number = order.pickupNumber, !isBlank(number) {
            lines.append(.text(text(lang, "kitchen_ticket.pickup", ["number": number]), align: .center, large: true))
        }
        if let buzzer = order.pickupBuzzer, !isBlank(buzzer) {
            lines.append(.text(text(lang, "kitchen_ticket.buzzer", ["buzzer": buzzer]), align: .center, bold: true))
        }
        lines.append(.columns(label: text(lang, "kitchen_ticket.server"), value: ticket.serverName))
        lines.append(.columns(label: text(lang, "kitchen_ticket.covers"), value: "\(ticket.coversCount)"))
        lines.append(.columns(label: text(lang, "kitchen_ticket.time"), value: TicketFormat.localTime(ticket.dispatchedAtUtc, in: context.timeZone)))
        lines.append(.separator())
        for item in ticket.items {
            lines.append(.text("\(item.quantity)x \(item.productName)", large: true))
            if let modifiers = item.modifiersSummary, !isBlank(modifiers) { lines.append(.text("+ " + modifiers)) }
            if let comment = item.kitchenComment, !isBlank(comment) {
                lines.append(.text(text(lang, "kitchen_ticket.comment", ["comment": comment]), bold: true))
            }
            // Comme le .NET : service retrouvé par produit, premier article de la commande.
            let course = order.items.first { $0.productId == item.productId }?.course ?? .direct
            lines.append(.text(text(lang, courseKey(course))))
            lines.append(.separator())
        }
        return document(lang, lines)
    }

    static func testPage(printer: Printer, language: String, now: Date) -> TicketDocument {
        let lang = resolve(language)
        return document(lang, [
            .text(text(lang, "receipt.test_title"), align: .center, bold: true, large: true),
            .separator(),
            .columns(label: text(lang, "receipt.test_printer"), value: printer.name),
            .columns(label: text(lang, "receipt.test_address"), value: "\(printer.ipAddress):\(printer.port)"),
            .columns(label: text(lang, "receipt.test_paper"), value: "\(printer.paperWidthMm) mm"),
            .columns(label: text(lang, "receipt.date"), value: TicketFormat.utcDateTime(now)),
            .separator(),
            // Échantillon fixe : vérifie accents et liaison arabe quelle que soit la langue.
            .text("àâçéèêëîïôùû ÀÉÈ — مرحبا بكم — 0123456789", align: .center),
        ])
    }

    static func withDuplicateNotice(_ doc: TicketDocument, duplicateNumber: Int) -> TicketDocument {
        var lines = doc.lines.filter {
            guard case let .text(text, _, _, _) = $0 else { return true }
            return text.range(of: "DUPLICATA", options: [.caseInsensitive, .literal]) == nil
        }
        lines.insert(duplicateNotice(duplicateNumber), at: 0)
        return TicketDocument(language: doc.language, rightToLeft: doc.rightToLeft, lines: lines)
    }

    // MARK: Blocs communs

    private static func receiptLines(_ receipt: TicketReceipt, _ order: TicketOrder, _ lang: String, _ settings: TicketSettings?,
                                     _ duplicateNumber: Int?, _ context: TicketContext) -> [TicketLine] {
        var lines = duplicateLines(duplicateNumber)
        lines.append(.separator())
        appendHeader(&lines, settings, context)
        lines.append(.separator())
        lines.append(.columns(label: text(lang, "receipt.ticket"), value: receipt.receiptNumber))
        lines.append(.columns(label: text(lang, "receipt.date"), value: TicketFormat.utcDateTime(receipt.createdAtUtc)))
        lines.append(.columns(label: text(lang, "receipt.terminal"), value: receipt.terminalId))
        lines.append(.columns(label: text(lang, "receipt.mode"), value: mode(lang, order)))
        lines.append(.separator())
        for item in order.items {
            lines.append(.columns(label: "\(item.quantity)x \(item.productName)", value: TicketFormat.amount(item.totalTtcCents)))
        }
        lines.append(.separator())
        appendReceiptFooter(&lines, lang, receipt)
        return lines
    }

    /// Totaux, ventilation TVA et signature : fin commune du ticket et de son repli.
    private static func appendReceiptFooter(_ lines: inout [TicketLine], _ lang: String, _ receipt: TicketReceipt) {
        lines.append(.columns(label: text(lang, "receipt.total_ttc"), value: TicketFormat.amount(receipt.totalTtcCents)))
        lines.append(.columns(label: text(lang, "receipt.total_ht"), value: TicketFormat.amount(receipt.totalHtCents)))
        lines.append(.separator())
        lines.append(.text(text(lang, "receipt.vat_breakdown"), bold: true))
        lines.append(.text(receipt.taxBreakdownJson))
        lines.append(.text(text(lang, "receipt.fiscal_signature"), bold: true))
        lines.append(.text(receipt.signatureHash))
    }

    private static func appendHeader(_ lines: inout [TicketLine], _ settings: TicketSettings?, _ context: TicketContext) {
        lines.append(.text(orDefault(settings?.companyName, "RESTAURANT L'ANTIGRAVITE"), align: .center, bold: true))
        // Découpe sur `\n` par scalaire : en Swift, « \r\n » forme un seul caractère.
        let address = orDefault(settings?.addressLines, "12 Rue de la Gastronomie\n75001 Paris")
        for entry in address.unicodeScalars.split(separator: "\n", omittingEmptySubsequences: false) {
            let line = String(String.UnicodeScalarView(entry)).trimmingCharacters(in: .whitespacesAndNewlines)
            if !line.isEmpty { lines.append(.text(line, align: .center)) }
        }
        lines.append(.text("SIRET: \(orDefault(settings?.siret, "88877766600012"))", align: .center))
        lines.append(.text("TVA: \(orDefault(settings?.vatNumber, "FR12888777666"))", align: .center))
        appendSoftwareFooter(&lines, settings, context)
    }

    /// « Logiciel » puis « Certificat » s'il est renseigné.
    private static func appendSoftwareFooter(_ lines: inout [TicketLine], _ settings: TicketSettings?, _ context: TicketContext) {
        lines.append(.text("Logiciel: RestaurantPOS v\(context.softwareVersion)", align: .center))
        if let certificate = settings?.certificateNumber, !isBlank(certificate) {
            lines.append(.text("Certificat: \(certificate)", align: .center))
        }
    }

    private static func appendSignatureFooter(_ lines: inout [TicketLine], _ lang: String, _ signature: String,
                                              _ settings: TicketSettings?, _ context: TicketContext) {
        lines.append(.separator())
        lines.append(.text(text(lang, "report.signature"), bold: true))
        lines.append(.text(signature))
        lines.append(.separator())
        appendSoftwareFooter(&lines, settings, context)
    }

    private static func appendPickup(_ lines: inout [TicketLine], _ lang: String, _ order: TicketOrder, _ pickupNumber: String,
                                     _ buzzer: String?, _ now: Date) {
        lines.append(.text(text(lang, "receipt.pickup_title"), align: .center, bold: true))
        lines.append(.columns(label: text(lang, "receipt.number"), value: pickupNumber))
        if let buzzer, !isBlank(buzzer) { lines.append(.columns(label: text(lang, "receipt.buzzer"), value: buzzer)) }
        lines.append(.separator())
        lines.append(.columns(label: text(lang, "receipt.date"), value: TicketFormat.utcDateTime(now)))
        lines.append(.columns(label: text(lang, "receipt.mode"), value: mode(lang, order)))
        lines.append(.text(text(lang, "receipt.items_count", ["count": "\(order.items.count)"])))
        lines.append(.separator())
        for item in order.items {
            lines.append(.text("\(item.quantity)x \(item.productName)"))
            if !item.selectedModifiers.isEmpty { lines.append(.text("+ " + item.selectedModifiers.joined(separator: ", "))) }
        }
        lines.append(.separator())
        lines.append(.text(text(lang, "receipt.keep_until_pickup"), align: .center))
    }

    private static func appendReportTotals(_ lines: inout [TicketLine], _ lang: String, receiptCount: Int, ttc: Int, ht: Int,
                                           vat: [(Decimal, Int)], payments: [(Int, Int)], grandTotal: Int) {
        lines.append(.separator())
        lines.append(.columns(label: text(lang, "report.receipt_count"), value: "\(receiptCount)"))
        lines.append(.columns(label: text(lang, "receipt.total_ttc"), value: TicketFormat.amount(ttc)))
        lines.append(.columns(label: text(lang, "receipt.total_ht"), value: TicketFormat.amount(ht)))
        lines.append(.separator())
        lines.append(.text(text(lang, "receipt.vat_breakdown"), bold: true))
        for (rate, cents) in vat.sorted(by: { $0.0 < $1.0 }) {
            lines.append(.columns(label: "\(TicketFormat.rate(rate)) %", value: TicketFormat.amount(cents)))
        }
        lines.append(.separator())
        lines.append(.text(text(lang, "report.payments"), bold: true))
        for (method, cents) in payments.sorted(by: { $0.0 < $1.0 }) {
            lines.append(.columns(label: paymentLabel(lang, method), value: TicketFormat.amount(cents)))
        }
        lines.append(.separator())
        lines.append(.columns(label: text(lang, "report.grand_total"), value: TicketFormat.amount(grandTotal)))
    }

    private static func appendReportDetails(_ lines: inout [TicketLine], _ lang: String, _ data: ReportPrintData) {
        if !data.categories.isEmpty {
            lines.append(.separator())
            lines.append(.text(text(lang, "report.items_sold"), align: .center, bold: true))
            if data.hasGlobalDiscount { lines.append(.text(text(lang, "report.items_before_discount"))) }
            for group in data.categories {
                lines.append(.text(group.categoryName ?? text(lang, "report.other_category"), bold: true))
                for item in group.items {
                    lines.append(.columns(label: "\(item.quantity)x \(item.productName)", value: TicketFormat.amount(item.totalTtcCents)))
                }
                lines.append(.columns(label: text(lang, "report.subtotal"), value: TicketFormat.amount(group.subtotalTtcCents)))
            }
        }
        if data.totalTipsCents > 0 {
            lines.append(.separator())
            lines.append(.text(text(lang, "report.tips_by_server"), align: .center, bold: true))
            for tip in data.tips {
                lines.append(.columns(label: tip.serverName ?? text(lang, "report.unknown_server"), value: TicketFormat.amount(tip.tipCents)))
            }
            lines.append(.columns(label: text(lang, "report.tips_total"), value: TicketFormat.amount(data.totalTipsCents)))
        }
    }

    // MARK: Petits utilitaires

    private static func resolve(_ language: String) -> String {
        TicketTexts.supportedLanguages.contains(language) ? language : "en"
    }

    private static func document(_ lang: String, _ lines: [TicketLine]) -> TicketDocument {
        TicketDocument(language: lang, rightToLeft: lang == "ar", lines: lines)
    }

    private static func text(_ lang: String, _ key: String, _ args: [String: String] = [:]) -> String {
        TicketTexts.get(lang, key, args)
    }

    private static func duplicateNotice(_ number: Int) -> TicketLine {
        .text("DUPLICATA n\u{00B0}\(number)", align: .center, bold: true, large: true)
    }

    private static func duplicateLines(_ number: Int?) -> [TicketLine] {
        number.map { [duplicateNotice($0), .separator()] } ?? []
    }

    private static func mode(_ lang: String, _ order: TicketOrder) -> String {
        text(lang, order.destination == .takeaway ? "receipt.mode_takeaway" : "receipt.mode_eat_in")
    }

    private static func courseKey(_ course: CourseType) -> String {
        switch course {
        case .suite: "kitchen_ticket.course_suite"
        case .dessert: "kitchen_ticket.course_dessert"
        case .onDemand: "kitchen_ticket.course_on_demand"
        case .direct: "kitchen_ticket.course_direct"
        }
    }

    /// Libellé par valeur d'énumération ; une valeur hors énumération (clé « 7 ») tombe sur « autre ».
    private static func paymentLabel(_ lang: String, _ method: Int) -> String {
        let key = switch PaymentMethod(rawValue: method) {
        case .cash: "admin.payment_method_cash"
        case .creditCard: "admin.payment_method_credit_card"
        case .mealVoucher: "admin.payment_method_meal_voucher"
        case .giftCard: "admin.payment_method_gift_card"
        case .roomCharge: "admin.payment_method_room_charge"
        case nil: "admin.payment_method_other"
        }
        return text(lang, key)
    }

    /// `DateTimeOffset.MinValue` du .NET (premier rapport sans clôture précédente) = `nil` = « - ».
    private static func localDate(_ date: Date?, _ zone: TimeZone) -> String {
        date.map { TicketFormat.localDateTime($0, in: zone) } ?? "-"
    }

    private static func isBlank(_ value: String) -> Bool { value.allSatisfy(\.isWhitespace) }

    private static func orDefault(_ value: String?, _ fallback: String) -> String {
        guard let value, !isBlank(value) else { return fallback }
        return value
    }

    /// `JsonSerializer.Deserialize<Dictionary<string, long>>` dans un `try/catch` : JSON vide, invalide,
    /// `null` ou valeur non entière = dictionnaire vide.
    private static func jsonDictionary(_ json: String) -> [String: Int] {
        guard !isBlank(json) else { return [:] }
        return (try? JSONDecoder().decode([String: Int]?.self, from: Data(json.utf8)) ?? [:]) ?? [:]
    }

    /// `decimal.TryParse(key, NumberStyles.Any, InvariantCulture)` pour les clés écrites par le .NET (« 5.5 », « 10 »).
    // ponytail: séparateurs de milliers, parenthèses et symbole monétaire de NumberStyles.Any non gérés ; les clés viennent de decimal.ToString().
    private static func invariantDecimal(_ key: String) -> Decimal? {
        let scanner = Scanner(string: key.trimmingCharacters(in: .whitespacesAndNewlines))
        scanner.locale = Locale(identifier: "en_US_POSIX")
        guard let value = scanner.scanDecimal(), scanner.isAtEnd else { return nil }
        return value
    }

    /// `Enum.TryParse<PaymentMethod>(key)` : nom sensible à la casse, liste de noms séparés par des virgules (OU binaire)
    /// ou entier en chaîne, même hors énumération (la clé « 7 » donne la valeur 7). Espaces autour ignorés.
    private static func paymentMethodValue(_ key: String) -> Int? {
        let trimmed = key.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let first = trimmed.first else { return nil }
        if first.isASCII, first.isNumber || first == "-" || first == "+" { return Int32(trimmed).map(Int.init) }
        var value = 0
        for name in trimmed.split(separator: ",", omittingEmptySubsequences: false) {
            guard let method = paymentMethodNames[name.trimmingCharacters(in: .whitespaces)] else { return nil }
            value |= method
        }
        return value
    }

    private static let paymentMethodNames = ["Cash": 0, "CreditCard": 1, "MealVoucher": 2, "GiftCard": 3, "RoomCharge": 4]
}
