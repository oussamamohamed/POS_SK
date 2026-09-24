import SwiftUI
import PosKit

@main
struct RestaurantPOSApp: App {
    @State private var environment = AppEnvironment()

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(environment)
                .environment(environment.model)
                .environment(environment.router)
                .id(environment.generation)
        }
    }
}

/// Configuration de lancement.
/// - `-UITestMode` : backend en mémoire, réglages éphémères, animations coupées (tests XCUITest).
/// - `-UITestHappyHour` : Happy Hour forcé dès le lancement (mode test).
/// - `POS_SERVER_URL` (variable d'environnement) : force l'adresse du serveur.
/// - `-UITestPin 1234` : déverrouillage automatique (mode test uniquement).
/// - `-UITestSection floor` : écran affiché après connexion (mode test uniquement).
struct LaunchConfiguration {
    let isUITest: Bool
    let forceHappyHour: Bool
    let serverOverride: String?
    let autoPin: String?
    let initialSection: Router.Section?

    static let current: LaunchConfiguration = {
        let args = ProcessInfo.processInfo.arguments
        let defaults = UserDefaults.standard
        let isUITest = args.contains("-UITestMode")
        return LaunchConfiguration(
            isUITest: isUITest,
            forceHappyHour: args.contains("-UITestHappyHour"),
            serverOverride: ProcessInfo.processInfo.environment["POS_SERVER_URL"],
            autoPin: isUITest ? defaults.string(forKey: "UITestPin") : nil,
            initialSection: isUITest ? defaults.string(forKey: "UITestSection").flatMap(Router.Section.init(rawValue:)) : nil
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
            settings = TerminalSettings(defaults: defaults)
        } else {
            settings = TerminalSettings()
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
            return AppModel(api: api, settings: settings)
        }
        let url = settings.url ?? URL(string: "http://localhost:5080")!
        return AppModel(api: HTTPPosAPI(baseURL: url), settings: settings, realtimeBaseURL: url)
    }

    /// Applique une nouvelle adresse serveur : nouvelle session, nouvel état.
    func reconnect() {
        model.stopRealtime()
        model = AppEnvironment.makeModel(settings: settings, launch: launch)
        router.section = .order
        generation += 1
    }
}

@MainActor @Observable
final class Router {
    enum Section: String, CaseIterable, Identifiable {
        case order, floor, kitchen, fiscal, admin
        var id: String { rawValue }

        var title: String {
            switch self {
            case .order: "Caisse"
            case .floor: "Salle"
            case .kitchen: "Cuisine"
            case .fiscal: "Clôture"
            case .admin: "Gestion"
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
