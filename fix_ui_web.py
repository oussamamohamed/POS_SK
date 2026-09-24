import os

pos_terminal = """import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';
import '../blocs/pos_terminal/pos_terminal_state.dart';
import '../blocs/pos_terminal/input_buffer_state.dart';
import '../../domain/entities/order_item.dart';
import '../widgets/numeric_keypad.dart';
import '../widgets/cart_swipe_item.dart';

// Brand Variables from Website CSS
const bgColor = Color(0xFF0F172A);
const panelColor = Color(0xFF1E293B);
const cardColor = Color(0xFF334155);
const borderColor = Color(0x14FFFFFF);
const primaryColor = Color(0xFF3B82F6);
const textColor = Color(0xFFF8FAFC);
const textMuted = Color(0xFF94A3B8);

class PosTerminalPage extends StatelessWidget {
  const PosTerminalPage({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<PosTerminalBloc, PosTerminalState>(
      builder: (context, state) {
        return Scaffold(
          backgroundColor: bgColor,
          body: SafeArea(
            child: Row(
              children: [
                if (state.isLeftHanded) _buildCartPanel(context, state),
                _buildMainPanel(context, state),
                if (!state.isLeftHanded) _buildCartPanel(context, state),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _buildCartPanel(BuildContext context, PosTerminalState state) {
    final double total = state.cart.fold(0, (sum, item) => sum + (item.quantity * item.unitPriceCents)) / 100;

    return Container(
      width: 380,
      decoration: const BoxDecoration(
        color: panelColor,
        border: Border(right: BorderSide(color: borderColor), left: BorderSide(color: borderColor)),
      ),
      child: Column(
        children: [
          // Header
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
            decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: borderColor))),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                const Text("TICKET", style: TextStyle(color: textColor, fontSize: 18, fontWeight: FontWeight.bold, letterSpacing: 1.2)),
                InkWell(
                  onTap: () => context.read<PosTerminalBloc>().add(ToggleHandednessEvent()),
                  child: Container(
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(color: cardColor, borderRadius: BorderRadius.circular(8)),
                    child: Icon(Icons.swap_horiz, color: textMuted, size: 20),
                  ),
                )
              ],
            ),
          ),
          // Cart Items
          Expanded(
            child: state.cart.isEmpty
                ? Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.receipt_long, size: 64, color: textMuted.withOpacity(0.5)),
                        const SizedBox(height: 16),
                        const Text("La commande est vide", style: TextStyle(color: textMuted, fontSize: 16)),
                      ],
                    ),
                  )
                : ListView.builder(
                    padding: const EdgeInsets.symmetric(vertical: 12),
                    itemCount: state.cart.length,
                    itemBuilder: (context, index) => CartSwipeItem(
                      item: state.cart[index],
                      onDelete: () => context.read<PosTerminalBloc>().add(RemoveItemEvent(state.cart[index].id)),
                    ),
                  ),
          ),
          
          // Total & Checkout
          Container(
            padding: const EdgeInsets.all(20),
            decoration: const BoxDecoration(
              color: panelColor,
              border: Border(top: BorderSide(color: borderColor)),
            ),
            child: Column(
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text("Total", style: TextStyle(color: textMuted, fontSize: 16)),
                    Text("\${total.toStringAsFixed(2)} €", style: const TextStyle(color: textColor, fontSize: 24, fontWeight: FontWeight.bold)),
                  ],
                ),
                const SizedBox(height: 16),
                SizedBox(
                  width: double.infinity,
                  height: 60,
                  child: ElevatedButton(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: primaryColor,
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                      elevation: 0,
                    ),
                    onPressed: state.cart.isEmpty ? null : () {},
                    child: const Text("ENCAISSER", style: TextStyle(fontSize: 16, fontWeight: FontWeight.bold, color: Colors.white, letterSpacing: 1)),
                  ),
                )
              ],
            ),
          )
        ],
      ),
    );
  }

  Widget _buildMainPanel(BuildContext context, PosTerminalState state) {
    return Expanded(
      child: Column(
        children: [
          // Header Categories (Dummy for now)
          Container(
            height: 70,
            padding: const EdgeInsets.symmetric(horizontal: 16),
            decoration: const BoxDecoration(
              color: panelColor,
              border: Border(bottom: BorderSide(color: borderColor)),
            ),
            child: Row(
              children: [
                _buildChip("Tous", true),
                const SizedBox(width: 8),
                _buildChip("Plats", false),
                const SizedBox(width: 8),
                _buildChip("Boissons", false),
                const SizedBox(width: 8),
                _buildChip("Desserts", false),
              ],
            ),
          ),
          
          Expanded(
            child: Row(
              children: [
                // Products Grid
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.all(16),
                    child: GridView.builder(
                      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: 4,
                        childAspectRatio: 1,
                        crossAxisSpacing: 12,
                        mainAxisSpacing: 12,
                      ),
                      itemCount: 12,
                      itemBuilder: (context, index) {
                        return InkWell(
                          onTap: () {
                            context.read<PosTerminalBloc>().add(AddItemEvent(
                              OrderItem(id: "p\$index", productId: "p\$index", productName: "Produit \${index+1}", quantity: 1, unitPriceCents: 1050 + (index * 100))
                            ));
                          },
                          child: Container(
                            decoration: BoxDecoration(
                              color: panelColor,
                              border: Border.all(color: const Color(0x1EFFFFFF)),
                              borderRadius: BorderRadius.zero, // strict carré as per CSS
                            ),
                            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
                            child: Column(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: [
                                Icon(Icons.fastfood, color: textMuted, size: 32),
                                const SizedBox(height: 12),
                                Text("Produit \${index+1}", 
                                  style: const TextStyle(color: textColor, fontWeight: FontWeight.w600, fontSize: 13),
                                  textAlign: TextAlign.center,
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                ),
                                const SizedBox(height: 4),
                                Text("\${((1050 + (index * 100))/100).toStringAsFixed(2)} €", 
                                  style: const TextStyle(color: textMuted, fontSize: 12),
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                ),
                
                // Numpad Area
                Container(
                  width: 320,
                  decoration: const BoxDecoration(
                    color: panelColor,
                    border: Border(left: BorderSide(color: borderColor)),
                  ),
                  child: Column(
                    children: [
                      Container(
                        padding: const EdgeInsets.all(24),
                        alignment: Alignment.centerRight,
                        decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: borderColor))),
                        height: 100,
                        child: Text(
                          state.numericBuffer.currentBuffer.isEmpty ? "0" : state.numericBuffer.currentBuffer,
                          style: TextStyle(
                            fontSize: 36, 
                            fontWeight: FontWeight.bold, 
                            color: state.numericBuffer.currentBuffer.isEmpty ? textMuted : textColor,
                            fontFamily: 'JetBrains Mono', // From CSS snippet
                          ),
                        ),
                      ),
                      const Expanded(
                        child: Padding(
                          padding: EdgeInsets.all(16.0),
                          child: NumericKeypad(),
                        ),
                      ),
                    ],
                  ),
                )
              ],
            ),
          )
        ],
      ),
    );
  }

  Widget _buildChip(String label, bool active) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
      decoration: BoxDecoration(
        color: active ? primaryColor : cardColor,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(label, style: TextStyle(color: active ? Colors.white : textColor, fontWeight: FontWeight.bold)),
    );
  }
}
"""

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(pos_terminal)


