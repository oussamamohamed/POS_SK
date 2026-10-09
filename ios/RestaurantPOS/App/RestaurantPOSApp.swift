import SwiftUI
import PosKit

@main
struct RestaurantPOSApp: App {
    @State private var environment = AppEnvironment()
    @AppStorage(AppearancePreference.storageKey) private var appearance = AppearancePreference.dark.rawValue

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(environment)
                .environment(environment.model)
                .environment(environment.router)
                // I2(b) : chiffres occidentaux même sur un iPad réglé en région arabe (ex. ar_SA, ar_EG
                // dont le système de numérotation par défaut est arabo-indien). Ne force que le système
                // de numérotation : langue et région restent celles de l'appareil, donc la langue de
                // l'interface (résolue via `Bundle.main.preferredLocalizations`) n'est pas affectée.
                .environment(\.locale, Money.latinDigitsLocale(.current))
                .id(environment.generation)
                .tint(Theme.primary)
                .preferredColorScheme((AppearancePreference(rawValue: appearance) ?? .dark).colorScheme)
        }
    }
}

/// Configuration de lancement.
/// - `-UITestMode` : backend en mémoire, réglages éphémères, animations coupées (tests XCUITest).
/// - `-UITestHappyHour` : Happy Hour forcé dès le lancement (mode test).
/// - `POS_SERVER_URL` (variable d'environnement) : force l'adresse du serveur.
/// - `-UITestFailedPrintJob` : une impression en échec sur l'imprimante cuisine (mode test).
/// - `-UITestPin 1234` : déverrouillage automatique (mode test uniquement).
/// - `-UITestSection floor` : écran affiché après connexion (mode test uniquement).
/// - `-UITestPaired` : poste déjà appairé (sinon écran d'appairage, mode test uniquement).
/// - `-UITestLocal` : mode autonome sur une base SQLite en mémoire avec les données de démonstration (mode test uniquement).
/// - `-UITestLocalBlank` : mode autonome sur une base en mémoire vierge, donc première configuration (mode test uniquement).
/// - `-UITestModeChoice` : aucun mode mémorisé, l'écran « Serveur / Autonome » s'affiche (mode test uniquement).
struct LaunchConfiguration {
    let isUITest: Bool
    let forceHappyHour: Bool
    let seedsFailedPrintJob: Bool
    let serverOverride: String?
    let autoPin: String?
    let initialSection: Router.Section?
    let startsPaired: Bool
    let usesLocalBackend: Bool
    let localIsBlank: Bool
    let showsModeChoice: Bool

    static let current: LaunchConfiguration = {
        let args = ProcessInfo.processInfo.arguments
        let defaults = UserDefaults.standard
        let isUITest = args.contains("-UITestMode")
        return LaunchConfiguration(
            isUITest: isUITest,
            forceHappyHour: args.contains("-UITestHappyHour"),
            seedsFailedPrintJob: args.contains("-UITestFailedPrintJob"),
            serverOverride: ProcessInfo.processInfo.environment["POS_SERVER_URL"],
            autoPin: isUITest ? defaults.string(forKey: "UITestPin") : nil,
            initialSection: isUITest ? defaults.string(forKey: "UITestSection").flatMap(Router.Section.init(rawValue:)) : nil,
            startsPaired: isUITest && args.contains("-UITestPaired"),
            usesLocalBackend: isUITest && (args.contains("-UITestLocal") || args.contains("-UITestLocalBlank")),
            localIsBlank: isUITest && args.contains("-UITestLocalBlank"),
            showsModeChoice: isUITest && args.contains("-UITestModeChoice")
        )
    }()
}

/// Construit l'`AppModel` selon le mode de lancement et le reconstruit si le serveur change.
@MainActor @Observable
final class AppEnvironment {
    /// Première configuration d'une installation autonome.
    enum SetupState { case checking, needed, done }

