import Foundation
import Observation

/// Racine de l'état de l'application : assemble l'API, les stores et le temps réel.
@MainActor @Observable
public final class AppModel {
    public let api: PosAPI
    public let settings: TerminalSettings
    public let notifier: Notifier
    public let session: SessionStore
    public let catalog: CatalogStore
    public let happyHour: HappyHourStore
    public let ticket: TicketStore
    public let floor: FloorStore
    public let kitchen: KitchenStore
    public let fiscal: FiscalStore
    public let catalogAdmin: CatalogAdminStore
    public let staff: StaffStore
    public let printers: PrinterStore
    public let gridEditor: GridEditorStore
    public let happyHourAdmin: HappyHourAdminStore
    public let dashboard: DashboardStore
    public let network: NetworkStore

    public private(set) var isRealtimeConnected = false
    public private(set) var isBootstrapped = false

    @ObservationIgnored private var realtime: [SignalRConnection] = []
    @ObservationIgnored private var realtimeTasks: [Task<Void, Never>] = []
    @ObservationIgnored private let realtimeBaseURL: URL?

    public init(api: PosAPI, settings: TerminalSettings, realtimeBaseURL: URL? = nil) {
        self.api = api
        self.settings = settings
        self.realtimeBaseURL = realtimeBaseURL
        let notifier = Notifier()
        self.notifier = notifier
        let session = SessionStore(api: api)
        self.session = session
        let terminal: @MainActor () -> String = { settings.terminalId }
        let catalog = CatalogStore(api: api, notifier: notifier)
        self.catalog = catalog
        let happyHour = HappyHourStore(api: api, notifier: notifier, terminalId: terminal)
        self.happyHour = happyHour
        ticket = TicketStore(api: api, notifier: notifier, happyHour: happyHour, session: session, terminalId: terminal)
        floor = FloorStore(api: api, notifier: notifier, session: session)
        kitchen = KitchenStore(api: api, notifier: notifier)
        fiscal = FiscalStore(api: api, notifier: notifier, session: session, settings: settings)
        catalogAdmin = CatalogAdminStore(api: api, notifier: notifier, catalog: catalog)
        staff = StaffStore(api: api, notifier: notifier)
        printers = PrinterStore(api: api, notifier: notifier)
        gridEditor = GridEditorStore(api: api, notifier: notifier, catalog: catalog)
        happyHourAdmin = HappyHourAdminStore(api: api, notifier: notifier, happyHour: happyHour)
        dashboard = DashboardStore(api: api, notifier: notifier)
        network = NetworkStore(api: api, notifier: notifier)
    }

    /// Chargement initial après le premier déverrouillage.
    public func bootstrap() async {
        async let catalogLoad: Void = catalog.load()
        async let hh: Void = happyHour.refresh()
        async let tables: Void = floor.load()
        async let net: Void = network.refresh()
        _ = await (catalogLoad, hh, tables, net)
        if ticket.orderId == nil && ticket.lines.isEmpty {
            await ticket.openCounter(destination: .takeaway)
        }
        isBootstrapped = true
        startRealtime()
    }

    // MARK: - Temps réel

    public func startRealtime() {
        guard realtime.isEmpty, let base = realtimeBaseURL else { return }
        let session = self.session
        let hubs = ["hubs/kitchen", "hubs/pos"].map { path in
            SignalRConnection(hubURL: base.appendingPathComponent(path)) { await session.token }
        }
        realtime = hubs
        for hub in hubs {
            hub.start()
            realtimeTasks.append(Task { [weak self] in
                for await event in hub.events {
                    await self?.handle(event)
                }
            })
        }
    }

    public func stopRealtime() {
        realtime.forEach { $0.stop() }
        realtimeTasks.forEach { $0.cancel() }
        realtime = []
        realtimeTasks = []
    }

    public func handle(_ event: RealtimeEvent) async {
        switch event {
        case .kitchenChanged:
            await kitchen.load()
        case .gridLayoutUpdated(let layout):
            if let layout { catalog.apply(layout: layout) } else { catalog.invalidateLayouts(); await catalog.loadCurrentLayout() }
        case .happyHourChanged(let status):
            if let status { happyHour.apply(status) }
            await happyHour.refresh()
        case .tablesChanged:
            await floor.load()
        case .connectionChanged(let connected):
            isRealtimeConnected = connected
        }
    }
}
