import Foundation

/// Réservations et analyse du menu du mode autonome : mêmes règles que `ReservationService` et `MenuAnalysisService` (.NET),
/// via `ReservationRules` et `MenuAnalysisCalculator`.
extension LocalPosAPI {
    var reservationRepository: LocalReservationRepository { LocalReservationRepository(db: db) }

    private func visible(_ r: Reservation, isManager: Bool) -> Reservation {
        var copy = r
        if !isManager { copy.phone = nil }
        copy.isLate = ReservationRules.isLate(status: r.status, isWaitlist: r.isWaitlist, startsAt: r.startsAtUtc, now: clock())
        return copy
    }

    private func existing(_ id: UUID) throws -> Reservation {
        guard let r = try reservationRepository.reservation(id: id) else { throw ReservationError.notFound.apiError }
        return r
    }

    private func checkTable(_ d: ReservationDraft, ignoring id: UUID?) throws {
        guard let number = d.tableNumber else { return }
        guard let table = try floorRepository.tables().first(where: { $0.tableNumber == number }) else { throw ReservationError.unknownTable.apiError }
        guard d.covers <= table.capacity else { throw ReservationError.tableCapacity.apiError }
        let clash = try reservationRepository.all().contains {
            $0.tableNumber == number && !$0.isWaitlist && $0.id != id && ($0.status == .booked || $0.status == .seated)
                && ReservationRules.overlaps(start: d.startsAt, minutes: d.durationMinutes, otherStart: $0.startsAtUtc, otherMinutes: $0.durationMinutes)
        }
        if clash { throw ReservationError.tableConflict.apiError }
    }

    private func validated(_ draft: ReservationDraft, ignoring id: UUID?) throws -> ReservationDraft {
        do {
            let d = try ReservationRules.validate(draft)
            try checkTable(d, ignoring: id)
            return d
        } catch let error as ReservationError {
            throw error.apiError
        }
    }

    public func reservations(from: Date, to: Date) async throws -> [Reservation] {
        let member = try requireAuth()
        try reservationRepository.purge(now: clock())
        return try reservationRepository.all().filter { $0.startsAtUtc >= from && $0.startsAtUtc <= to }.map { visible($0, isManager: member.role.isManager) }
    }

    public func createReservation(_ draft: ReservationDraft) async throws -> Reservation {
        let member = try requireAuth()
        let d = try validated(draft, ignoring: nil)
        let r = Reservation(guestName: d.guestName, covers: d.covers, startsAtUtc: d.startsAt, durationMinutes: d.durationMinutes, tableNumber: d.tableNumber, note: d.note, phone: d.phone, isWaitlist: d.isWaitlist, quoteMinutes: d.quoteMinutes)
        try reservationRepository.insert(r, now: clock())
        return visible(r, isManager: member.role.isManager)
    }

    public func updateReservation(id: UUID, _ draft: ReservationDraft) async throws -> Reservation {
        let member = try requireAuth()
        guard try existing(id).status == .booked else { throw ReservationError.invalidState.apiError }
        let d = try validated(draft, ignoring: id)
        try reservationRepository.update(id: id, d, keepPhone: !member.role.isManager, now: clock())
        return visible(try existing(id), isManager: member.role.isManager)
    }

    public func seatReservation(id: UUID, tableNumber: String?, operatorId: UUID?, waiterName: String?) async throws -> Reservation {
        let member = try requireAuth()
        let r = try existing(id)
        guard r.status == .booked else { throw ReservationError.invalidState.apiError }
        let requested = tableNumber?.trimmingCharacters(in: .whitespaces)
        guard let target = (requested?.isEmpty == false ? requested : r.tableNumber) else { throw ReservationError.tableRequired.apiError }
        guard let table = try floorRepository.tables().first(where: { $0.tableNumber == target }) else { throw ReservationError.unknownTable.apiError }
        guard r.covers <= table.capacity else { throw ReservationError.tableCapacity.apiError }
        guard table.status == .free, table.activeOrderId == nil else { throw ReservationError.tableOccupied.apiError }
        try await openTable(number: target, covers: r.covers, operatorId: operatorId, waiterName: waiterName)
        try reservationRepository.setStatus(id: id, .seated, tableNumber: target, now: clock())
        return visible(try existing(id), isManager: member.role.isManager)
    }

    public func setReservationStatus(id: UUID, status: ReservationStatus) async throws -> Reservation {
        let member = try requireAuth()
        guard ReservationRules.transitionAllowed(from: try existing(id).status, to: status) else { throw ReservationError.invalidState.apiError }
        try reservationRepository.setStatus(id: id, status, now: clock())
        return visible(try existing(id), isManager: member.role.isManager)
    }

    // MARK: Analyse du menu

    public func menuAnalysis(from: Date, to: Date) async throws -> MenuAnalysisReport {
        try requireManager()
        let (start, end) = from <= to ? (from, to) : (to, from)
        let voidedReceiptIds = Set(try db.query("SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL").compactMap { $0.uuid("VoidedReceiptId") })
        let voidedOrderIds = Set(try db.query("SELECT Id, OrderId FROM FiscalReceipts").compactMap { row -> UUID? in
            guard let id = row.uuid("Id"), voidedReceiptIds.contains(id) else { return nil }
            return row.uuid("OrderId")
        })

        var sold: [UUID: (name: String, quantity: Int, revenueHtCents: Decimal)] = [:]
        for row in try db.query("SELECT Id, CreatedAtUtc FROM Orders WHERE Status = ?", [.integer(LocalOrderStatus.paid.rawValue)]) {
            guard let orderId = row.uuid("Id"), let created = row.date("CreatedAtUtc"), created >= start, created <= end,
                  !voidedOrderIds.contains(orderId), let order = try orderRepository.order(id: orderId),
                  order.globalDiscountType != .comp else { continue }
            let lines = order.lines.map(CartLine.init(serverLine:))
            let totals = OrderMath.totals(lines: lines, destination: order.destination, discount: order.globalDiscount)
            let scale: Decimal = totals.subtotalTtc.cents > 0 ? Decimal(totals.totalTtc.cents) / Decimal(totals.subtotalTtc.cents) : 0
            for line in lines where !line.isComp && line.quantity > 0 {
                let ttcCents = Decimal(OrderMath.lineTotal(line).cents) * scale
                let htCents = ttcCents / (1 + OrderMath.effectiveTaxRate(line, destination: order.destination) / 100)
                let acc = sold[line.productId]
                sold[line.productId] = (line.name, (acc?.quantity ?? 0) + line.quantity, (acc?.revenueHtCents ?? 0) + htCents)
            }
        }

        let products = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, Name, CategoryId, Cost FROM Products").compactMap { row in
            row.uuid("Id").map { ($0, (name: row.string("Name") ?? "", categoryId: row.string("CategoryId"), cost: row.int("Cost"))) }
        })
        let categories = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, Name FROM Categories").map { ($0.string("Id") ?? "", $0.string("Name") ?? "") })
        let sales = sold.map { id, s in
            MenuSale(productId: id, name: products[id]?.name ?? s.name, categoryName: products[id]?.categoryId.flatMap { categories[$0] },
                     quantity: s.quantity, revenueHtCents: s.revenueHtCents, unitCostCents: products[id]?.cost)
        }
        return MenuAnalysisCalculator.report(from: start, to: end, sales: sales)
    }
}
