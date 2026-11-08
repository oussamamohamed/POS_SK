import Foundation
import Testing
@testable import PosKit

/// Rejoue `insights_reference.json`, généré par le .NET (`WRITE_INSIGHTS_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter InsightsReferenceFixtureTests`).
@Suite struct InsightsMathTests {
    struct Row: Decodable { let current: Int, reference: Int, delta: String }

    @Test func deltaPercentMatchesTheServer() throws {
        let url = try #require(Bundle.module.url(forResource: "insights_reference", withExtension: "json", subdirectory: "Fixtures"))
        let rows = try JSONDecoder().decode([Row].self, from: Data(contentsOf: url))
        #expect(!rows.isEmpty)
        for r in rows {
            #expect(InsightsMath.deltaPercent(current: r.current, reference: r.reference) == Decimal(string: r.delta), "\(r.current)/\(r.reference)")
        }
    }
}
