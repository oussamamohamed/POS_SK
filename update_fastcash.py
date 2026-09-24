import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    page = f.read()

# I want to add Fast Cash buttons between the TTC row and the Action rows.
# Currently they look like this:
old_ttc = """                  children: [
                    Text("Total à Payer TTC :", style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.bold)),
                    Text("${(state.totalTtcCents / 100).toStringAsFixed(2)} €", style: TextStyle(color: successColor, fontSize: 22, fontWeight: FontWeight.bold)),
                  ],
                ),
                const SizedBox(height: 16),
                
                // ACTION BUTTONS"""

new_ttc = """                  children: [
                    Text("Total à Payer TTC :", style: TextStyle(color: Colors.white, fontSize: 18, fontWeight: FontWeight.bold)),
                    Text("${(state.totalTtcCents / 100).toStringAsFixed(2)} €", style: TextStyle(color: successColor, fontSize: 22, fontWeight: FontWeight.bold)),
                  ],
                ),
                const SizedBox(height: 12),
                
                // FAST CASH
                Row(
                  children: [
                    Expanded(child: _buildFastCashBtn(context, "10 €")),
                    const SizedBox(width: 8),
                    Expanded(child: _buildFastCashBtn(context, "20 €")),
                    const SizedBox(width: 8),
                    Expanded(child: _buildFastCashBtn(context, "50 €")),
                    const SizedBox(width: 8),
                    Expanded(child: _buildFastCashBtn(context, "Exact")),
                  ],
                ),
                
                const SizedBox(height: 16),
                
                // ACTION BUTTONS"""

if old_ttc in page:
    page = page.replace(old_ttc, new_ttc)

# Inject _buildFastCashBtn
builder = """
  Widget _buildFastCashBtn(BuildContext context, String label) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          context.read<PosTerminalBloc>().add(ClearCartEvent());
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Paiement rapide: $label")));
        },
        borderRadius: BorderRadius.circular(8),
        child: Ink(
          padding: const EdgeInsets.symmetric(vertical: 8),
          decoration: BoxDecoration(color: const Color(0xFF1E293B), borderRadius: BorderRadius.circular(8)),
          child: Center(child: Text(label, style: const TextStyle(color: Color(0xFFE2E8F0), fontWeight: FontWeight.bold, fontSize: 13))),
        ),
      ),
    );
  }

  Widget _buildCartAction"""

page = page.replace("  Widget _buildCartAction", builder)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(page)
