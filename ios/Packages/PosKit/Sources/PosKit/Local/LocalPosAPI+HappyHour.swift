import Foundation

extension LocalPosAPI {
    static let overrideFallbackName = "Dérogation Responsable"

    /// Dérogation active d'abord, sinon planning actif à l'heure locale de l'appareil : jour de la semaine, début inclus, fin exclue
    /// (pas de plage à cheval sur minuit, comme le serveur), priorité la plus haute, le premier créé à égalité.
    func currentHappyHourStatus(now: Date) throws -> HappyHourStatus {
        let repository = happyHourRepository
        let schedules = try repository.schedules()
        if let session = try repository.activeOverride(terminalId: Self.standaloneTerminalId, now: now) {
            let first = schedules.first { $0.isActive }
            return HappyHourStatus(
                isActive: true, isOverride: true, activeScheduleName: first?.name ?? Self.overrideFallbackName, activeScheduleId: first?.id,
                appliesToTakeaway: first?.appliesToTakeaway ?? false,
                currentWindow: HappyHourWindow(
                    startTime: Self.utcTime(session.startsAt), endTime: Self.utcTime(session.expiresAt),
                    remainingMinutes: Self.remainingMinutes(session.expiresAt.timeIntervalSince(now))
                )
            )
        }
        let parts = calendar.dateComponents([.weekday, .hour, .minute, .second], from: now)
        let day = (parts.weekday ?? 1) - 1
        let seconds = (parts.hour ?? 0) * 3600 + (parts.minute ?? 0) * 60 + (parts.second ?? 0)
        let candidates = schedules.compactMap { schedule -> (schedule: HappyHourSchedule, start: Int, end: Int)? in
            guard schedule.isActive, schedule.daysOfWeek.contains(day),
                  let start = LocalTimeOfDay.seconds(from: schedule.startTime), let end = LocalTimeOfDay.seconds(from: schedule.endTime),
                  start <= seconds, seconds < end
            else { return nil }
            return (schedule, start, end)
        }
        guard let best = candidates.max(by: { $0.schedule.priority < $1.schedule.priority }) else { return .inactive }
        return HappyHourStatus(
            isActive: true, isOverride: false, activeScheduleName: best.schedule.name, activeScheduleId: best.schedule.id,
            appliesToTakeaway: best.schedule.appliesToTakeaway,
            currentWindow: HappyHourWindow(
                startTime: LocalTimeOfDay.stored(best.start), endTime: LocalTimeOfDay.stored(best.end),
                remainingMinutes: Self.remainingMinutes(TimeInterval(best.end - seconds))
            )
        )
    }

    /// Lecture ouverte, comme `GET /api/happy-hour/status`. Le terminal demandé est ignoré (terminal fixe).
    public func happyHourStatus(terminalId: String) async throws -> HappyHourStatus {
        try currentHappyHourStatus(now: clock())
    }

    /// Produits dont le tarif baisse pendant la plage en cours. Le prix appliqué à une ligne est celui que le client envoie avec
    /// `isHappyHourApplied` (le serveur .NET ne le recalcule pas non plus).
    public func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable {
        let status = try currentHappyHourStatus(now: clock())
        guard status.isActive else { return HappyHourPricingTable(isActive: false, items: []) }
        let schedules = try happyHourRepository.schedules()
        let schedule = status.activeScheduleId.flatMap { id in schedules.first { $0.id == id } }
            ?? schedules.filter(\.isActive).max { $0.priority < $1.priority }
        guard let schedule, !schedule.priceRules.isEmpty else { return HappyHourPricingTable(isActive: true, items: []) }
        var items: [HappyHourPrice] = []
        for product in try catalogRepository.activeProducts() {
            let result = LocalHappyHourPricing.price(productId: product.id, categoryId: product.categoryId, standard: product.price, rules: schedule.priceRules)
            guard result.isHappyHour, result.effective < product.price else { continue }
            items.append(HappyHourPrice(
                productId: product.id, productName: product.name, standardPrice: product.price, happyHourPrice: result.effective,
                ruleType: LocalHappyHourPricing.ruleType(productId: product.id, categoryId: product.categoryId, rules: schedule.priceRules)
            ))
        }
        return HappyHourPricingTable(isActive: true, items: items)
    }

    /// PIN d'un responsable. Les PIN inconnus comptent dans le verrouillage partagé avec la connexion ; un PIN valide sans droit de
    /// responsable est refusé sans compter comme échec.
    func authenticateSupervisor(pin: String) throws -> StaffMember {
        try ensurePinAttemptsAllowed()
        let insufficient = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")
        guard let supervisor = try staffRepository.activeMember(pin: pin) else {
            try recordFailedPin()
            throw insufficient
        }
        guard supervisor.role.isManager else { throw insufficient }
        return supervisor
    }

    /// Dérogation de responsable : remplace la précédente, durée par défaut 60 minutes. L'audit JET viendra avec le sous-projet fiscal.
    public func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult {
        let supervisor = try authenticateSupervisor(pin: pin)
        let duration = minutes <= 0 ? 60 : min(minutes, 1440)
        let now = clock()
        let repository = happyHourRepository
        try db.transaction {
            try repository.deactivateOverrides(terminalId: Self.standaloneTerminalId)
            try repository.insertOverride(
                terminalId: Self.standaloneTerminalId, operatorId: supervisor.id, operatorName: supervisor.name, startsAt: now,
                expiresAt: now.addingTimeInterval(TimeInterval(duration * 60)), reason: reason.isEmpty ? "Dérogation manuelle responsable" : reason
            )
        }
        return OperationResult(success: true, message: "Happy Hour activé/prolongé de \(duration) minutes avec succès.")
    }

    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult {
        _ = try authenticateSupervisor(pin: pin)
        try happyHourRepository.deactivateOverrides(terminalId: Self.standaloneTerminalId)
        return OperationResult(success: true, message: "Dérogation Happy Hour désactivée.")
    }

    private static func remainingMinutes(_ seconds: TimeInterval) -> Int { Int(max(0, (seconds / 60).rounded(.up))) }

    private static func utcTime(_ date: Date) -> String {
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let parts = utc.dateComponents([.hour, .minute, .second], from: date)
        return LocalTimeOfDay.stored((parts.hour ?? 0) * 3600 + (parts.minute ?? 0) * 60 + (parts.second ?? 0))
    }
}
