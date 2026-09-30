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
struct LaunchConfiguration {
    let isUITest: Bool
    let forceHappyHour: Bool
    let seedsFailedPrintJob: Bool
    let serverOverride: String?
    let autoPin: String?
    let initialSection: Router.Section?
    let startsPaired: Bool

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
            startsPaired: isUITest && args.contains("-UITestPaired")
        )
    }()
}

/// Construit l'`AppModel` selon le mode de lancement et le reconstruit si le serveur change.
@MainActor @Observable
final class AppEnvironment {
    private(set) var model: AppModel
    let router = Router()
    let settings: TerminalSettings
    private(set) var generation = 0
    let launch = LaunchConfiguration.current

    init() {
        let launch = LaunchConfiguration.current
        if launch.isUITest {
            UIView.setAnimationsEnabled(false)
            let defaults = UserDefaults(suiteName: "RestaurantPOS.UITests")!
            defaults.removePersistentDomain(forName: "RestaurantPOS.UITests")
            settings = TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(launch.startsPaired ? .demo : nil))
        } else {
            settings = TerminalSettings(credentialStore: KeychainCredentialStore())
        }
        if let override = launch.serverOverride { settings.serverURL = override }
        model = AppEnvironment.makeModel(settings: settings, launch: launch)
        if let section = launch.initialSection { router.section = section }
        if let pin = launch.autoPin {
            let session = model.session
            Task { for digit in pin { await session.appendDigit(String(digit)) } }
        }
    }

    private static func makeModel(settings: TerminalSettings, launch: LaunchConfiguration) -> AppModel {
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

    /// Applique une nouvelle adresse serveur : nouvelle session, nouvel état.
    func reconnect() {
        model.stopRealtime()
        model = AppEnvironment.makeModel(settings: settings, launch: launch)
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

    /// Le serveur a changé d'adresse (DHCP) : on le retrouve par son nom Bonjour.
    func rediscoverServerIfUnreachable() async {
        guard !launch.isUITest, let name = settings.credentials?.serverName else { return }
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
