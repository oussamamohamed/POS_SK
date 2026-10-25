import PosKit
import Testing
@testable import PosPrinters

@Suite struct PrinterAddressTests {
    private func printer(_ kind: PrinterTransportKind, _ id: String?) -> Printer {
        Printer(name: "P", ipAddress: "10.0.0.9", transport: kind, deviceIdentifier: id)
    }

    @Test(arguments: [
        ("bluetooth:00:11:62:AA:BB:CC", StarInterface.bluetooth, "00:11:62:AA:BB:CC"),
        ("bluetoothLE:ABC-123", .bluetoothLE, "ABC-123"),
        ("usb:SN42", .usb, "SN42"),
        ("lan:192.168.1.50", .lan, "192.168.1.50")
    ])
    func starInterfaces(raw: String, interface: StarInterface, identifier: String) throws {
        #expect(try PrinterAddress.parse(printer(.star, raw)) == .star(interface: interface, identifier: identifier))
    }

    @Test func starDefaultsToLanOnIp() throws {
        #expect(try PrinterAddress.parse(printer(.star, nil)) == .star(interface: .lan, identifier: "10.0.0.9"))
        #expect(try PrinterAddress.parse(printer(.star, "")) == .star(interface: .lan, identifier: "10.0.0.9"))
    }

    @Test(arguments: ["serial:123", "nocolon", "usb:"])
    func starInvalid(raw: String) {
        #expect(throws: PrinterTransportError.connection("Identifiant d'imprimante invalide : \(raw)")) {
            try PrinterAddress.parse(printer(.star, raw))
        }
    }

    @Test func epsonSeriesAndTarget() throws {
        #expect(try PrinterAddress.parse(printer(.epson, "TM_M30;TCP:192.168.1.60")) == .epson(series: "TM_M30", target: "TCP:192.168.1.60"))
        #expect(try PrinterAddress.parse(printer(.epson, "TM_T20;BT:00:11:22")) == .epson(series: "TM_T20", target: "BT:00:11:22"))
    }

    @Test func epsonTargetAloneUsesTmT88() throws {
        #expect(try PrinterAddress.parse(printer(.epson, "USB:ABC")) == .epson(series: "TM_T88", target: "USB:ABC"))
    }

    @Test func epsonDefaultsToTcpOnIp() throws {
        #expect(try PrinterAddress.parse(printer(.epson, nil)) == .epson(series: "TM_T88", target: "TCP:10.0.0.9"))
        #expect(try PrinterAddress.parse(printer(.epson, "")) == .epson(series: "TM_T88", target: "TCP:10.0.0.9"))
    }

    @Test(arguments: ["192.168.1.60", "TM_M30;192.168.1.60", ";TCP:1.2.3.4", "TCP:"])
    func epsonInvalid(raw: String) {
        #expect(throws: PrinterTransportError.connection("Identifiant d'imprimante invalide : \(raw)")) {
            try PrinterAddress.parse(printer(.epson, raw))
        }
    }

    @Test func roundTrip() throws {
        for address in [PrinterAddress.star(interface: .bluetooth, identifier: "00:11:62:AA:BB:CC"),
                        .star(interface: .lan, identifier: "10.0.0.1"),
                        .epson(series: "TM_M30", target: "TCP:192.168.1.60")] {
            let kind: PrinterTransportKind = if case .star = address { .star } else { .epson }
            #expect(try PrinterAddress.parse(printer(kind, address.deviceIdentifier)) == address)
        }
    }

    @Test func lanTransportIsRejected() {
        #expect(throws: PrinterTransportError.self) { try PrinterAddress.parse(printer(.lan, nil)) }
    }
}
