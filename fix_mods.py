import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    page = f.read()

old_add = """                          onTap: () {
                            int actualIndex = index + (_currentPage - 1) * 15;
                            context.read<PosTerminalBloc>().add(AddProductEvent(
                              productId: "p$actualIndex", productName: "Produit ${actualIndex+1}", unitPriceCents: 1050 + (actualIndex * 100)
                            ));
                          },"""

new_add = """                          onTap: () {
                            int actualIndex = index + (_currentPage - 1) * 15;
                            if (actualIndex % 3 == 0) {
                              _showModifiersDialog(context, "p$actualIndex", "Produit ${actualIndex+1}", 1050 + (actualIndex * 100));
                            } else {
                              context.read<PosTerminalBloc>().add(AddProductEvent(
                                productId: "p$actualIndex", productName: "Produit ${actualIndex+1}", unitPriceCents: 1050 + (actualIndex * 100)
                              ));
                            }
                          },"""

page = page.replace(old_add, new_add)

dialog_code = """
  void _showModifiersDialog(BuildContext context, String pId, String pName, int price) {
    showDialog(
      context: context,
      builder: (BuildContext ctx) {
        return AlertDialog(
          backgroundColor: const Color(0xFF1E293B),
          title: Text("⚙️ Modificateurs - $pName", style: const TextStyle(color: Colors.white)),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text("Cuisson :", style: TextStyle(color: Color(0xFF94A3B8))),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  ElevatedButton(onPressed: () {}, style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF0F172A)), child: const Text("Saignant")),
                  ElevatedButton(onPressed: () {}, style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF0F172A)), child: const Text("À point")),
                ],
              ),
              const SizedBox(height: 16),
              const Text("Note Cuisine :", style: TextStyle(color: Color(0xFF94A3B8))),
              const SizedBox(height: 8),
              const TextField(
                style: TextStyle(color: Colors.white),
                decoration: InputDecoration(
                  hintText: "Ex: sans sauce...",
                  hintStyle: TextStyle(color: Color(0xFF475569)),
                  filled: true,
                  fillColor: Color(0xFF0F172A),
                  border: OutlineInputBorder(borderSide: BorderSide.none),
                ),
              )
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(),
              child: const Text("Annuler", style: TextStyle(color: Color(0xFF94A3B8))),
            ),
            ElevatedButton(
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF10B981)),
              onPressed: () {
                context.read<PosTerminalBloc>().add(AddProductEvent(productId: pId, productName: pName, unitPriceCents: price));
                Navigator.of(ctx).pop();
              },
              child: const Text("Valider", style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
            ),
          ],
        );
      },
    );
  }

  Widget _buildPlaceholderPage"""

page = page.replace("  Widget _buildPlaceholderPage", dialog_code)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(page)
