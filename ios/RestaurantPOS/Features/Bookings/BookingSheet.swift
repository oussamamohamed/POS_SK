import SwiftUI
import PosKit

/// Création, modification et actions (installer, absent, annuler, terminer) d'une réservation.
struct BookingSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    let target: BookingTarget
    @State private var draft = ReservationDraft(startsAt: Self.defaultStart())
    @State private var loaded = false

    private var existing: Reservation? {
        if case .existing(let r) = target { r } else { nil }
    }

    /// Prochaine heure ronde dans une heure.
    private static func defaultStart() -> Date {
        let t = Date().addingTimeInterval(3600).timeIntervalSinceReferenceDate
        return Date(timeIntervalSinceReferenceDate: (t / 300).rounded(.up) * 300)
    }

    private var isEditable: Bool { existing.map { $0.status == .booked } ?? true }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    TextField("bookings.field_name", text: $draft.guestName).accessibilityIdentifier("booking.name")
                    Stepper(value: $draft.covers, in: 1...30) {
                        Text("bookings.field_covers \(draft.covers)")
                    }
                    .accessibilityIdentifier("booking.covers")
                    DatePicker("bookings.field_date", selection: $draft.startsAt, displayedComponents: [.date, .hourAndMinute])
                        .accessibilityIdentifier("booking.start")
                    Picker("bookings.field_duration", selection: $draft.durationMinutes) {
                        ForEach([60, 90, 120, 150], id: \.self) { Text(Self.durationLabel($0)).tag($0) }
                    }
                    Toggle("bookings.field_waitlist", isOn: $draft.isWaitlist).accessibilityIdentifier("booking.waitlist")
                    if draft.isWaitlist {
                        Stepper(value: quoteBinding, in: 5...240, step: 5) {
                            Text("bookings.field_quote \(quoteBinding.wrappedValue)")
                        }
                        .accessibilityIdentifier("booking.quote")
                    }
                    if !draft.isWaitlist {
                        Picker("bookings.field_table", selection: $draft.tableNumber) {
                            Text("bookings.field_table_none").tag(String?.none)
                            ForEach(model.floor.diningTables) { Text("\($0.tableNumber) (\($0.capacity))").tag(Optional($0.tableNumber)) }
                        }
                        .accessibilityIdentifier("booking.table")
                    }
                }
                .disabled(!isEditable)
                Section {
                    TextField(phonePlaceholder, text: phoneBinding)
                        .keyboardType(.phonePad)
                        .accessibilityIdentifier("booking.phone")
                    TextField("bookings.field_note", text: noteBinding, axis: .vertical).accessibilityIdentifier("booking.note")
                } footer: {
                    Text("bookings.field_phone_hint")
                }
                .disabled(!isEditable)
                if let r = existing {
                    Section {
                        if r.status == .booked {
                            Button("bookings.btn_seat") { Task { if await model.bookings.seat(r, table: draft.tableNumber) { dismiss() } } }
                                .accessibilityIdentifier("booking.seat")
                            Button("bookings.btn_noshow") { Task { if await model.bookings.setStatus(r, .noShow) { dismiss() } } }
                                .accessibilityIdentifier("booking.noshow")
                            Button("bookings.btn_cancel", role: .destructive) { Task { if await model.bookings.setStatus(r, .cancelled) { dismiss() } } }
                                .accessibilityIdentifier("booking.cancel")
                        } else if r.status == .seated {
                            Button("bookings.btn_done") { Task { if await model.bookings.setStatus(r, .done) { dismiss() } } }
                                .accessibilityIdentifier("booking.done")
                        }
                    }
                }
            }
            .navigationTitle(existing == nil ? "bookings.form_new_title" : "bookings.form_edit_title")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                if isEditable {
                    ToolbarItem(placement: .confirmationAction) {
                        Button("common.save") {
                            var toSave = draft
                            if toSave.isWaitlist, toSave.quoteMinutes == nil { toSave.quoteMinutes = 15 }
                            Task { if await model.bookings.save(toSave, editing: existing?.id) != nil { dismiss() } }
                        }
                        .accessibilityIdentifier("booking.save")
                    }
                }
            }
            .onAppear {
                guard !loaded else { return }
                loaded = true
                if let r = existing { draft = ReservationDraft(r) }
            }
        }
    }

    /// Un non-responsable ne reçoit pas le numéro : le champ reste vide et ne l'écrase pas.
    private var phonePlaceholder: String {
        existing?.hasPhone == true && existing?.phone == nil ? String(localized: "bookings.field_phone_hidden") : String(localized: "bookings.field_phone")
    }
    private var phoneBinding: Binding<String> { Binding(get: { draft.phone ?? "" }, set: { draft.phone = $0 }) }
    /// Attente annoncée : 15 min par défaut pour un client en liste d'attente.
    private var quoteBinding: Binding<Int> { Binding(get: { draft.quoteMinutes ?? 15 }, set: { draft.quoteMinutes = $0 }) }
    private var noteBinding: Binding<String> { Binding(get: { draft.note ?? "" }, set: { draft.note = $0 }) }

    private static func durationLabel(_ minutes: Int) -> String {
        minutes % 60 == 0 ? "\(minutes / 60) h" : "\(minutes / 60) h \(minutes % 60)"
    }
}
