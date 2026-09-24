import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

# Add _currentPage to state
old_state = """class _PosTerminalPageState extends State<PosTerminalPage> {
  int _currentTabIndex = 0;"""
new_state = """class _PosTerminalPageState extends State<PosTerminalPage> {
  int _currentTabIndex = 0;
  int _currentPage = 1;
  final int _totalPages = 3;"""
text = text.replace(old_state, new_state)


# Fix the GridView to use NeverScrollableScrollPhysics
old_grid = """                  Expanded(
                    child: GridView.builder(
                      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: 5,
                        childAspectRatio: 0.9,
                        crossAxisSpacing: 8,
                        mainAxisSpacing: 8,
                      ),
                      itemCount: 15,"""
new_grid = """                  Expanded(
                    child: GridView.builder(
                      physics: const NeverScrollableScrollPhysics(),
                      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: 5,
                        childAspectRatio: 0.9,
                        crossAxisSpacing: 8,
                        mainAxisSpacing: 8,
                      ),
                      itemCount: 15,"""
text = text.replace(old_grid, new_grid)

# Also update the items displayed to change based on _currentPage so the user sees something change
old_item = """                          onTap: () {
                            context.read<PosTerminalBloc>().add(AddProductEvent(
                              productId: "p$index", productName: "Produit ${index+1}", unitPriceCents: 1050 + (index * 100)
                            ));
                          },
                          child: Container(
                            decoration: BoxDecoration(
                              color: panelColor,
                              border: Border.all(color: const Color(0x1EFFFFFF)),
                              borderRadius: BorderRadius.zero, 
                            ),
                            padding: const EdgeInsets.all(8),
                            child: Column(
                              mainAxisAlignment: MainAxisAlignment.spaceBetween,
                              children: [
                                const Align(alignment: Alignment.topRight, child: Text("BAR", style: TextStyle(fontSize: 10, color: textMuted))),
                                const Icon(Icons.fastfood, color: textMuted, size: 32),
                                const SizedBox(height: 4),
                                Text("Produit ${index+1}","""
new_item = """                          onTap: () {
                            int actualIndex = index + (_currentPage - 1) * 15;
                            context.read<PosTerminalBloc>().add(AddProductEvent(
                              productId: "p$actualIndex", productName: "Produit ${actualIndex+1}", unitPriceCents: 1050 + (actualIndex * 100)
                            ));
                          },
                          child: Container(
                            decoration: BoxDecoration(
                              color: panelColor,
                              border: Border.all(color: const Color(0x1EFFFFFF)),
                              borderRadius: BorderRadius.zero, 
                            ),
                            padding: const EdgeInsets.all(8),
                            child: Column(
                              mainAxisAlignment: MainAxisAlignment.spaceBetween,
                              children: [
                                const Align(alignment: Alignment.topRight, child: Text("BAR", style: TextStyle(fontSize: 10, color: textMuted))),
                                const Icon(Icons.fastfood, color: textMuted, size: 32),
                                const SizedBox(height: 4),
                                Text("Produit ${(index + (_currentPage - 1) * 15)+1}","""
text = text.replace(old_item, new_item)

old_price = """                                  child: Text("${((1050 + (index * 100))/100).toStringAsFixed(2)} €","""
new_price = """                                  child: Text("${((1050 + ((index + (_currentPage - 1) * 15) * 100))/100).toStringAsFixed(2)} €","""
text = text.replace(old_price, new_price)

# Fix the Pagination bar
old_pagination = """                  // Pagination
                  Container(
                    margin: const EdgeInsets.only(top: 12),
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                    height: 52,
                    decoration: BoxDecoration(
                      color: const Color(0xD91E293B),
                      border: Border.all(color: borderColor),
                      borderRadius: BorderRadius.circular(12)
                    ),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                          decoration: BoxDecoration(color: cardColor, borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                          child: const Row(children: [Icon(Icons.chevron_left, color: textColor, size: 18), Text(" Précédent", style: TextStyle(color: textColor, fontWeight: FontWeight.bold))]),
                        ),
                        Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            const Text("PAGE 1 / 3", style: TextStyle(color: textColor, fontSize: 12, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                            const SizedBox(height: 4),
                            Row(
                              children: [
                                Container(width: 10, height: 10, decoration: const BoxDecoration(color: primaryColor, shape: BoxShape.circle)),
                                const SizedBox(width: 6),
                                Container(width: 10, height: 10, decoration: const BoxDecoration(color: Color(0x33FFFFFF), shape: BoxShape.circle)),
                                const SizedBox(width: 6),
                                Container(width: 10, height: 10, decoration: const BoxDecoration(color: Color(0x33FFFFFF), shape: BoxShape.circle)),
                              ]
                            )
                          ]
                        ),
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                          decoration: BoxDecoration(color: cardColor, borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                          child: const Row(children: [Text("Suivant ", style: TextStyle(color: textColor, fontWeight: FontWeight.bold)), Icon(Icons.chevron_right, color: textColor, size: 18)]),
                        ),
                      ]
                    )
                  )"""

new_pagination = """                  // Pagination
                  Container(
                    margin: const EdgeInsets.only(top: 12),
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                    height: 52,
                    decoration: BoxDecoration(
                      color: const Color(0xD91E293B),
                      border: Border.all(color: borderColor),
                      borderRadius: BorderRadius.circular(12)
                    ),
                    child: Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        Material(
                          color: Colors.transparent,
                          child: InkWell(
                            onTap: _currentPage > 1 ? () {
                              setState(() {
                                _currentPage--;
                              });
                            } : null,
                            borderRadius: BorderRadius.circular(8),
                            child: Ink(
                              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                              decoration: BoxDecoration(color: _currentPage > 1 ? cardColor : const Color(0x330F172A), borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                              child: Row(children: [Icon(Icons.chevron_left, color: _currentPage > 1 ? textColor : textMuted, size: 18), Text(" Précédent", style: TextStyle(color: _currentPage > 1 ? textColor : textMuted, fontWeight: FontWeight.bold))]),
                            ),
                          ),
                        ),
                        Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Text("PAGE $_currentPage / $_totalPages", style: const TextStyle(color: textColor, fontSize: 12, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                            const SizedBox(height: 4),
                            Row(
                              children: List.generate(_totalPages, (i) {
                                return Container(
                                  margin: EdgeInsets.only(right: i < _totalPages - 1 ? 6 : 0),
                                  width: 10, height: 10,
                                  decoration: BoxDecoration(
                                    color: (i + 1) == _currentPage ? primaryColor : const Color(0x33FFFFFF),
                                    shape: BoxShape.circle
                                  )
                                );
                              }),
                            )
                          ]
                        ),
                        Material(
                          color: Colors.transparent,
                          child: InkWell(
                            onTap: _currentPage < _totalPages ? () {
                              setState(() {
                                _currentPage++;
                              });
                            } : null,
                            borderRadius: BorderRadius.circular(8),
                            child: Ink(
                              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                              decoration: BoxDecoration(color: _currentPage < _totalPages ? cardColor : const Color(0x330F172A), borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                              child: Row(children: [Text("Suivant ", style: TextStyle(color: _currentPage < _totalPages ? textColor : textMuted, fontWeight: FontWeight.bold)), Icon(Icons.chevron_right, color: _currentPage < _totalPages ? textColor : textMuted, size: 18)]),
                            ),
                          ),
                        ),
                      ]
                    )
                  )"""
text = text.replace(old_pagination, new_pagination)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

