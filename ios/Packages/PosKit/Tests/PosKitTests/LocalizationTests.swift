import Foundation
import Testing
@testable import PosKit

/// `swift build`/`swift test` en ligne de commande ne compilent pas les `.xcstrings` (pas de `.lproj`
/// généré dans `.build`, vérifié via `find .build -name "*.strings" -path "*fr.lproj*"`) : PosKit utilise
/// donc `Resources/<langue>.lproj/Localizable.strings`, lus ici directement sur le disque.
@Suite("Localisation PosKit")
struct LocalizationTests {
    static func stringsFile(_ language: String) throws -> [String: String] {
        let url = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
            .appendingPathComponent("Sources/PosKit/Resources/\(language).lproj/Localizable.strings")
        let dict = try #require(NSDictionary(contentsOf: url) as? [String: String])
        return dict
    }

    @Test func englishCatalogIsNotEmpty() throws {
        // `defaultLocalization` du Package.swift est "en" ; le fichier anglais (référence) doit exister et ne pas être vide.
        #expect(!(try Self.stringsFile("en")).isEmpty)
    }

    /// Spécificateurs de format (`%@`, `%lld`, `%d`, `%%`, positionnels `%1$@` normalisés) et isolats
    /// Unicode (U+2066…U+2069) : une traduction doit garder exactement ceux de l'anglais.
    static func formatSignature(_ value: String) -> [String] {
        let specifiers = value.matches(of: /%(?:\d+\$)?(?:lld|ld|d|@|f|%)/).map {
            String($0.output).replacing(/\d+\$/, with: "")
        }
        let isolates = value.unicodeScalars.filter { (0x2066...0x2069).contains($0.value) }.map { String($0) }
        return (specifiers + isolates).sorted()
    }

    @Test(arguments: ["en", "fr", "ar"])
    func everyKeyIsTranslated(language: String) throws {
        let reference = try Self.stringsFile("en")
        let translations = try Self.stringsFile(language)
        #expect(!reference.isEmpty)
        #expect(Set(translations.keys) == Set(reference.keys), "\(language) : clés en trop ou manquantes")
        for (key, english) in reference {
            let value = translations[key]
            #expect(value != nil && !(value!.isEmpty), "\(language) : \(key)")
            if let value { #expect(Self.formatSignature(value) == Self.formatSignature(english), "\(language) : format \(key)") }
        }
    }

    /// Catalogue de l'app (`ios/RestaurantPOS/Resources/Localizable.xcstrings`), compilé par Xcode seulement :
    /// vérifié ici par lecture directe du JSON (5 niveaux au-dessus de ce fichier = `ios/`).
    @Test(arguments: ["en", "fr", "ar"])
    func everyAppCatalogKeyIsTranslated(language: String) throws {
        var url = URL(fileURLWithPath: #filePath)
        for _ in 0..<5 { url.deleteLastPathComponent() }
        url.appendPathComponent("RestaurantPOS/Resources/Localizable.xcstrings")
        let catalog = try #require(try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any])
        let strings = try #require(catalog["strings"] as? [String: [String: Any]])
        #expect(!strings.isEmpty)
        func unit(_ entry: [String: Any], _ lang: String) -> [String: Any]? {
            ((entry["localizations"] as? [String: Any])?[lang] as? [String: Any])?["stringUnit"] as? [String: Any]
        }
        for (key, entry) in strings {
            let english = unit(entry, "en")?["value"] as? String ?? key
            let translated = unit(entry, language)
            let value = translated?["value"] as? String ?? ""
            #expect(translated?["state"] as? String == "translated" && !value.isEmpty, "\(language) : \(key)")
            #expect(Self.formatSignature(value) == Self.formatSignature(english), "\(language) : format \(key)")
        }
    }

    @Test func keysAreSemantic() throws {
        for key in try Self.stringsFile("en").keys {
            #expect(key.range(of: #"^[a-z]+\.[a-z0-9_.]+$"#, options: .regularExpression) != nil, "clé non sémantique : \(key)")
        }
    }
}
