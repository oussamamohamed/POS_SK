import Foundation

/// Les 14 allergènes à déclaration obligatoire, codés en bits 0 à 13 du masque.
/// Miroir de `RestaurantPos.Domain.ValueObjects.Allergens` (.NET) : même ordre, mêmes noms, ne jamais réordonner.
public enum Allergens {
    public static let names: [String] = [
        "gluten", "crustaceans", "eggs", "fish", "peanuts", "soy", "milk",
        "nuts", "celery", "mustard", "sesame", "sulphites", "lupin", "molluscs"
    ]

    public static let allMask = (1 << names.count) - 1

    public static func isValid(mask: Int) -> Bool { (0...allMask).contains(mask) }

    public static func bit(of name: String) -> Int? { names.firstIndex(of: name) }

    public static func contains(_ mask: Int, name: String) -> Bool {
        guard let bit = bit(of: name) else { return false }
        return mask & (1 << bit) != 0
    }

    /// Noms présents dans le masque, dans l'ordre des bits.
    public static func names(in mask: Int) -> [String] {
        names.enumerated().filter { mask & (1 << $0.offset) != 0 }.map(\.element)
    }
}
