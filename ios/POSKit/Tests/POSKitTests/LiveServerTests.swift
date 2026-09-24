import Foundation
import Testing
@testable import POSKit

/// Scénario de bout en bout contre un vrai serveur `RestaurantPos.Api` (données de démo, PIN 1234).
///
///     POS_API_URL=http://127.0.0.1:5080 swift test --filter LiveServer
///
/// Ignoré si `POS_API_URL` n'est pas défini (CI iOS, poste développeur sans serveur).
@MainActor
@Suite("Serveur réel", .enabled(if: ProcessInfo.processInfo.environment["POS_API_URL"] != nil))
struct LiveServerTests {
    private func liveApp() throws -> AppModel {
        let address = try #require(ProcessInfo.processInfo.environment["POS_API_URL"])
        let settings = TerminalSettings(serverAddress: address, terminalId: "IPAD_LIVE_\(Int.random(in: 1000...9999))", demoMode: false)
        return AppModel(settingsStore: InMemorySettingsStore(settings))
    }

    @Test func fullServiceAgainstRealServer() async throws {
        let app = try liveApp()
        #expect(try await app.context.api.health())
        for digit in [1, 2, 3, 4] { await app.pinDigit(digit) }
        #expect(app.isUnlocked, "PIN 1234 refusé : \(app.pinError ?? "?")")

        await app.catalog.load()
        let burger = try #require(app.catalog.products.first { $0.hasModifiers })
        let drink = try #require(app.catalog.products.first { !$0.hasModifiers && $0.taxRatePercent == 20 })

        await app.floor.load()
        let table = try #require(app.floor.tables.first { $0.status == .free })
        #expect(await app.floor.open(table, covers: 2))
        await app.openTable(table.tableNumber)

        let selection = ModifierSelection(product: burger)
        app.order.add(burger, modifiers: selection.selectedOptions, note: "Test iPad")
        app.order.add(drink)
        let expected = app.order.totalDue
        await app.order.sendToKitchen()
        #expect(app.order.pending.isEmpty)
        #expect(app.order.serverLines.allSatisfy { $0.isDispatched })
        #expect(app.order.totalDue == expected, "Totaux iPad et serveur divergents")

        await app.kitchen.load()
        #expect(app.kitchen.tickets.contains { $0.tableNumber.hasSuffix(table.tableNumber) })

        #expect(await app.order.prepareCheckout())
        let checkout = CheckoutModel(context: .table(table.tableNumber), order: try #require(app.order.order), destination: .eatIn, posContext: app.context)
        checkout.split(into: 2)
        checkout.method = .creditCard
        await checkout.submit()
        #expect(checkout.remaining.isPositive)
        checkout.method = .cash
        checkout.entry.set(checkout.amountDue + Money(cents: 500))
        await checkout.submit()
        #expect(checkout.isCompleted)

        await app.floor.load()
        #expect(app.floor.tables.first { $0.tableNumber == table.tableNumber }?.status == .free)

        await app.openCounter()
        app.order.add(drink)
        #expect(await app.order.hold(label: "Live"))
        await app.held.load()
        let held = try #require(app.held.orders.first { $0.customerLabel == "Live" })
        #expect(await app.order.recall(held))
        #expect(await app.order.prepareCheckout())
        let counter = CheckoutModel(context: .counter, order: try #require(app.order.order), destination: app.order.destination, posContext: app.context)
        counter.method = .cash
        counter.entry.set(Money(cents: 2000))
        await counter.submit()
        guard case .completed(let receipt) = counter.phase else {
            Issue.record("Vente comptoir non terminée")
            return
        }
        // Format serveur : `#<préfixe du terminal>-NN` (ex. `#A-01`).
        let pickup = try #require(receipt.pickupNumber)
        #expect(pickup.hasPrefix("#") && pickup.contains("-") && pickup.suffix(2).allSatisfy { $0.isNumber })

        await app.fiscal.load()
        #expect((app.fiscal.xReport?.receiptCount ?? 0) >= 3)
    }
}
