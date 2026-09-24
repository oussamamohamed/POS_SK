import os

with open("src/RestaurantPos.Client.Flutter/lib/presentation/widgets/cart_swipe_item.dart", "w") as f:
    f.write("""import 'package:flutter/material.dart';
import '../../domain/entities/order_item.dart';

class CartSwipeItem extends StatelessWidget {
  final OrderItem item;
  final VoidCallback onDelete;
  final VoidCallback onAdd;
  final VoidCallback onRemove;

  const CartSwipeItem({
    super.key, 
    required this.item, 
    required this.onDelete,
    required this.onAdd,
    required this.onRemove,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: const Color(0x08FFFFFF), // rgba(255, 255, 255, 0.03)
        border: Border.all(color: const Color(0x14FFFFFF)),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        item.productName, 
                        style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: Color(0xFFF8FAFC)),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: 4),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                      decoration: BoxDecoration(
                        color: const Color(0x33F59E0B),
                        border: Border.all(color: const Color(0x66F59E0B)),
                        borderRadius: BorderRadius.circular(4),
                      ),
                      child: const Text("➕ Nouveau", style: TextStyle(color: Color(0xFFFBBF24), fontSize: 10, fontWeight: FontWeight.bold)),
                    )
                  ]
                ),
                const SizedBox(height: 4),
                Text('\${(item.unitPriceCents / 100).toStringAsFixed(2)} € × \${item.quantity}', 
                     style: const TextStyle(color: Color(0xFF94A3B8), fontSize: 12)),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              _buildQtyButton("-", onRemove),
              Container(
                width: 24,
                alignment: Alignment.center,
                child: Text("\${item.quantity}", style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Colors.white)),
              ),
              _buildQtyButton("+", onAdd),
              const SizedBox(width: 8),
              SizedBox(
                width: 50,
                child: Text(
                  '\${((item.quantity * item.unitPriceCents) / 100).toStringAsFixed(2)} €',
                  textAlign: TextAlign.right,
                  style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14, color: Color(0xFFF8FAFC), fontFamily: 'JetBrains Mono'),
                ),
              )
            ],
          )
        ],
      ),
    );
  }

  Widget _buildQtyButton(String label, VoidCallback onTap) {
    return InkWell(
      onTap: onTap,
      child: Container(
        width: 28, height: 28,
        decoration: BoxDecoration(
          color: const Color(0x14FFFFFF),
          border: Border.all(color: const Color(0x14FFFFFF)),
          borderRadius: BorderRadius.circular(6),
        ),
        alignment: Alignment.center,
        child: Text(label, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 16)),
      ),
    );
  }
}
""")

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

