import Foundation

extension LocalPosAPI {
    private static let unknownSchedule = APIError.server(status: 400, message: "Planning Happy Hour introuvable.")

    /// Lecture ouverte, comme `GET /api/happy-hour/schedules`.
    public func happyHourSchedules() async throws -> [HappyHourSchedule] { try happyHourRepository.schedules() }

    /// Écart de sécurité assumé : le .NET laisse la création anonyme, l'iPad la réserve aux responsables.
    public func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID? {
        try requireManager()
        guard !schedule.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw APIError.server(status: 400, message: "Le nom de la plage est requis.")
        }
        guard LocalTimeOfDay.seconds(from: schedule.startTime) != nil, LocalTimeOfDay.seconds(from: schedule.endTime) != nil else {
            throw APIError.server(status: 400, message: "Format horaire invalide (HH:mm requis).")
        }
        guard schedule.daysOfWeek.allSatisfy({ (0...6).contains($0) }) else {
            throw APIError.server(status: 400, message: "Jour de la semaine invalide (0 à 6).")
        }
        let repository = happyHourRepository
        return try db.transaction { try repository.insert(schedule) }
    }

    public func deleteHappyHourSchedule(id: UUID) async throws {
        try requireManager()
        guard try happyHourRepository.delete(id: id) else { throw APIError.notFound("Plage horaire introuvable.") }
    }

    /// Pose ou remplace une règle par cible. Retourne le nombre de cibles traitées. Tous les refus précèdent les écritures.
    public func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int {
        try requireManager()
        let repository = happyHourRepository
        return try db.transaction { () throws -> Int in
            guard try repository.schedule(id: scheduleId) != nil else { throw Self.unknownSchedule }
            guard !request.targetIds.isEmpty else { throw APIError.server(status: 400, message: "Aucun élément sélectionné.") }
            var fixed: Money?
            var percent: Decimal?
            switch request.pricingMode {
            case .fixedPrice:
                guard let price = request.fixedPrice, price.cents > 0 else {
                    throw APIError.server(status: 400, message: "Le prix fixe doit être strictement supérieur à 0.")
                }
                fixed = price
            case .percentageDiscount:
                guard let value = request.discountPercent, value > 0, value <= 100 else {
                    throw APIError.server(status: 400, message: "Le pourcentage de remise doit être compris entre 0.01% et 100%.")
                }
                percent = value
            }
            for target in request.targetIds {
                let name = try repository.targetName(type: request.targetType, id: target) ?? target
                try repository.upsertRule(
                    scheduleId: scheduleId, targetType: request.targetType, targetId: target, targetName: name,
                    mode: request.pricingMode, fixedPrice: fixed, discountPercent: percent
                )
            }
            return request.targetIds.count
        }
    }

    public func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws {
        try requireManager()
        let repository = happyHourRepository
        try db.transaction {
            guard try repository.schedule(id: scheduleId) != nil else { throw Self.unknownSchedule }
            guard !ruleIds.isEmpty else { throw APIError.server(status: 400, message: "Aucune règle sélectionnée pour la suppression.") }
            _ = try repository.deleteRules(scheduleId: scheduleId, ids: ruleIds)
        }
    }
}
