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
        case .free: "Libre"
        case .occupied: "Occupée"
        case .billRequested: "Addition"
        case .paid: "Encaissée"
        }
    }
}

public enum TicketStatus: Int, TolerantIntEnum {
    case pending = 0, inPreparation = 1, ready = 2, served = 3
    public static let fallback = TicketStatus.pending

    public var label: String {
        switch self {
        case .pending: "En attente"
        case .inPreparation: "En préparation"
        case .ready: "Prêt"
        case .served: "Servi"
        }
    }
}

/// Attention : côté serveur `Takeaway = 0`, `EatIn = 1` (le client web inversait les deux).
public enum OrderDestination: Int, TolerantIntEnum {
    case takeaway = 0, eatIn = 1, delivery = 2
    public static let fallback = OrderDestination.takeaway

    public var label: String {
        switch self {
        case .takeaway: "À emporter"
        case .eatIn: "Sur place"
        case .delivery: "Livraison"
        }
    }
}

public enum CourseType: Int, TolerantIntEnum {
    case direct = 0, suite = 1, dessert = 2, onDemand = 3
    public static let fallback = CourseType.direct

    public var label: String {
        switch self {
        case .direct: "Direct"
        case .suite: "Suite"
        case .dessert: "Dessert"
        case .onDemand: "À la demande"
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
        case .cash: "Espèces"
        case .creditCard: "Carte bancaire"
        case .mealVoucher: "Titre-restaurant"
        case .giftCard: "Carte cadeau"
        case .roomCharge: "Note de chambre"
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
        case .capAtBalance: "Plafonner (pas de rendu)"
        case .strictRejection: "Refuser tout dépassement"
        case .customerCreditVoucher: "Émettre un avoir"
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
        case .waiter: "Serveur"
        case .cashier: "Caissier"
        case .kitchenStaff: "Cuisine"
        case .floorManager: "Responsable"
        case .admin: "Administrateur"
        }
    }

    public var isManager: Bool { self == .floorManager || self == .admin }
    public var canBumpKitchen: Bool { self == .kitchenStaff || isManager }
}