cart_item = """import 'package:flutter/material.dart';
import '../../domain/entities/order_item.dart';

class CartSwipeItem extends StatelessWidget {
  final OrderItem item;
  final VoidCallback onDelete;

  const CartSwipeItem({super.key, required this.item, required this.onDelete});

  @override
  Widget build(BuildContext context) {
    return Dismissible(
      key: ValueKey(item.id),
      direction: DismissDirection.endToStart,
      onDismissed: (_) => onDelete(),
      background: Container(
        alignment: Alignment.centerRight,
        padding: const EdgeInsets.only(right: 20),
        color: const Color(0xFFEF4444), // --danger
        child: const Icon(Icons.delete, color: Colors.white, size: 24),
      ),
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: const Color(0x08FFFFFF), // rgba(255,255,255,0.03) + slight adjustment
          border: Border.all(color: const Color(0x14FFFFFF)),
          borderRadius: BorderRadius.circular(8),
        ),
        child: Row(
          children: [
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
              decoration: BoxDecoration(
                color: const Color(0xFF334155),
                borderRadius: BorderRadius.circular(6)
              ),
              child: Text(
                "\${item.quantity}",
                style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15, color: Color(0xFFF8FAFC)),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(item.productName, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 15, color: Color(0xFFF8FAFC))),
                  const SizedBox(height: 4),
                  Text('\${(item.unitPriceCents / 100).toStringAsFixed(2)} €', 
                       style: const TextStyle(color: Color(0xFF94A3B8), fontSize: 13)),
                ],
              ),
            ),
            Text(
              '\${((item.quantity * item.unitPriceCents) / 100).toStringAsFixed(2)} €',
              style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 16, color: Color(0xFFF8FAFC)),
            )
          ],
        ),
      ),
    );
  }
}
"""

with open("src/RestaurantPos.Client.Flutter/lib/presentation/widgets/cart_swipe_item.dart", "w") as f:
    f.write(cart_item)


numeric_keypad = """import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';

class NumericKeypad extends StatelessWidget {
  const NumericKeypad({super.key});

  @override
  Widget build(BuildContext context) {
    return GridView.count(
      crossAxisCount: 3,
      childAspectRatio: 1.2,
      crossAxisSpacing: 12,
      mainAxisSpacing: 12,
      physics: const NeverScrollableScrollPhysics(),
      children: [
        _buildKey(context, '7'),
        _buildKey(context, '8'),
        _buildKey(context, '9'),
        _buildKey(context, '4'),
        _buildKey(context, '5'),
        _buildKey(context, '6'),
        _buildKey(context, '1'),
        _buildKey(context, '2'),
        _buildKey(context, '3'),
        _buildKey(context, 'C', color: const Color(0xFFEF4444)),
        _buildKey(context, '0'),
        _buildKey(context, '⌫', color: const Color(0xFFF59E0B)),
      ],
    );
  }

  Widget _buildKey(BuildContext context, String label, {Color? color}) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          if (label == 'C') {
            context.read<PosTerminalBloc>().add(ClearInputBufferEvent());
          } else if (label == '⌫') {
            // Remove last not natively supported in minimal bloc, skipping or doing custom if exists
          } else {
            context.read<PosTerminalBloc>().add(NumpadPressedEvent(label));
          }
        },
        borderRadius: BorderRadius.circular(12),
        child: Ink(
          decoration: BoxDecoration(
            color: const Color(0xFF334155),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: const Color(0x14FFFFFF)),
          ),
          child: Center(
            child: Text(
              label,
              style: TextStyle(
                fontSize: 24,
                fontWeight: FontWeight.bold,
                color: color ?? const Color(0xFFF8FAFC),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
"""

with open("src/RestaurantPos.Client.Flutter/lib/presentation/widgets/numeric_keypad.dart", "w") as f:
    f.write(numeric_keypad)

