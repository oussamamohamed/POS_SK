import Foundation
import Observation

/// Sections de la barre latérale.
public enum AppSection: String, CaseIterable, Identifiable, Hashable, Sendable {
    case floor, order, counter, kitchen, reports, settings

    public var id: String { rawValue }

    public var title: String {
        switch self {
        case .floor: "Salle"
        case .order: "Prise de commande"
        case .counter: "Comptoir"
        case .kitchen: "Cuisine"
        case .reports: "Rapports fiscaux"
        case .settings: "Réglages"
        }
    }

    public var systemImage: String {
        switch self {
        case .floor: "square.grid.3x3.topleft.filled"
        case .order: "fork.knife"
        case .counter: "bag"
        case .kitchen: "flame"
        case .reports: "doc.text.magnifyingglass"
        case .settings: "gearshape"
        }
    }
}

/// Racine de l'application : réglages, session opérateur, navigation et écrans.
@MainActor
@Observable
public final class AppModel {
    public private(set) var settings: TerminalSettings
    public private(set) var session: OperatorSession?
    public var section: AppSection = .floor
    public var pin = PinEntry()
    public private(set) var pinError: String?
    public private(set) var isAuthenticating = false
    /// Incrémenté à chaque PIN refusé : déclenche l'animation de secousse.
    public private(set) var pinFailures = 0

    public let context: POSContext
    public let catalog: CatalogModel
    public let floor: FloorPlanModel
    public let order: OrderModel
    public let kitchen: KitchenModel
    public let fiscal: FiscalModel
    public let held: HeldOrdersModel

    @ObservationIgnored private let settingsStore: SettingsStore
    @ObservationIgnored private let makeAPI: @MainActor (TerminalSettings) -> POSAPI

    public init(
        settingsStore: SettingsStore,
        defaultSettings: TerminalSettings = TerminalSettings(),
        makeAPI: @escaping @MainActor (TerminalSettings) -> POSAPI = AppModel.defaultAPIFactory()
    ) {
        let loaded = settingsStore.load() ?? defaultSettings
        self.settingsStore = settingsStore
        self.makeAPI = makeAPI
        self.settings = loaded
        let ctx = POSContext(api: makeAPI(loaded), terminalId: loaded.terminalId)
        self.context = ctx
        self.catalog = CatalogModel(context: ctx)
        self.floor = FloorPlanModel(context: ctx)
        self.order = OrderModel(context: ctx)
        self.kitchen = KitchenModel(context: ctx)
        self.fiscal = FiscalModel(context: ctx)
        self.held = HeldOrdersModel(context: ctx)
        ctx.onUnauthorized = { [weak self] in
            self?.expireSession()
        }
    }

    /// Fabrique par défaut : mode démo en mémoire, sinon client HTTP vers le serveur configuré.
    public static func defaultAPIFactory() -> @MainActor (TerminalSettings) -> POSAPI {
        let demo = InMemoryPOSBackend()
        return { settings in
            if !settings.demoMode, let url = settings.serverURL {
                return POSAPIClient(baseURL: url)
            }
            return demo
        }
    }

    public var currentOperator: Operator? { session?.operator }
    public var isUnlocked: Bool { session != nil }

    // MARK: Session

    /// Saisie d'un chiffre sur le pavé PIN (validation automatique à la longueur configurée).
    public func pinDigit(_ digit: Int) async {
        guard !isAuthenticating else { return }
        pinError = nil
        if pin.append(digit, autoSubmitLength: settings.pinAutoSubmitLength) {
            await submitPin()
        }
    }

    public func pinBackspace() {
        pin.backspace()
        pinError = nil
    }

    public func submitPin() async {
        guard pin.isComplete, !isAuthenticating else { return }
        let code = pin.code
        isAuthenticating = true
        defer { isAuthenticating = false }
        do {
            let newSession = try await context.api.login(pin: code)
            session = newSession
            context.currentOperator = newSession.operator
            pin.clear()
            pinError = nil
            section = newSession.operator.role == .kitchenStaff ? .kitchen : .floor
            context.notify("Bonjour \(newSession.operator.shortName)", style: .success)
        } catch {
            pin.clear()
            pinFailures += 1
            pinError = (error as? LocalizedError)?.errorDescription ?? "Code PIN invalide."
        }
    }

    /// Verrouille la caisse (changement de serveur). Les saisies locales sont enregistrées avant.
    public func lock() async {
        if order.hasPending {
            _ = await order.save()
        }
        await context.api.logout()
        session = nil
        context.currentOperator = nil
        pin.clear()
    }

    private func expireSession() {
        session = nil
        context.currentOperator = nil
        pin.clear()
        pinError = "Session expirée. Saisissez à nouveau votre code PIN."
    }

    // MARK: Navigation

    /// Ouvre la prise de commande sur une table.
    public func openTable(_ tableNumber: String) async {
        await order.open(.table(tableNumber))
        section = .order
    }

    /// Démarre (ou reprend) la vente directe au comptoir.
    public func openCounter() async {
        if order.context != .counter {
            await order.open(.counter, destination: order.destination)
        }
        section = .counter
    }

    // MARK: Réglages

    /// Applique de nouveaux réglages ; changer de serveur ferme la session en cours.
    public func apply(_ newSettings: TerminalSettings) async {
        let serverChanged = newSettings.demoMode != settings.demoMode
            || newSettings.serverURL != settings.serverURL
        if serverChanged {
            // On termine proprement avec l'ancien serveur avant de basculer.
            await order.close()
            if isUnlocked { await lock() }
        }
        settings = newSettings
        settingsStore.save(newSettings)
        context.terminalId = newSettings.terminalId
        if serverChanged {
            context.api = makeAPI(newSettings)
            await catalog.load(force: true)
        }
    }

    /// Teste la connexion à un serveur avant de l'enregistrer.
    public func testConnection(_ candidate: TerminalSettings) async -> Result<String, APIError> {
        let api = makeAPI(candidate)
        do {
            let healthy = try await api.health()
            return healthy
                ? .success(candidate.demoMode ? "Mode démonstration actif." : "Serveur de caisse joignable.")
                : .failure(.server(status: 503, message: "Le serveur répond mais n'est pas prêt."))
        } catch let error as APIError {
            return .failure(error)
        } catch {
            return .failure(.network(error.localizedDescription))
        }
    }
}
