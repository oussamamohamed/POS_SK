import Foundation

/// Saisie de la première configuration d'une installation réelle.
public struct LocalSetup: Sendable {
    public var managerName: String
    public var managerPin: String
    public var companyName: String
    public var address: String
    public var siret: String
    public var vatNumber: String

    public init(managerName: String, managerPin: String, companyName: String, address: String, siret: String, vatNumber: String) {
        self.managerName = managerName
        self.managerPin = managerPin
        self.companyName = companyName
        self.address = address
        self.siret = siret
        self.vatNumber = vatNumber
    }
}

extension LocalPosAPI {
    /// Ouvre le fichier d'une installation réelle : jamais de comptes ni de données de démonstration (`seed: .blank`).
    /// C'est le seul point d'entrée de la production ; le défaut `.demo` de l'initialiseur est réservé aux tests.
    public static func openStandalone(path: String) throws -> LocalPosAPI {
        try LocalPosAPI(path: path, seed: .blank)
    }

    /// `true` tant qu'aucun compte n'existe (base vierge) : l'application doit alors proposer la configuration initiale.
    /// Même critère que la garde « déjà configurée » (409) de `completeFirstRun`.
    public func needsSetup() async throws -> Bool {
        try staffRepository.members().isEmpty
    }

    /// Crée le premier responsable (rôle `admin`) et l'identité de l'établissement d'une base vierge. Refusé (409) dès qu'un compte
    /// existe : une installation en service ne peut pas être réinitialisée par cette voie. Nom et SIRET (14 chiffres) sont obligatoires,
    /// le fiscal en a besoin.
    public func completeFirstRun(_ setup: LocalSetup) async throws {
        let name = setup.managerName.trimmingCharacters(in: .whitespacesAndNewlines)
        let company = setup.companyName.trimmingCharacters(in: .whitespacesAndNewlines)
        let siret = setup.siret.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { throw APIError.server(status: 400, message: "Le nom du responsable est requis.") }
        try validate(pin: setup.managerPin)
        guard !company.isEmpty else { throw APIError.server(status: 400, message: "Le nom de l'établissement est requis.") }
        guard siret.count == 14, siret.allSatisfy(\.isASCII), siret.allSatisfy(\.isNumber) else {
            throw APIError.server(status: 400, message: "Le SIRET doit comporter 14 chiffres.")
        }
        let staff = staffRepository, floor = floorRepository
        try db.transaction {
            guard try staff.members().isEmpty else { throw APIError.server(status: 409, message: "L'installation est déjà configurée.") }
            try staff.insert(StaffMember(name: name, role: .admin), pin: setup.managerPin)
            try floor.insertSettings(
                companyName: company, addressLines: setup.address.trimmingCharacters(in: .whitespacesAndNewlines), siret: siret,
                vatNumber: setup.vatNumber.trimmingCharacters(in: .whitespacesAndNewlines)
            )
        }
    }
}
