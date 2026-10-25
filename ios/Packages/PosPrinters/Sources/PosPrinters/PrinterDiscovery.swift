import Foundation
import os
import PosKit

/// Imprimante trouvée par une recherche SDK.
public struct DiscoveredPrinter: Equatable, Sendable {
    public let transport: PrinterTransportKind
    /// Modèle lu par le SDK, ex. « TSP143IV ».
    public let name: String
    /// Au format de `PrinterAddress.deviceIdentifier`.
    public let deviceIdentifier: String
    /// Pour une imprimante réseau.
    public let ipAddress: String?

    public init(transport: PrinterTransportKind, name: String, deviceIdentifier: String, ipAddress: String?) {
        self.transport = transport
        self.name = name
        self.deviceIdentifier = deviceIdentifier
        self.ipAddress = ipAddress
    }
}

public protocol PrinterDiscovery: Sendable {
    /// Recherche pendant `timeout` ; rend les résultats dans l'ordre d'arrivée. Le dédoublonnage (même `deviceIdentifier`)
    /// est fait par `CombinedPrinterDiscovery`, pas par chaque source.
    func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter]
}

/// Fusionne plusieurs recherches (lancées en parallèle, résultats dans l'ordre des sources) ;
/// l'échec d'une source n'empêche pas les autres : elle est ignorée et journalisée.
public struct CombinedPrinterDiscovery: PrinterDiscovery {
    private let sources: [any PrinterDiscovery]
    private static let log = Logger(subsystem: "PosPrinters", category: "discovery")

    public init(_ sources: [any PrinterDiscovery]) { self.sources = sources }

    public func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter] {
        let results = await withTaskGroup(of: (Int, [DiscoveredPrinter]).self) { group in
            for (index, source) in sources.enumerated() {
                group.addTask {
                    do { return (index, try await source.discover(timeout: timeout)) } catch {
                        Self.log.error("Recherche d'imprimantes en échec : \(error.localizedDescription)")
                        return (index, [])
                    }
                }
            }
            var all: [(Int, [DiscoveredPrinter])] = []
            for await result in group { all.append(result) }
            return all.sorted { $0.0 < $1.0 }.flatMap(\.1)
        }
        var seen = Set<String>()
        return results.filter { seen.insert($0.deviceIdentifier).inserted }
    }
}
