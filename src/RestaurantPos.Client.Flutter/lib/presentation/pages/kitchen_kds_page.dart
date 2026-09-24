import 'package:flutter/material.dart';

class KitchenKdsPage extends StatelessWidget {
  const KitchenKdsPage({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFF0F172A),
      padding: const EdgeInsets.all(24),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _buildLane("Nouveau (3)", const Color(0xFF3B82F6), true),
          const SizedBox(width: 16),
          _buildLane("En Préparation (1)", const Color(0xFFF59E0B), false),
          const SizedBox(width: 16),
          _buildLane("Prêt (0)", const Color(0xFF10B981), false),
        ],
      ),
    );
  }

  Widget _buildLane(String title, Color color, bool hasCards) {
    return Expanded(
      child: Column(
        children: [
          Container(
            padding: const EdgeInsets.symmetric(vertical: 12),
            decoration: BoxDecoration(color: color.withOpacity(0.2), border: Border.all(color: color), borderRadius: BorderRadius.circular(8)),
            alignment: Alignment.center,
            child: Text(title, style: TextStyle(color: color, fontWeight: FontWeight.bold, fontSize: 18)),
          ),
          const SizedBox(height: 16),
          if (hasCards)
            Expanded(
              child: ListView.builder(
                itemCount: 3,
                itemBuilder: (context, index) {
                  return Container(
                    margin: const EdgeInsets.only(bottom: 12),
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: const Color(0xFF1E293B),
                      border: Border.all(color: const Color(0xFF334155)),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Text("Commande #${1024 + index}", style: const TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
                            const Text("12:45", style: TextStyle(color: Color(0xFF94A3B8))),
                          ],
                        ),
                        const Divider(color: Color(0xFF334155)),
                        const Text("- 1x Burger Maison", style: TextStyle(color: Colors.white)),
                        const Text("- 2x Frites", style: TextStyle(color: Colors.white)),
                        const Text("- 1x Coca Cola", style: TextStyle(color: Colors.white)),
                        const SizedBox(height: 12),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.end,
                          children: [
                            ElevatedButton(
                              onPressed: () {},
                              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFFF59E0B)),
                              child: const Text("Préparer", style: TextStyle(color: Colors.black, fontWeight: FontWeight.bold)),
                            )
                          ],
                        )
                      ],
                    ),
                  );
                },
              ),
            )
        ],
      ),
    );
  }
}
