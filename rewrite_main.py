import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

main_panel_regex = re.compile(r'Widget _buildMainPanel\(BuildContext context, PosTerminalState state\) \{.*?\n  \}', re.DOTALL)

new_main_panel = """Widget _buildMainPanel(BuildContext context, PosTerminalState state) {
    return Expanded(
      child: Column(
        children: [
          // Quick Keys
          Container(
            padding: const EdgeInsets.only(top: 16, bottom: 8, left: 16, right: 16),
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  _buildQuickKey("⚡", "Café Express (1.50€)"),
                  _buildQuickKey("⚡", "Croissant (1.80€)"),
                  _buildQuickKey("⚡", "Menu Midi (14.50€)"),
                ],
              ),
            ),
          ),
          
          // Category Tabs
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                _buildCatTab("Tous", true),
                _buildCatTab("Plats", false),
                _buildCatTab("Boissons", false),
                _buildCatTab("Desserts", false),
              ],
            ),
          ),
          
          Expanded(
            child: Container(
              margin: const EdgeInsets.all(16),
              padding: const EdgeInsets.all(8),
              decoration: BoxDecoration(
                color: const Color(0x730F172A),
                border: Border.all(color: borderColor),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Column(
                children: [
                  Expanded(
                    child: GridView.builder(
                      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: 5,
                        childAspectRatio: 0.9,
                        crossAxisSpacing: 8,
                        mainAxisSpacing: 8,
                      ),
                      itemCount: 15,
                      itemBuilder: (context, index) {
                        return InkWell(
                          onTap: () {
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
                                Text("Produit ${index+1}", 
                                  style: const TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 13),
                                  textAlign: TextAlign.center,
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                ),
                                Container(
                                  width: double.infinity,
                                  padding: const EdgeInsets.symmetric(vertical: 4),
                                  decoration: BoxDecoration(
                                    color: const Color(0x1E10B981),
                                    border: Border.all(color: const Color(0x4010B981)),
                                    borderRadius: BorderRadius.circular(2)
                                  ),
                                  child: Text("${((1050 + (index * 100))/100).toStringAsFixed(2)} €", 
                                    textAlign: TextAlign.center,
                                    style: const TextStyle(color: successColor, fontSize: 14, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono'),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                  
                  // Pagination
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
                  )
                ],
              ),
            ),
          )
        ],
      ),
    );
  }"""
  
if main_panel_regex.search(text):
    text = main_panel_regex.sub(new_main_panel, text)
    with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
        f.write(text)
    print("Replaced!")
else:
    print("Not found!")