import re
old_buildCartPanel = re.search(r'Widget _buildCartPanel\(BuildContext context, PosTerminalState state\) \{.*?\n  \}', text, re.DOTALL)
if old_buildCartPanel:
    new_cart_method = """Widget _buildCartPanel(BuildContext context, PosTerminalState state) {
    final double totalTtc = (state.totalTtcCents) / 100.0;
    final double totalHt = totalTtc / 1.10;
    final double tva = totalTtc - totalHt;

    return Container(
      width: 380,
      decoration: const BoxDecoration(
        color: panelColor,
        border: Border(right: BorderSide(color: borderColor), left: BorderSide(color: borderColor)),
      ),
      child: Column(
        children: [
          // Header Cart
          Container(
            padding: const EdgeInsets.all(16),
            decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: borderColor))),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.center,
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                      decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(6)),
                      child: const Text("Comptoir", style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14)),
                    ),
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.all(2),
                      decoration: BoxDecoration(
                        color: const Color(0x990F172A),
                        borderRadius: BorderRadius.circular(6),
                        border: Border.all(color: borderColor),
                      ),
                      child: Row(
                        children: [
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                            decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(4)),
                            child: const Text("🥡 À Emporter", style: TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.bold)),
                          ),
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                            child: const Text("🍽️ Sur Place", style: TextStyle(color: textMuted, fontSize: 11, fontWeight: FontWeight.bold)),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                      decoration: BoxDecoration(
                        color: const Color(0x26F59E0B),
                        border: Border.all(color: const Color(0x66F59E0B)),
                        borderRadius: BorderRadius.circular(6)
                      ),
                      child: const Text("⏸️ 0", style: TextStyle(color: Color(0xFFFBBF24), fontSize: 14)),
                    ),
                    const SizedBox(width: 8),
                    InkWell(
                      onTap: () {}, // Clear cart
                      child: const Text("🗑️", style: TextStyle(fontSize: 18)),
                    ),
                  ]
                )
              ],
            ),
          ),
          
          Expanded(
            child: state.cartItems.isEmpty
                ? Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.receipt_long, size: 64, color: textMuted.withAlpha(128)),
                        const SizedBox(height: 16),
                        const Text("La commande est vide", style: TextStyle(color: textMuted, fontSize: 16)),
                      ],
                    ),
                  )
                : ListView.builder(
                    padding: const EdgeInsets.symmetric(vertical: 12),
                    itemCount: state.cartItems.length,
                    itemBuilder: (context, index) => CartSwipeItem(
                      item: state.cartItems[index],
                      onDelete: () => context.read<PosTerminalBloc>().add(RemoveCartItemEvent(state.cartItems[index])),
                      onAdd: () => context.read<PosTerminalBloc>().add(AddProductEvent(
                        productId: state.cartItems[index].productId,
                        productName: state.cartItems[index].productName,
                        unitPriceCents: state.cartItems[index].unitPriceCents,
                      )),
                      onRemove: () => context.read<PosTerminalBloc>().add(RemoveCartItemEvent(state.cartItems[index])),
                    ),
                  ),
          ),
          
          Container(
            padding: const EdgeInsets.all(16),
            decoration: const BoxDecoration(
              color: Color(0xB20F172A),
              border: Border(top: BorderSide(color: borderColor)),
            ),
            child: Column(
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text("Total HT :", style: TextStyle(color: textMuted, fontSize: 13)),
                    Text("${totalHt.toStringAsFixed(2)} €", style: const TextStyle(color: textMuted, fontSize: 13)),
                  ],
                ),
                const SizedBox(height: 4),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text("TVA (10%) :", style: TextStyle(color: textMuted, fontSize: 13)),
                    Text("${tva.toStringAsFixed(2)} €", style: const TextStyle(color: textMuted, fontSize: 13)),
                  ],
                ),
                const SizedBox(height: 8),
                Container(
                  padding: const EdgeInsets.only(top: 8),
                  decoration: const BoxDecoration(border: Border(top: BorderSide(color: Color(0x33FFFFFF), style: BorderStyle.none))), 
                  // BorderStyle.solid is required for dashed but we'll use a simple top border line
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    crossAxisAlignment: CrossAxisAlignment.center,
                    children: [
                      const Text("TOTAL TTC", style: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.bold)),
                      Text("${totalTtc.toStringAsFixed(2)} €", style: const TextStyle(color: successColor, fontSize: 22, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                
                Row(
                  children: [
                    _buildFastCash("10 €"), _buildFastCash("20 €"), _buildFastCash("50 €"), _buildFastCash("Exact"),
                  ],
                ),
                const SizedBox(height: 10),

                GridView.count(
                  crossAxisCount: 3,
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  crossAxisSpacing: 8,
                  mainAxisSpacing: 8,
                  childAspectRatio: 2.2, // wide buttons
                  children: [
                    _buildCartAction("📤", "Cuisine", const Color(0xFF3B82F6), Colors.white),
                    _buildCartAction("⏸️", "Attente", cardColor, Colors.white),
                    _buildCartAction("🏷️", "Remise", const Color(0xFF8B5CF6), Colors.white),
                    _buildCartAction("🔄", "Transférer", const Color(0xFF06B6D4), Colors.white),
                    _buildCartAction("➗", "Split", const Color(0xFFF59E0B), Colors.black),
                    _buildCartAction("💳", "Encaisser", successColor, Colors.white),
                  ],
                )
              ],
            ),
          )
        ],
      ),
    );
  }

  Widget _buildFastCash(String label) {
    return Expanded(
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 3),
        padding: const EdgeInsets.symmetric(vertical: 10),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: const Color(0x2610B981),
          border: Border.all(color: const Color(0x4D10B981)),
          borderRadius: BorderRadius.circular(8)
        ),
        child: Text(label, style: const TextStyle(color: Color(0xFF34D399), fontWeight: FontWeight.bold, fontSize: 14)),
      )
    );
  }

  Widget _buildCartAction(String icon, String label, Color bgColor, Color textColor) {
    return Container(
      decoration: BoxDecoration(color: bgColor, borderRadius: BorderRadius.circular(8)),
      alignment: Alignment.center,
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Text(icon, style: const TextStyle(fontSize: 14)),
          const SizedBox(width: 4),
          Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 13)),
        ],
      ),
    );
  }"""
    text = text.replace(old_buildCartPanel.group(0), new_cart_method)
    
    with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
        f.write(text)
else:
    print("Could not find _buildCartPanel in the file!")
