import SwiftUI
import PosKit

/// Grille de pastilles à cocher, une par allergène (bits de `Allergens`).
struct AllergenGrid: View {
    @Binding var mask: Int
    var isDisabled = false

    var body: some View {
        LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: Theme.Space.s)], spacing: Theme.Space.s) {
            ForEach(Array(Allergens.names.enumerated()), id: \.offset) { bit, name in
                let isOn = mask & (1 << bit) != 0
                Button {
                    Haptics.tap()
                    mask ^= 1 << bit
                } label: {
                    Text(LocalizedStringKey("allergen.\(name)"))
                        .font(.posLabel)
                        .foregroundStyle(isOn ? Theme.onPrimary : Theme.ink)
                        .frame(maxWidth: .infinity, minHeight: Theme.touchMin)
                        .background(Capsule().fill(isOn ? Theme.primary : Theme.raised))
                }
                .buttonStyle(.plain)
                .disabled(isDisabled)
                .accessibilityAddTraits(isOn ? .isSelected : [])
                .accessibilityIdentifier("product.allergens.\(name)")
            }
        }
        .opacity(isDisabled ? 0.4 : 1)
    }
}

/// Filtre « éviter ces allergènes » de l'écran de vente.
struct AllergenFilterSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        @Bindable var catalog = model.catalog
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: Theme.Space.l) {
                    Text("order.allergen_filter_hint").font(.posCaption).foregroundStyle(Theme.inkMuted)
                    AllergenGrid(mask: $catalog.avoidedAllergens)
                }
                .padding(Theme.Space.l)
            }
            .navigationTitle("order.allergen_filter_title")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("order.allergen_filter_clear") { catalog.avoidedAllergens = 0 }
                        .accessibilityIdentifier("allergen.filter.clear")
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("order.allergen_filter_done") { dismiss() }.accessibilityIdentifier("allergen.filter.done")
                }
            }
        }
        .presentationDetents([.medium, .large])
    }
}

/// Section de l'éditeur de produit : « renseignés » + cases d'allergènes.
struct AllergenEditorSection: View {
    @Binding var declared: Bool
    @Binding var mask: Int

    var body: some View {
        Section("admin.allergens_title") {
            Toggle("admin.allergens_declared_label", isOn: $declared).accessibilityIdentifier("product.allergens.declared")
            AllergenGrid(mask: $mask, isDisabled: !declared)
        }
    }
}
