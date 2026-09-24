import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

# Fix 1: _buildAppHeader overflowing middle nav section. Wrap it in Expanded and SingleChildScrollView
old_nav_tabs_container = """          Container(
            padding: const EdgeInsets.all(4),
            decoration: BoxDecoration(
              color: const Color(0x990F172A),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: borderColor),
            ),
            child: Row(
              children: [
                _buildNavTab(context, "🛒", "Caisse", 0),"""
new_nav_tabs_container = """          Expanded(
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Container(
                margin: const EdgeInsets.symmetric(horizontal: 16),
                padding: const EdgeInsets.all(4),
                decoration: BoxDecoration(
                  color: const Color(0x990F172A),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: borderColor),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _buildNavTab(context, "🛒", "Caisse", 0),"""
text = text.replace(old_nav_tabs_container, new_nav_tabs_container)


# Fix 2: _buildCartPanel top section Row overflowing
# Around line 233
old_cart_header = """            child: Row(
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
                      decoration: BoxDecoration("""
new_cart_header = """            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.center,
                      children: [
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                          decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(6)),
                          child: const Text("Comptoir", style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13)),
                        ),
                        const SizedBox(width: 4),
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
                                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                                decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(4)),
                                child: const Text("🥡 À Emporter", style: TextStyle(color: Colors.white, fontSize: 10, fontWeight: FontWeight.bold)),
                              ),
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                                child: const Text("🍽️ Sur Place", style: TextStyle(color: textMuted, fontSize: 10, fontWeight: FontWeight.bold)),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                      decoration: BoxDecoration("""
text = text.replace(old_cart_header, new_cart_header)

# Fix 3: _buildCartAction Row overflowing
old_cart_action = """          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(icon, style: const TextStyle(fontSize: 14)),
              const SizedBox(width: 4),
              Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 13)),
            ],
          ),"""
new_cart_action = """          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(icon, style: const TextStyle(fontSize: 13)),
              const SizedBox(width: 4),
              Flexible(child: Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 12), overflow: TextOverflow.ellipsis)),
            ],
          ),"""
text = text.replace(old_cart_action, new_cart_action)

# Fix 4: fast cash buttons might be overflowing if width shrinks
old_fast_cash = """              child: Center(child: Text(label, style: const TextStyle(color: Color(0xFF34D399), fontWeight: FontWeight.bold, fontSize: 14))),"""
new_fast_cash = """              child: Center(child: Text(label, style: const TextStyle(color: Color(0xFF34D399), fontWeight: FontWeight.bold, fontSize: 13), overflow: TextOverflow.ellipsis)),"""
text = text.replace(old_fast_cash, new_fast_cash)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

