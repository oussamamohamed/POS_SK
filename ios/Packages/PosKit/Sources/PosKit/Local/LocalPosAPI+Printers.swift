import Foundation

extension LocalPosAPI {
    static let printerNotFound = APIError.notFound("Imprimante introuvable.")

    /// Liste anonyme, comme `GET /api/printers`.
    public func printers() async throws -> [Printer] { try printerRepository.all() }

    /// Création (`isNew`) ou mise à jour. Comme le serveur : nom et adresse nettoyés, port 9100 par défaut, papier 58 ou 80 mm (sinon 80),
    /// imprimante créée active. Écarts : nom ou IP vide refusé en 400 (le .NET lève une exception), identifiant inconnu en 404.
    public func savePrinter(_ printer: Printer, isNew: Bool) async throws {
        try requireAuth()
        let name = printer.name.trimmingCharacters(in: .whitespacesAndNewlines)
        let address = printer.ipAddress.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, !address.isEmpty else {
            throw APIError.server(status: 400, message: "Le nom et l'adresse IP de l'imprimante sont requis.")
        }
        var clean = printer
        clean.name = name
        clean.ipAddress = address
        clean.port = printer.port > 0 ? printer.port : 9100
        clean.paperWidthMm = [58, 80].contains(printer.paperWidthMm) ? printer.paperWidthMm : 80
        if isNew {
            clean.isActive = true
            guard try printerRepository.printer(id: clean.id) == nil else { throw APIError.server(status: 409, message: "Imprimante déjà enregistrée.") }
            try printerRepository.insert(clean)
        } else {
            guard try printerRepository.update(clean) else { throw Self.printerNotFound }
        }
    }

    /// L'envoi direct arrive avec le sous-projet « impression » : le test répond clairement qu'il n'est pas disponible.
    public func testPrinter(id: UUID) async throws -> TestPrintResult {
        try requireAuth()
        guard try printerRepository.printer(id: id) != nil else { throw Self.printerNotFound }
        return TestPrintResult(success: false, message: "L'impression directe n'est pas encore disponible en mode autonome.")
    }

    /// Aucune file d'impression locale : ni état en ligne, ni impression en attente ou en échec.
    public func printerStatuses() async throws -> [PrinterStatus] {
        try requireAuth()
        return try printerRepository.all().map { PrinterStatus(printerId: $0.id, name: $0.name, isActive: $0.isActive) }
    }

    public func printJobs(printerId: UUID) async throws -> [PrintJobInfo] {
        try requireManager()
        guard try printerRepository.printer(id: printerId) != nil else { throw Self.printerNotFound }
        return []
    }

    public func retryPrintJob(id: UUID) async throws {
        try requireManager()
        throw APIError.notFound("Impression introuvable.")
    }

    public func cancelPrintJob(id: UUID) async throws {
        try requireManager()
        throw APIError.notFound("Impression introuvable.")
    }
}
