import Foundation
import Testing
@testable import PosKit

@Suite("Suivi de l'état des imprimantes")
struct PrinterStatusTrackerTests {
    private let printer = UUID()
    private let t0 = Date(timeIntervalSince1970: 1_760_000_000)

    @Test func unknownUntilTheFirstSend() {
        let tracker = PrinterStatusTracker()
        #expect(tracker.get(printer) == PrinterState(isOnline: nil, sinceUtc: nil))
    }

    @Test func firstSuccessIsSilent() {
        var tracker = PrinterStatusTracker()
        let r1 = tracker.record(printer, success: true, at: t0)
        #expect(r1 == false)
        #expect(tracker.get(printer) == PrinterState(isOnline: true, sinceUtc: t0))
    }

    @Test func firstFailureIsNotified() {
        var tracker = PrinterStatusTracker()
        let r2 = tracker.record(printer, success: false, at: t0)
        #expect(r2)
        #expect(tracker.get(printer) == PrinterState(isOnline: false, sinceUtc: t0))
    }

    @Test func transitionsAreNotifiedAndRepeatsAreNot() {
        var tracker = PrinterStatusTracker()
        let t1 = t0.addingTimeInterval(10), t2 = t0.addingTimeInterval(20), t3 = t0.addingTimeInterval(30)
        _ = tracker.record(printer, success: true, at: t0)
        let r3 = tracker.record(printer, success: true, at: t1)
        #expect(r3 == false)
        #expect(tracker.get(printer).sinceUtc == t0) // une répétition ne déplace pas « depuis »
        let r4 = tracker.record(printer, success: false, at: t2)
        #expect(r4)
        let r5 = tracker.record(printer, success: false, at: t3)
        #expect(r5 == false)
        #expect(tracker.get(printer) == PrinterState(isOnline: false, sinceUtc: t2))
        let r6 = tracker.record(printer, success: true, at: t3)
        #expect(r6)
        #expect(tracker.get(printer) == PrinterState(isOnline: true, sinceUtc: t3))
        #expect(tracker.get(UUID()).isOnline == nil)
    }
}
