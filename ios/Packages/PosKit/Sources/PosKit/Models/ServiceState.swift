import Foundation

/// Temps cible d'un bon de cuisine, en minutes : au-delà, il est « en retard ».
/// shortcut: constante (12 min, comme le web) ; à passer en réglage de l'établissement si besoin.
public enum KitchenTarget {
    public static let minutes = 12
}

/// État de service d'une table, dérivé de la table et des bons de cuisine (miroir de `tableServiceState` du client web).
public enum TableServiceState: String, Sendable, CaseIterable {
    case free, ordering, cooking, eating, late, bill, paid

    public static func of(_ table: DiningTable, tickets: [KitchenTicket], now: Date = Date(), targetMinutes: Int = KitchenTarget.minutes) -> TableServiceState {
        switch table.status {
        case .free: return .free
        case .billRequested: return .bill
        case .paid: return .paid
        case .occupied: break
        }
        let open = tickets.filter { $0.tableNumber == table.tableNumber && ($0.status == .pending || $0.status == .inPreparation) }
        if !open.isEmpty {
            let oldest = open.compactMap(\.dispatchedAtUtc).min() ?? now
            return now.timeIntervalSince(oldest) > TimeInterval(targetMinutes * 60) ? .late : .cooking
        }
        if let orderId = table.activeOrderId, tickets.contains(where: { $0.orderId == orderId }) { return .eating }
        return .ordering
    }
}

/// Forme et position d'une table sur le plan de 960 px de large (le serveur ne stocke pas de dessin de salle :
/// forme d'après la capacité, rangées automatiques ; miroir de `floorShape` / `floorLayout` du client web).
public struct FloorBox: Hashable, Sendable {
    public enum Shape: String, Sendable { case round, square, rect, long, bar }
    public var shape: Shape
    public var seats: Int
    public var x: Double, y: Double, width: Double, height: Double
}

public enum FloorLayout {
    public static let width = 960.0

    public static func shape(capacity: Int, number: String) -> (shape: FloorBox.Shape, width: Double, height: Double) {
        if number.lowercased() == "bar" { return (.bar, 270, 40) }
        switch capacity {
        case ...2: return (.round, 76, 76)
        case 3...4: return (.square, 96, 96)
        case 5...6: return (.rect, 170, 92)
        default: return (.long, 230, 90)
        }
    }

    public static func boxes(for tables: [DiningTable]) -> (boxes: [String: FloorBox], height: Double) {
        var out: [String: FloorBox] = [:]
        var x = 70.0, y = 60.0, rowHeight = 0.0
        for table in tables {
            let s = shape(capacity: table.capacity, number: table.tableNumber)
            if x + s.width > 900 { x = 70; y += rowHeight + 70; rowHeight = 0 }
            out[table.tableNumber] = FloorBox(shape: s.shape, seats: table.capacity, x: x, y: y, width: s.width, height: s.height)
            x += s.width + 64
            rowHeight = max(rowHeight, s.height)
        }
        return (out, y + rowHeight + 60)
    }

    /// Emplacements des chaises autour d'une table (mêmes règles que le web).
    public static func chairs(of b: FloorBox) -> [CGRect] {
        let g = 7.0, long = 26.0, short = 11.0
        var out: [CGRect] = []
        func top(_ n: Int) { for i in 0..<n { out.append(CGRect(x: b.x + b.width * (Double(i) + 0.5) / Double(n) - long / 2, y: b.y - g - short, width: long, height: short)) } }
        func bottom(_ n: Int) { for i in 0..<n { out.append(CGRect(x: b.x + b.width * (Double(i) + 0.5) / Double(n) - long / 2, y: b.y + b.height + g, width: long, height: short)) } }
        func left(_ n: Int) { for i in 0..<n { out.append(CGRect(x: b.x - g - short, y: b.y + b.height * (Double(i) + 0.5) / Double(n) - long / 2, width: short, height: long)) } }
        func right(_ n: Int) { for i in 0..<n { out.append(CGRect(x: b.x + b.width + g, y: b.y + b.height * (Double(i) + 0.5) / Double(n) - long / 2, width: short, height: long)) } }
        if b.shape == .bar {
            for i in 0..<b.seats { out.append(CGRect(x: b.x + b.width * (Double(i) + 0.5) / Double(b.seats) - 11, y: b.y - 32, width: 22, height: 22)) }
            return out
        }
        if b.seats <= 2 { left(1); right(1); return out }
        if b.seats <= 4 { top(1); bottom(1); left(1); right(1); return out }
        let side = (b.seats - 2) / 2
        top(side); bottom(b.seats - 2 - side); left(1); right(1)
        return out
    }
}

/// Tuiles de l'écran cuisine, calculées sur les derniers bons du serveur (50 au plus : approximation du jour).
public struct KitchenStats: Sendable, Equatable {
    public var open: Int
    public var late: Int
    public var averageSeconds: TimeInterval?
    public var servedLastHour: Int
    public var oldestOpen: Date?

    public static func compute(tickets: [KitchenTicket], now: Date = Date(), calendar: Calendar = .autoupdatingCurrent, targetMinutes: Int = KitchenTarget.minutes) -> KitchenStats {
        let open = tickets.filter { $0.status == .pending || $0.status == .inPreparation }
        let limit = TimeInterval(targetMinutes * 60)
        let late = open.filter { ($0.dispatchedAtUtc.map { now.timeIntervalSince($0) } ?? 0) > limit }.count
        let startOfDay = calendar.startOfDay(for: now)
        let served = tickets.filter { ($0.completedAtUtc ?? .distantPast) >= startOfDay }
        let durations = served.compactMap { t -> TimeInterval? in
            guard let done = t.completedAtUtc, let sent = t.dispatchedAtUtc, done >= sent else { return nil }
            return done.timeIntervalSince(sent)
        }
        return KitchenStats(
            open: open.count, late: late,
            averageSeconds: durations.isEmpty ? nil : durations.reduce(0, +) / Double(durations.count),
            servedLastHour: served.filter { now.timeIntervalSince($0.completedAtUtc ?? .distantPast) <= 3600 }.count,
            oldestOpen: open.compactMap(\.dispatchedAtUtc).min())
    }

    /// Portions encore à cuire, toutes salles confondues, de la plus nombreuse à la moins nombreuse.
    public static func allDay(tickets: [KitchenTicket]) -> [(name: String, quantity: Int)] {
        var totals: [String: Int] = [:]
        for t in tickets where t.status == .pending || t.status == .inPreparation {
            for item in t.items { totals[item.productName, default: 0] += item.quantity }
        }
        return totals.map { (name: $0.key, quantity: $0.value) }.sorted { $0.quantity != $1.quantity ? $0.quantity > $1.quantity : $0.name < $1.name }
    }

    public static func recentlyServed(tickets: [KitchenTicket], limit: Int = 5) -> [KitchenTicket] {
        tickets.filter { $0.completedAtUtc != nil }.sorted { ($0.completedAtUtc ?? .distantPast) > ($1.completedAtUtc ?? .distantPast) }.prefix(limit).map { $0 }
    }
}
