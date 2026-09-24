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
      margin: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: const Color(0x08FFFFFF), // rgba(255, 255, 255, 0.03)
        border: Border.all(color: const Color(0x14FFFFFF)),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Wrap(
                  crossAxisAlignment: WrapCrossAlignment.center,
                  spacing: 6,
                  runSpacing: 4,
                  children: [
                    Text(
                      item.productName, 
                      style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 13, color: Color(0xFFF8FAFC)),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
                      decoration: BoxDecoration(
                        color: const Color(0x1AF59E0B),
                        border: Border.all(color: const Color(0x33F59E0B)),
                        borderRadius: BorderRadius.circular(2),
                      ),
                      child: const Text("Direct", style: TextStyle(color: Color(0xFFFCD34D), fontSize: 10, fontWeight: FontWeight.bold)),
                    ),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
                      decoration: BoxDecoration(
                        color: const Color(0x1AF59E0B),
                        border: Border.all(color: const Color(0x33F59E0B)),
                        borderRadius: BorderRadius.circular(2),
                      ),
                      child: const Text("➕ Nouveau", style: TextStyle(color: Color(0xFFFBBF24), fontSize: 10, fontWeight: FontWeight.bold)),
                    )
                  ]
                ),
                const SizedBox(height: 2),
                Text(
                  '\${(item.unitPriceCents / 100).toStringAsFixed(2)} € × \${item.quantity} (TVA 10%)', 
                  style: const TextStyle(color: Color(0xFF94A3B8), fontSize: 11)
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Row(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.center,
            children: [
              _buildQtyButton("-", onRemove),
              Container(
                width: 25,
                alignment: Alignment.center,
                child: Text("\${item.quantity}", style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Colors.white)),
              ),
              _buildQtyButton("+", onAdd),
              const SizedBox(width: 8),
              Container(
                width: 60,
                alignment: Alignment.centerRight,
                child: Text(
                  '\${((item.quantity * item.unitPriceCents) / 100).toStringAsFixed(2)} €',
                  style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 13, color: Color(0xFFF8FAFC), fontFamily: 'JetBrains Mono'),
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
        width: 26, height: 26,
        decoration: BoxDecoration(
          color: const Color(0x14FFFFFF),
          border: Border.all(color: const Color(0x14FFFFFF)),
          borderRadius: BorderRadius.circular(6),
        ),
        alignment: Alignment.center,
        child: Text(label, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14)),
      ),
    );
  }
}
""")
