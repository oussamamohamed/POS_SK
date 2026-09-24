with open('src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart', 'r') as f:
    text = f.read()

text = text.replace(
    'final double total = state.cartItems.fold(0, (sum, item) => sum + (item.quantity * item.unitPriceCents)) / 100;',
    'final double total = (state.totalTtcCents) / 100.0;'
)
text = text.replace(
    "import 'package:flutter/material.dart';",
    "import 'package:flutter/material.dart';\nimport '../../domain/entities/order_item.dart';"
)
text = text.replace(
    "import '../../domain/entities/order_item.dart';",
    "" 
) # removes them all, but I just added it back, maybe I shouldn't bother since unused imports are just warnings.

text = text.replace(
    "Icon(Icons.receipt_long, size: 64, color: textMuted.withOpacity(0.5)),",
    "Icon(Icons.receipt_long, size: 64, color: textMuted.withValues(alpha: 0.5)),"
)
with open('src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart', 'w') as f:
    f.write(text)

