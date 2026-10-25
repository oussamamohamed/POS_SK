#if os(iOS) && canImport(libepos2)
import Testing
import libepos2
@testable import PosPrinters

/// Vérifie les copies d'`EpsonCodes` contre le SDK installé (ne tourne que sur simulateur iOS avec le SDK).
@Suite struct EpsonSdkConstantsTests {
    @Test func constantsMatchSdk() {
        #expect(EpsonCodes.errConnect == Int32(EPOS2_ERR_CONNECT.rawValue))
        #expect(EpsonCodes.errTimeout == Int32(EPOS2_ERR_TIMEOUT.rawValue))
        #expect(EpsonCodes.errNotFound == Int32(EPOS2_ERR_NOT_FOUND.rawValue))
        #expect(EpsonCodes.errInUse == Int32(EPOS2_ERR_IN_USE.rawValue))
        #expect(EpsonCodes.errDeviceBusy == Int32(EPOS2_ERR_DEVICE_BUSY.rawValue))
        #expect(EpsonCodes.codeErrTimeout == Int32(EPOS2_CODE_ERR_TIMEOUT.rawValue))
        #expect(EpsonCodes.codeErrNotFound == Int32(EPOS2_CODE_ERR_NOT_FOUND.rawValue))
        #expect(EpsonCodes.codeErrCoverOpen == Int32(EPOS2_CODE_ERR_COVER_OPEN.rawValue))
        #expect(EpsonCodes.codeErrEmpty == Int32(EPOS2_CODE_ERR_EMPTY.rawValue))
        #expect(EpsonCodes.codeErrDeviceBusy == Int32(EPOS2_CODE_ERR_DEVICE_BUSY.rawValue))
        #expect(EpsonCodes.codeErrInUse == Int32(EPOS2_CODE_ERR_IN_USE.rawValue))
        #expect(EpsonCodes.codeErrConnect == Int32(EPOS2_CODE_ERR_CONNECT.rawValue))
        let series: [String: Int32] = [
            "TM_M30": Int32(EPOS2_TM_M30.rawValue), "TM_T20": Int32(EPOS2_TM_T20.rawValue), "TM_T82": Int32(EPOS2_TM_T82.rawValue),
            "TM_T88": Int32(EPOS2_TM_T88.rawValue), "TM_M30II": Int32(EPOS2_TM_M30II.rawValue), "TM_M50": Int32(EPOS2_TM_M50.rawValue),
            "TM_T88VII": Int32(EPOS2_TM_T88VII.rawValue), "TM_M30III": Int32(EPOS2_TM_M30III.rawValue)
        ]
        #expect(EpsonCodes.seriesValues == series)
    }
}
#endif
