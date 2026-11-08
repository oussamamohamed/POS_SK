import Testing
@testable import PosKit

@Suite("Allergènes")
struct AllergensTests {
    /// Même liste que `AllergensTests.cs` côté .NET : l'ordre des bits est figé.
    static let expected = ["gluten", "crustaceans", "eggs", "fish", "peanuts", "soy", "milk",
                           "nuts", "celery", "mustard", "sesame", "sulphites", "lupin", "molluscs"]

    @Test func orderIsFrozen() { #expect(Allergens.names == Self.expected) }

    @Test func maskHelpers() {
        #expect(Allergens.allMask == 16383)
        #expect(Allergens.isValid(mask: 0) && Allergens.isValid(mask: 16383))
        #expect(!Allergens.isValid(mask: -1) && !Allergens.isValid(mask: 16384))
        #expect(Allergens.contains(0b1000001, name: "milk") && !Allergens.contains(0b1000001, name: "fish"))
        #expect(Allergens.names(in: 0b1000001) == ["gluten", "milk"])
    }

    @Test func filterHidesUndeclaredAndMatching() {
        let free = Product(name: "A", categoryId: "C", price: .zero, allergenMask: 0, allergensDeclared: true)
        let gluten = Product(name: "B", categoryId: "C", price: .zero, allergenMask: 1, allergensDeclared: true)
        let unknown = Product(name: "C", categoryId: "C", price: .zero)
        #expect([free, gluten, unknown].filter { $0.passesAllergenFilter(1) } == [free])
        #expect([free, gluten, unknown].filter { $0.passesAllergenFilter(0) }.count == 3)
    }
}