    private(set) var model: AppModel
    let router = Router()
    private(set) var settings: TerminalSettings
    private(set) var generation = 0
    let launch = LaunchConfiguration.current
    /// `nil` tant que l'utilisateur n'a pas choisi (premier lancement, aucun poste appairé).
    private(set) var mode: AppMode?
    private(set) var setupState = SetupState.done
    /// Ouverture de la base locale impossible : l'application affiche l'erreur au lieu de démarrer.
    private(set) var startupError: String?
    @ObservationIgnored private let defaults: UserDefaults
    /// **Une seule instance** de l'acteur pour tout le processus : la recréer ouvrirait une seconde connexion sur le même fichier.
    @ObservationIgnored private var localAPI: LocalPosAPI?

    var isStandalone: Bool { mode == .standalone }

    init() {
        let launch = LaunchConfiguration.current
        let defaults: UserDefaults
        let serverSettings: TerminalSettings
        if launch.isUITest {
            UIView.setAnimationsEnabled(false)
            defaults = UserDefaults(suiteName: "RestaurantPOS.UITests")!
            defaults.removePersistentDomain(forName: "RestaurantPOS.UITests")
            serverSettings = TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(launch.startsPaired ? .demo : nil))
        } else {
            defaults = .standard
            serverSettings = TerminalSettings(credentialStore: KeychainCredentialStore())
        }
        if let override = launch.serverOverride { serverSettings.serverURL = override }

        let stored = defaults.string(forKey: AppMode.storageKey).flatMap(AppMode.init(rawValue:))
        let mode: AppMode?
        if launch.usesLocalBackend {
            mode = .standalone
        } else if launch.isUITest {
            mode = launch.showsModeChoice ? nil : .server
        } else {
            mode = AppMode.resolve(stored: stored, hasPairedCredentials: serverSettings.isPaired)
        }
        let settings = mode == .standalone ? Self.standaloneSettings(defaults: defaults) : serverSettings
        var api: LocalPosAPI?
        var startupError: String?
        if mode == .standalone {
            do { api = try Self.openLocal(launch: launch) } catch { startupError = Self.describe(error) }
        }

