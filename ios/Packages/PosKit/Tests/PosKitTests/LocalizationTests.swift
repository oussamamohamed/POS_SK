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

    @Test func sourceLanguageIsEnglish() throws {
        // `defaultLocalization` du Package.swift est "en" ; le fichier anglais doit exister et être complet.
        #expect(!(try Self.stringsFile("en")).isEmpty)
    }

    @Test(arguments: ["en", "fr"])
    func everyKeyIsTranslated(language: String) throws {
        let reference = try Self.stringsFile("en")
        let translations = try Self.stringsFile(language)
        #expect(!reference.isEmpty)
        for key in reference.keys {
            let value = translations[key]
            #expect(value != nil && !(value!.isEmpty), "\(language) : \(key)")
        }
    }

    @Test func keysAreSemantic() throws {
        for key in try Self.stringsFile("en").keys {
            #expect(key.range(of: #"^[a-z]+\.[a-z0-9_.]+$"#, options: .regularExpression) != nil, "clé non sémantique : \(key)")
        }
    }
}
