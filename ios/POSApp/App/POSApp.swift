import POSKit
import SwiftUI
import UIKit

@main
struct POSApp: App {
    @State private var app: AppModel

    init() {
        let arguments = ProcessInfo.processInfo.arguments
        if arguments.contains("-uiTesting") {
            // Tests UI : serveur simulé déterministe, réglages en mémoire, animations coupées.
            let backend = InMemoryPOSBackend()
            let settings = TerminalSettings(terminalId: "IPAD_UITEST", demoMode: true)
            _app = State(initialValue: AppModel(
                settingsStore: InMemorySettingsStore(settings),
                makeAPI: { _ in backend }
            ))
            UIView.setAnimationsEnabled(false)
        } else {
            _app = State(initialValue: AppModel(settingsStore: UserDefaultsSettingsStore()))
        }
    }

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(app)
        }
    }
}
