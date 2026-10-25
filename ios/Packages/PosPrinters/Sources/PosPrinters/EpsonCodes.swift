import Foundation

/// Traduction des codes du SDK Epson ePOS en `PrinterSessionFailure`, sans dépendre du SDK (entiers).
/// Valeurs relevées dans `ePOS2.h` du SDK 2.37.1 ; un test sous `canImport(libepos2)` les compare au SDK.
public enum EpsonCodes {
    // Epos2ErrorStatus (retour de connect / sendData)
    static let errConnect: Int32 = 2, errTimeout: Int32 = 3, errNotFound: Int32 = 7, errInUse: Int32 = 8, errDeviceBusy: Int32 = 16
    static let errProcessing: Int32 = 6
    // Epos2CallbackCode (délégué de réception)
    public static let codeErrTimeout: Int32 = 1
    static let codeErrNotFound: Int32 = 2, codeErrCoverOpen: Int32 = 4, codeErrEmpty: Int32 = 7
    static let codeErrDeviceBusy: Int32 = 33, codeErrInUse: Int32 = 35, codeErrConnect: Int32 = 36
    /// Epos2PrinterSeries (SDK 2.37.1), par nom court tel que stocké dans `PrinterAddress.epson`.
    static let seriesValues: [String: Int32] = [
        "TM_M30": 1, "TM_T20": 6, "TM_T82": 10, "TM_T88": 12, "TM_M30II": 21, "TM_M50": 23, "TM_T88VII": 24, "TM_M30III": 29
    ]
    static let defaultSeries = "TM_T88"

    /// Code de retour d'un appel (`connect`, `sendData`) ; nil = succès.
    public static func failure(returnCode code: Int32) -> PrinterSessionFailure? {
        switch code {
        case 0: nil
        case errTimeout: .timeout
        case errConnect, errNotFound: .failed("Imprimante introuvable")
        case errInUse, errDeviceBusy: .failed("Imprimante occupée par un autre appareil")
        default: .failed("Erreur Epson (code \(code))")
        }
    }

    /// Code du délégué de réception ; nil = succès.
    public static func failure(callbackCode code: Int32) -> PrinterSessionFailure? {
        switch code {
        case 0: nil
        case codeErrTimeout: .timeout
        case codeErrNotFound, codeErrConnect: .failed("Imprimante introuvable")
        case codeErrInUse, codeErrDeviceBusy: .failed("Imprimante occupée par un autre appareil")
        case codeErrCoverOpen: .failed("Couvercle ouvert")
        case codeErrEmpty: .failed("Plus de papier")
        default: .failed("Erreur Epson (code \(code))")
        }
    }

    /// Série ePOS2 d'après le nom d'appareil de la recherche (« TM-T88VI », « TM-m30II-H »…) ; inconnu → TM_T88.
    /// TM-T88VI (ePOS2) et TM-T88III (réseau brut 9100) relèvent de TM_T88 ; le VII a sa série.
    public static func series(forDeviceName name: String) -> String {
        let key = name.uppercased().filter { $0.isLetter || $0.isNumber }
        // Du plus long au plus court : « M30III » avant « M30 », « T88VII » avant « T88 ».
        let known = ["M30III", "M30II", "M30", "M50", "T20", "T82", "T88VII", "T88"]
        guard let hit = known.first(where: { key.contains($0) }) else { return defaultSeries }
        return "TM_" + hit
    }

    /// Valeur `Epos2PrinterSeries` ; série inconnue → TM_T88.
    public static func seriesValue(_ series: String) -> Int32 { seriesValues[series] ?? seriesValues[defaultSeries]! }

    /// Rejoue `operation` tant qu'elle rend « en cours » (ERR_PROCESSING), au plus `attempts` fois, avec `pause` entre deux essais.
    /// La pause n'est pas annulable : sous annulation (délai du transport), les essais ne sont pas brûlés d'un coup.
    @discardableResult
    public static func retryWhileProcessing(attempts: Int = 20, pause: Duration = .milliseconds(100),
                                            isolation: isolated (any Actor)? = #isolation,
                                            _ operation: () -> Int32) async -> Int32 {
        var code = operation()
        var left = attempts - 1
        while code == errProcessing, left > 0 {
            left -= 1
            await Task { try? await Task.sleep(for: pause) }.value
            code = operation()
        }
        return code
    }
}
