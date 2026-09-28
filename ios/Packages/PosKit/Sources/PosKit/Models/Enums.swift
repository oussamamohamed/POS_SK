import Foundation

/// Les enums .NET sont sérialisés en entiers (aucun `JsonStringEnumConverter` côté API).
/// Une valeur inconnue retombe sur `fallback` pour ne jamais casser le décodage.
public protocol TolerantIntEnum: RawRepresentable, Codable, Sendable, Hashable, CaseIterable where RawValue == Int {
    static var fallback: Self { get }
}

public extension TolerantIntEnum {
    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let raw = try? container.decode(Int.self) {
            self = Self(rawValue: raw) ?? Self.fallback
        } else if let name = try? container.decode(String.self),
                  let match = Self.allCases.first(where: { "\($0)".caseInsensitiveCompare(name) == .orderedSame }) {
            self = match
        } else {
            self = Self.fallback
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        try container.encode(rawValue)
    }
}

public enum TableStatus: Int, TolerantIntEnum {
    case free = 0, occupied = 1, billRequested = 2, paid = 3
    public static let fallback = TableStatus.free

    public var label: String {
        switch self {
        case .free: L10n.string("common.table_status_free")
        case .occupied: L10n.string("common.table_status_occupied")
        case .billRequested: L10n.string("common.table_status_bill_requested")
        case .paid: L10n.string("common.table_status_paid")
        }
    }
}

public enum TicketStatus: Int, TolerantIntEnum {
    case pending = 0, inPreparation = 1, ready = 2, served = 3
    public static let fallback = TicketStatus.pending

    public var label: String {
        switch self {
        case .pending: L10n.string("common.ticket_status_pending")
        case .inPreparation: L10n.string("common.ticket_status_in_preparation")
        case .ready: L10n.string("common.ticket_status_ready")
        case .served: L10n.string("common.ticket_status_served")
        }
    }
}

/// Attention : côté serveur `Takeaway = 0`, `EatIn = 1` (le client web inversait les deux).
public enum OrderDestination: Int, TolerantIntEnum {
    case takeaway = 0, eatIn = 1, delivery = 2
    public static let fallback = OrderDestination.takeaway

    public var label: String {
        switch self {
        case .takeaway: L10n.string("common.destination_takeaway")
        case .eatIn: L10n.string("common.destination_eat_in")
        case .delivery: L10n.string("common.destination_delivery")
        }
    }
}

public enum CourseType: Int, TolerantIntEnum {
    case direct = 0, suite = 1, dessert = 2, onDemand = 3
    public static let fallback = CourseType.direct

    public var label: String {
        switch self {
        case .direct: L10n.string("common.course_direct")
        case .suite: L10n.string("common.course_suite")
        case .dessert: L10n.string("common.course_dessert")
        case .onDemand: L10n.string("common.course_on_demand")
        }
    }

    /// Cycle Direct → Suite → Dessert → Direct (identique au client web).
    public var next: CourseType {
        switch self {
        case .direct: .suite
        case .suite: .dessert
        case .dessert, .onDemand: .direct
        }
    }
}

public enum DiscountType: Int, TolerantIntEnum {
    case percentage = 0, fixedAmount = 1, comp = 2
    public static let fallback = DiscountType.percentage
}

public enum PaymentMethod: Int, TolerantIntEnum {
    case cash = 0, creditCard = 1, mealVoucher = 2, giftCard = 3, roomCharge = 4
    public static let fallback = PaymentMethod.cash

    public var label: String {
        switch self {
        case .cash: L10n.string("common.payment_method_cash")
        case .creditCard: L10n.string("common.payment_method_credit_card")
        case .mealVoucher: L10n.string("common.payment_method_meal_voucher")
        case .giftCard: L10n.string("common.payment_method_gift_card")
        case .roomCharge: L10n.string("common.payment_method_room_charge")
        }
    }

    /// Libellé à partir du nom .NET renvoyé dans les rapports (« CreditCard », « Cash »…).
    public static func label(forServerName name: String) -> String {
        allCases.first { "\($0)".caseInsensitiveCompare(name) == .orderedSame }?.label ?? name
    }
}

public enum MealVoucherPolicy: Int, TolerantIntEnum {
    case capAtBalance = 0, strictRejection = 1, customerCreditVoucher = 2
    public static let fallback = MealVoucherPolicy.capAtBalance

    public var label: String {
        switch self {
        case .capAtBalance: L10n.string("common.voucher_policy_cap")
        case .strictRejection: L10n.string("common.voucher_policy_reject")
        case .customerCreditVoucher: L10n.string("common.voucher_policy_credit_voucher")
        }
    }
}

public enum HappyHourTargetType: Int, TolerantIntEnum {
    case product = 0, category = 1
    public static let fallback = HappyHourTargetType.product
}

public enum HappyHourPricingMode: Int, TolerantIntEnum {
    case fixedPrice = 0, percentageDiscount = 1
    public static let fallback = HappyHourPricingMode.fixedPrice
}

/// Rôles opérateur (sérialisés en chaîne par l'API d'authentification).
public enum UserRole: String, Codable, Sendable, CaseIterable, Hashable {
    case waiter = "Waiter"
    case cashier = "Cashier"
    case kitchenStaff = "KitchenStaff"
    case floorManager = "FloorManager"
    case admin = "Admin"

    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = UserRole.allCases.first { $0.rawValue.caseInsensitiveCompare(raw) == .orderedSame } ?? .waiter
    }

    public var label: String {
        switch self {
        case .waiter: L10n.string("common.role_waiter")
        case .cashier: L10n.string("common.role_cashier")
        case .kitchenStaff: L10n.string("common.role_kitchen_staff")
        case .floorManager: L10n.string("common.role_floor_manager")
        case .admin: L10n.string("common.role_admin")
        }
    }

    public var isManager: Bool { self == .floorManager || self == .admin }
    public var canBumpKitchen: Bool { self == .kitchenStaff || isManager }
}
