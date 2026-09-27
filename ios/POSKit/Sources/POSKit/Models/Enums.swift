import Foundation

/// Les enums .NET sont sérialisés en entiers (pas de `JsonStringEnumConverter` côté API).
/// On accepte malgré tout leur nom en texte pour rester tolérant, comme le client web.
public protocol APIEnum: RawRepresentable, CaseIterable, Sendable, Hashable where RawValue == Int {
    /// Nom du membre côté C# (ex. `BillRequested`).
    var apiName: String { get }
    /// Valeur utilisée si l'API renvoie un membre inconnu de ce client.
    static var unknownFallback: Self { get }
}

extension APIEnum {
    static func decodeLeniently(from decoder: Decoder) throws -> Self {
        let container = try decoder.singleValueContainer()
        if let number = try? container.decode(Int.self) {
            return Self(rawValue: number) ?? unknownFallback
        }
        let text = try container.decode(String.self)
        if let number = Int(text) {
            return Self(rawValue: number) ?? unknownFallback
        }
        return allCases.first { $0.apiName.caseInsensitiveCompare(text) == .orderedSame } ?? unknownFallback
    }
}

public enum TableStatus: Int, APIEnum, Codable {
    case free = 0, occupied = 1, billRequested = 2, paid = 3

    public var apiName: String {
        switch self {
        case .free: "Free"
        case .occupied: "Occupied"
        case .billRequested: "BillRequested"
        case .paid: "Paid"
        }
    }

    public var label: String {
        switch self {
        case .free: "Libre"
        case .occupied: "Occupée"
        case .billRequested: "Addition"
        case .paid: "Encaissée"
        }
    }

    public static let unknownFallback = TableStatus.occupied
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public enum CourseType: Int, APIEnum, Codable, Identifiable {
    case direct = 0, suite = 1, dessert = 2, onDemand = 3

    public var id: Int { rawValue }

    public var apiName: String {
        switch self {
        case .direct: "Direct"
        case .suite: "Suite"
        case .dessert: "Dessert"
        case .onDemand: "OnDemand"
        }
    }

    public var label: String {
        switch self {
        case .direct: "Direct"
        case .suite: "Suite"
        case .dessert: "Dessert"
        case .onDemand: "À la demande"
        }
    }

    public static let unknownFallback = CourseType.direct
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public enum PaymentMethod: Int, APIEnum, Codable, Identifiable {
    case cash = 0, creditCard = 1, mealVoucher = 2, giftCard = 3, roomCharge = 4

    public var id: Int { rawValue }

    public var apiName: String {
        switch self {
        case .cash: "Cash"
        case .creditCard: "CreditCard"
        case .mealVoucher: "MealVoucher"
        case .giftCard: "GiftCard"
        case .roomCharge: "RoomCharge"
        }
    }

    public var label: String {
        switch self {
        case .cash: "Espèces"
        case .creditCard: "Carte bancaire"
        case .mealVoucher: "Titre-restaurant"
        case .giftCard: "Carte cadeau"
        case .roomCharge: "Note chambre"
        }
    }

    public var systemImage: String {
        switch self {
        case .cash: "banknote"
        case .creditCard: "creditcard"
        case .mealVoucher: "ticket"
        case .giftCard: "giftcard"
        case .roomCharge: "bed.double"
        }
    }

    /// Seules les espèces peuvent dépasser le montant dû (rendu monnaie).
    public var allowsOverpayment: Bool { self == .cash }

    /// Moyens proposés à la caisse iPad (la note chambre reste sur le back-office web).
    public static let checkoutMethods: [PaymentMethod] = [.cash, .creditCard, .mealVoucher, .giftCard]

    /// Libellé à partir du nom C# renvoyé comme clé par les rapports fiscaux.
    public static func label(forAPIName name: String) -> String {
        allCases.first { $0.apiName == name }?.label ?? name
    }

    public static let unknownFallback = PaymentMethod.cash
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public enum OrderDestination: Int, APIEnum, Codable, Identifiable {
    case takeaway = 0, eatIn = 1, delivery = 2

    public var id: Int { rawValue }

    public var apiName: String {
        switch self {
        case .takeaway: "Takeaway"
        case .eatIn: "EatIn"
        case .delivery: "Delivery"
        }
    }

    public var label: String {
        switch self {
        case .takeaway: "À emporter"
        case .eatIn: "Sur place"
        case .delivery: "Livraison"
        }
    }

    public static let unknownFallback = OrderDestination.takeaway
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public enum TicketStatus: Int, APIEnum, Codable, Identifiable {
    case pending = 0, inPreparation = 1, ready = 2, served = 3

    public var id: Int { rawValue }

    public var apiName: String {
        switch self {
        case .pending: "Pending"
        case .inPreparation: "InPreparation"
        case .ready: "Ready"
        case .served: "Served"
        }
    }

    public var label: String {
        switch self {
        case .pending: "À préparer"
        case .inPreparation: "En préparation"
        case .ready: "Prêt"
        case .served: "Servi"
        }
    }

    /// État suivant après un « bump » en cuisine.
    public var next: TicketStatus {
        switch self {
        case .pending: .inPreparation
        case .inPreparation: .ready
        case .ready, .served: .served
        }
    }

    public static let unknownFallback = TicketStatus.pending
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public enum DiscountType: Int, APIEnum, Codable {
    case percentage = 0, fixedAmount = 1, comp = 2

    public var apiName: String {
        switch self {
        case .percentage: "Percentage"
        case .fixedAmount: "FixedAmount"
        case .comp: "Comp"
        }
    }

    public static let unknownFallback = DiscountType.percentage
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

public enum MealVoucherPolicy: Int, APIEnum, Codable, Identifiable {
    case capAtBalance = 0, strictRejection = 1, customerCreditVoucher = 2

    public var id: Int { rawValue }

    public var apiName: String {
        switch self {
        case .capAtBalance: "CapAtBalance"
        case .strictRejection: "StrictRejection"
        case .customerCreditVoucher: "CustomerCreditVoucher"
        }
    }

    public var label: String {
        switch self {
        case .capAtBalance: "Plafonner (surplus perdu)"
        case .strictRejection: "Refuser tout dépassement"
        case .customerCreditVoucher: "Émettre un avoir"
        }
    }

    public static let unknownFallback = MealVoucherPolicy.capAtBalance
    public init(from decoder: Decoder) throws { self = try Self.decodeLeniently(from: decoder) }
    public func encode(to encoder: Encoder) throws { var c = encoder.singleValueContainer(); try c.encode(rawValue) }
}

/// Rôle opérateur, renvoyé en texte par `/api/auth/login` (`"FloorManager"`).
public enum UserRole: String, Codable, Sendable, CaseIterable {
    case waiter = "Waiter"
    case cashier = "Cashier"
    case kitchenStaff = "KitchenStaff"
    case floorManager = "FloorManager"
    case admin = "Admin"

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let number = try? container.decode(Int.self) {
            self = [.waiter, .cashier, .kitchenStaff, .floorManager, .admin][safe: number] ?? .waiter
        } else {
            let text = try container.decode(String.self)
            self = UserRole.allCases.first { $0.rawValue.caseInsensitiveCompare(text) == .orderedSame } ?? .waiter
        }
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

    /// Clôture Z, annulations : réservé aux responsables (politique `RequireManagerOrAdmin`).
    public var isManager: Bool { self == .floorManager || self == .admin }
}

extension Array {
    subscript(safe index: Int) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
