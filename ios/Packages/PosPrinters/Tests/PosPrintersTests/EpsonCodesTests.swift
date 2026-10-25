import Testing
@testable import PosPrinters

@Suite struct EpsonCodesTests {
    // Codes de retour des appels connect/sendData (Epos2ErrorStatus).
    @Test func returnCodes() {
        #expect(EpsonCodes.failure(returnCode: 0) == nil)
        #expect(EpsonCodes.failure(returnCode: 3) == .timeout)
        #expect(EpsonCodes.failure(returnCode: 2) == .failed("Imprimante introuvable"))
        #expect(EpsonCodes.failure(returnCode: 7) == .failed("Imprimante introuvable"))
        #expect(EpsonCodes.failure(returnCode: 8) == .failed("Imprimante occupée par un autre appareil"))
        #expect(EpsonCodes.failure(returnCode: 16) == .failed("Imprimante occupée par un autre appareil"))
        #expect(EpsonCodes.failure(returnCode: 1) == .failed("Erreur Epson (code 1)"))
    }

    // Codes du délégué de réception (Epos2CallbackCode).
    @Test func callbackCodes() {
        #expect(EpsonCodes.failure(callbackCode: 0) == nil)
        #expect(EpsonCodes.failure(callbackCode: 1) == .timeout)
        #expect(EpsonCodes.failure(callbackCode: 2) == .failed("Imprimante introuvable"))
        #expect(EpsonCodes.failure(callbackCode: 36) == .failed("Imprimante introuvable"))
        #expect(EpsonCodes.failure(callbackCode: 33) == .failed("Imprimante occupée par un autre appareil"))
        #expect(EpsonCodes.failure(callbackCode: 35) == .failed("Imprimante occupée par un autre appareil"))
        #expect(EpsonCodes.failure(callbackCode: 4) == .failed("Couvercle ouvert"))
        #expect(EpsonCodes.failure(callbackCode: 7) == .failed("Plus de papier"))
        #expect(EpsonCodes.failure(callbackCode: 5) == .failed("Erreur Epson (code 5)"))
    }

    @Test(arguments: [
        ("TM-T88VI", "TM_T88"), ("TM-T88V", "TM_T88"), ("TM-T88VII", "TM_T88VII"),
        ("TM-m30", "TM_M30"), ("TM-m30II", "TM_M30II"), ("TM-m30II-H", "TM_M30II"), ("TM-m30III", "TM_M30III"),
        ("TM-m50", "TM_M50"), ("TM-T20III", "TM_T20"), ("TM-T82", "TM_T82"),
        ("TM-T88III", "TM_T88"), ("Inconnue", "TM_T88"), ("", "TM_T88")
    ])
    func seriesByName(name: String, expected: String) {
        #expect(EpsonCodes.series(forDeviceName: name) == expected)
    }

    @Test func seriesValues() {
        #expect(EpsonCodes.seriesValue("TM_T88") == 12)
        #expect(EpsonCodes.seriesValue("TM_T88VII") == 24)
        #expect(EpsonCodes.seriesValue("TM_M30") == 1)
        #expect(EpsonCodes.seriesValue("???") == 12) // inconnu → TM_T88
    }
}

@Suite struct EpsonCallbackGateTests {
    @Test func finishBeforeAndAfterWait() async {
        let early = EpsonCallbackGate()
        early.finish(7); early.finish(9) // idempotent : le premier gagne
        #expect(await early.result(onCancel: 1) == 7)
        let late = EpsonCallbackGate()
        async let value = late.result(onCancel: 1)
        try? await Task.sleep(for: .milliseconds(50))
        late.finish(5)
        #expect(await value == 5)
    }

    @Test func cancellationResumesWithTimeoutCode() async {
        let gate = EpsonCallbackGate()
        let task = Task { await gate.result(onCancel: 1) }
        try? await Task.sleep(for: .milliseconds(50))
        task.cancel()
        #expect(await task.value == 1)
    }

    @Test func closeStyleFinishResumesPendingWait() async {
        let gate = EpsonCallbackGate()
        async let value = gate.result(onCancel: 1)
        try? await Task.sleep(for: .milliseconds(50))
        gate.finish(1)
        gate.finish(1)
        #expect(await value == 1)
    }

    @Test func retryIsBoundedAndStopsOnSuccess() async {
        nonisolated(unsafe) var calls = 0
        let code = await EpsonCodes.retryWhileProcessing(attempts: 3, pause: .milliseconds(1)) { calls += 1; return 6 }
        #expect(code == 6 && calls == 3)
        calls = 0
        let ok = await EpsonCodes.retryWhileProcessing(attempts: 5, pause: .milliseconds(1)) { calls += 1; return calls < 3 ? 6 : 0 }
        #expect(ok == 0 && calls == 3)
    }

    @Test func retryKeepsPauseWhenCancelled() async {
        let task = Task {
            nonisolated(unsafe) var calls = 0
            let start = ContinuousClock.now
            await EpsonCodes.retryWhileProcessing(attempts: 4, pause: .milliseconds(30)) { calls += 1; return 6 }
            return ContinuousClock.now - start
        }
        task.cancel()
        #expect(await task.value >= .milliseconds(80)) // 3 pauses de 30 ms malgré l'annulation
    }
}
