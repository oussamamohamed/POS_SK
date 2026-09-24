import 'package:flutter/material.dart';

class TableMapPage extends StatelessWidget {
  final VoidCallback onTableSelected;

  const TableMapPage({super.key, required this.onTableSelected});

  @override
  Widget build(BuildContext context) {
    return Container(
      color: const Color(0xFF0F172A),
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text("SALLE PRINCIPALE", style: TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.bold)),
          const SizedBox(height: 24),
          Expanded(
            child: GridView.builder(
              gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                crossAxisCount: 6,
                crossAxisSpacing: 16,
                mainAxisSpacing: 16,
                childAspectRatio: 1.2,
              ),
              itemCount: 20,
              itemBuilder: (context, index) {
                final isOccupied = index % 3 == 0;
                return InkWell(
                  onTap: () {
                    // Navigate to checkout/pos internally
                    onTableSelected();
                  },
                  borderRadius: BorderRadius.circular(12),
                  child: Container(
                    decoration: BoxDecoration(
                      color: isOccupied ? const Color(0x33EF4444) : const Color(0x3310B981),
                      border: Border.all(color: isOccupied ? const Color(0xFFEF4444) : const Color(0xFF10B981)),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.table_restaurant, color: isOccupied ? const Color(0xFFFCA5A5) : const Color(0xFF6EE7B7), size: 36),
                        const SizedBox(height: 8),
                        Text("Table ${index + 1}", style: TextStyle(color: isOccupied ? const Color(0xFFFCA5A5) : const Color(0xFF6EE7B7), fontWeight: FontWeight.bold)),
                        if (isOccupied)
                          const Text("45.50 €", style: TextStyle(color: Colors.white, fontSize: 12, fontFamily: 'JetBrains Mono')),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
