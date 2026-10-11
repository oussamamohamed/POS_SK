import Foundation
import Testing
@testable import PosKit

@Suite("Répartition au plus fort reste")
struct OrderMathAllocationTests {
    @Test func totalEqualToTheSumReturnsTheWeights() {
        #expect(OrderMath.allocate(total: 3005, weights: [1000, 2000, 5]) == [1000, 2000, 5])
    }

    @Test func totalAboveTheSumIsCapped() {
        #expect(OrderMath.allocate(total: 9999, weights: [100, 200]) == [100, 200])
    }

    @Test(arguments: [
        (3, [1, 1, 1, 1, 1], [1, 1, 1, 0, 0]),
        (2, [1, 1, 1], [1, 1, 0]),
        (100, [100, 100, 100], [34, 33, 33]),
        (2700, [1000, 2000], [900, 1800]),
        (1, [3, 3], [1, 0]),
        (5, [0, 10, 0], [0, 5, 0]),
        (0, [10, 20], [0, 0]),
        (-5, [10, 20], [0, 0]),
        (50, [0, 0], [0, 0]),
    ])
    func knownCases(total: Int, weights: [Int], expected: [Int]) {
        #expect(OrderMath.allocate(total: total, weights: weights) == expected)
    }

    @Test func noGroupsGivesNothing() {
        #expect(OrderMath.allocate(total: 50, weights: []).isEmpty)
    }

    @Test func keepsEveryInvariantOnManyRandomCases() {
        var generator = SplitMix64(seed: 20261011)
        for index in 0..<5000 {
            let count = Int(generator.next() % 6) + 1
            let weights = (0..<count).map { _ in Int(generator.next() % 100_000) }
            let sum = weights.reduce(0, +)
            let total = sum == 0 ? 0 : Int(generator.next() % UInt64(sum + 1))
            let shares = OrderMath.allocate(total: total, weights: weights)
            let context = "case \(index): total=\(total) weights=\(weights)"
            #expect(shares.reduce(0, +) == min(total, sum), Comment(rawValue: context))
            for (share, weight) in zip(shares, weights) {
                #expect(share >= 0 && share <= weight, Comment(rawValue: context))
                let exact = sum == 0 ? 0 : Double(total) * Double(weight) / Double(sum)
                #expect(abs(Double(share) - exact) < 1.0 + 1e-9, Comment(rawValue: context))
            }
        }
    }
}