        self.defaults = defaults
        self.mode = mode
        self.settings = settings
        self.localAPI = api
        self.startupError = startupError
        self.setupState = mode == .standalone && startupError == nil ? .checking : .done
        model = Self.makeModel(settings: settings, launch: launch, localAPI: api)
        if let section = launch.initialSection { router.section = section }
        if let pin = launch.autoPin {
            let session = model.session
            Task { for digit in pin { await session.appendDigit(String(digit)) } }
        }
    }

    private static func standaloneSettings(defaults: UserDefaults) -> TerminalSettings {
        TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(.standalone))
    }

    /// Production : fichier protégé, jamais de données de démonstration. Tests : base en mémoire (démonstration ou vierge).
    private static func openLocal(launch: LaunchConfiguration) throws -> LocalPosAPI {
        if launch.isUITest {
            return try LocalPosAPI(path: ":memory:", seed: launch.usesLocalBackend && !launch.localIsBlank ? .demo : .blank)
        }
        return try LocalPosAPI.openStandalone(path: LocalDatabaseLocation.defaultURL().path)
    }

    private static func describe(_ error: Error) -> String {
        (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
    }

    private static func makeModel(settings: TerminalSettings, launch: LaunchConfiguration, localAPI: LocalPosAPI?) -> AppModel {
        if let localAPI { return AppModel(api: localAPI, settings: settings) }
        if launch.isUITest {
            let api = InMemoryPosAPI()
            if launch.forceHappyHour { Task { await api.forceHappyHour(minutes: 45) } }
            if launch.seedsFailedPrintJob {
                Task { if let kitchen = try? await api.printers().first(where: { $0.name.contains("Cuisine") }) { await api.seedFailedPrintJob(printerId: kitchen.id) } }
            }
            return AppModel(api: api, settings: settings)
        }
        let url = settings.url ?? URL(string: "http://localhost:5080")!
        return AppModel(api: HTTPPosAPI(baseURL: url, deviceToken: settings.credentials?.token), settings: settings, realtimeBaseURL: url)
    }

    /// Mémorise le choix du mode et reconstruit l'état. Le mode autonome ouvre la base (une seule fois) ; une erreur d'ouverture est
    /// affichée par `StartupErrorScreen`.
    func choose(_ newMode: AppMode) {
        defaults.set(newMode.rawValue, forKey: AppMode.storageKey)
        model.stopRealtime()
        mode = newMode
        startupError = nil
        if newMode == .standalone {
            settings = Self.standaloneSettings(defaults: defaults)
            if localAPI == nil {
                do { localAPI = try Self.openLocal(launch: launch) } catch { startupError = Self.describe(error) }
            }
            setupState = startupError == nil ? .checking : .done
        } else {
            setupState = .done
        }
        model = Self.makeModel(settings: settings, launch: launch, localAPI: newMode == .standalone ? localAPI : nil)
        router.section = .order
        generation += 1
    }

    /// Nouvel essai après une erreur d'ouverture de la base.
    func retryStartup() { choose(mode ?? .standalone) }

    /// Mode autonome : la base est-elle vierge ? (première configuration à faire)
    func refreshSetup() async {
        guard isStandalone, let localAPI else { return }
        do { setupState = try await localAPI.needsSetup() ? .needed : .done } catch { startupError = Self.describe(error) }
    }

    /// Crée le premier responsable et l'établissement ; lève l'erreur de validation du modèle pour l'afficher dans le formulaire.
    func completeSetup(_ setup: LocalSetup) async throws {
        guard let localAPI else { return }
        try await localAPI.completeFirstRun(setup)
        setupState = .done
    }

    /// Applique une nouvelle adresse serveur : nouvelle session, nouvel état (mode serveur).
    func reconnect() {
        model.stopRealtime()
        model = Self.makeModel(settings: settings, launch: launch, localAPI: nil)
        router.section = .order
        generation += 1
    }

    /// Appairage réussi. Même serveur (ré-appairage après révocation) : on garde le modèle, donc le brouillon,
    /// et on change seulement le jeton. Nouveau serveur : nouvelle session.
    @discardableResult
    func completePairing(serverURL: URL, response: PairResponse) async -> Bool {
        let serverChanged = serverURL.absoluteString != settings.serverURL
        guard await model.applyPairing(serverURL: serverURL.absoluteString, credentials: DeviceCredentials(response)) else { return false }
        if serverChanged { reconnect() }
        return true
    }

    /// Le serveur a changé d'adresse (DHCP) : on le retrouve par son nom Bonjour. Sans objet en mode autonome.
    func rediscoverServerIfUnreachable() async {
        guard !launch.isUITest, !isStandalone, let name = settings.credentials?.serverName else { return }
        if (try? await model.api.health()) != nil { return }
        guard let url = await ServerBrowser.resolve(name: name), url.absoluteString != settings.serverURL else { return }
        settings.serverURL = url.absoluteString
        reconnect()
    }
}

@MainActor @Observable
final class Router {
    enum Section: String, CaseIterable, Identifiable {
        case order, floor, kitchen, fiscal, admin
        var id: String { rawValue }

        var title: String {
            switch self {
            case .order: String(localized: "common.nav_order")
            case .floor: String(localized: "common.nav_floor")
            case .kitchen: String(localized: "common.nav_kitchen")
            case .fiscal: String(localized: "common.nav_fiscal")
            case .admin: String(localized: "common.nav_admin")
            }
        }

        var systemImage: String {
            switch self {
            case .order: "cart"
            case .floor: "square.grid.3x3.topleft.filled"
            case .kitchen: "flame"
            case .fiscal: "doc.text.magnifyingglass"
            case .admin: "gearshape.2"
            }
        }

        var requiresManager: Bool { self == .fiscal || self == .admin }
    }

    var section: Section = .order
}
