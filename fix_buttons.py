import os

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

# Fix _buildCartAction
old_cart_action = """  Widget _buildCartAction(String icon, String label, Color bgColor, Color textColor) {
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
new_cart_action = """  Widget _buildCartAction(BuildContext context, String icon, String label, Color bgColor, Color textColor) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Action: $label"), duration: const Duration(seconds: 1)));
        },
        borderRadius: BorderRadius.circular(8),
        child: Ink(
          decoration: BoxDecoration(color: bgColor, borderRadius: BorderRadius.circular(8)),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(icon, style: const TextStyle(fontSize: 14)),
              const SizedBox(width: 4),
              Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 13)),
            ],
          ),
        ),
      ),
    );
  }"""
text = text.replace(old_cart_action, new_cart_action)
text = text.replace('_buildCartAction("📤"', '_buildCartAction(context, "📤"')
text = text.replace('_buildCartAction("⏸️"', '_buildCartAction(context, "⏸️"')
text = text.replace('_buildCartAction("🏷️"', '_buildCartAction(context, "🏷️"')
text = text.replace('_buildCartAction("🔄"', '_buildCartAction(context, "🔄"')
text = text.replace('_buildCartAction("➗"', '_buildCartAction(context, "➗"')
text = text.replace('_buildCartAction("💳"', '_buildCartAction(context, "💳"')

# Fix _buildFastCash
old_fast_cash = """  Widget _buildFastCash(String label) {
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
  }"""
new_fast_cash = """  Widget _buildFastCash(BuildContext context, String label) {
    return Expanded(
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 3),
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            onTap: () {
              ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Fast Cash: $label"), duration: const Duration(seconds: 1)));
            },
            borderRadius: BorderRadius.circular(8),
            child: Ink(
              padding: const EdgeInsets.symmetric(vertical: 10),
              decoration: BoxDecoration(
                color: const Color(0x2610B981),
                border: Border.all(color: const Color(0x4D10B981)),
                borderRadius: BorderRadius.circular(8)
              ),
              child: Center(child: Text(label, style: const TextStyle(color: Color(0xFF34D399), fontWeight: FontWeight.bold, fontSize: 14))),
            ),
          ),
        ),
      )
    );
  }"""
text = text.replace(old_fast_cash, new_fast_cash)
text = text.replace('_buildFastCash("', '_buildFastCash(context, "')

# Fix _buildQuickKey
old_quick_key = """  Widget _buildQuickKey(String icon, String label) {
    return Container(
      margin: const EdgeInsets.only(right: 8),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      decoration: BoxDecoration(
        color: const Color(0x1AF59E0B),
        border: Border.all(color: const Color(0x66F59E0B)),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Text(icon, style: const TextStyle(fontSize: 14, color: Color(0xFFFEF08A))),
          const SizedBox(width: 8),
          Text(label, style: const TextStyle(color: Color(0xFFFEF08A), fontWeight: FontWeight.bold, fontSize: 13)),
        ],
      ),
    );
  }"""
new_quick_key = """  Widget _buildQuickKey(BuildContext context, String icon, String label) {
    return Container(
      margin: const EdgeInsets.only(right: 8),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Quick Key: $label"), duration: const Duration(seconds: 1)));
          },
          borderRadius: BorderRadius.circular(8),
          child: Ink(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
            decoration: BoxDecoration(
              color: const Color(0x1AF59E0B),
              border: Border.all(color: const Color(0x66F59E0B)),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              children: [
                Text(icon, style: const TextStyle(fontSize: 14, color: Color(0xFFFEF08A))),
                const SizedBox(width: 8),
                Text(label, style: const TextStyle(color: Color(0xFFFEF08A), fontWeight: FontWeight.bold, fontSize: 13)),
              ],
            ),
          ),
        ),
      ),
    );
  }"""
text = text.replace(old_quick_key, new_quick_key)
text = text.replace('_buildQuickKey("', '_buildQuickKey(context, "')

# Fix _buildCatTab
old_cat_tab = """  Widget _buildCatTab(String label, bool active) {
    return Container(
      margin: const EdgeInsets.only(right: 10),
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
      decoration: BoxDecoration(
        color: panelColor,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: active ? Colors.white : borderColor),
      ),
      child: Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 14)),
    );
  }"""
new_cat_tab = """  Widget _buildCatTab(BuildContext context, String label, bool active) {
    return Container(
      margin: const EdgeInsets.only(right: 10),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Categorie: $label"), duration: const Duration(seconds: 1)));
          },
          borderRadius: BorderRadius.circular(12),
          child: Ink(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
            decoration: BoxDecoration(
              color: panelColor,
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: active ? Colors.white : borderColor),
            ),
            child: Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 14)),
          ),
        ),
      ),
    );
  }"""
text = text.replace(old_cat_tab, new_cat_tab)
text = text.replace('_buildCatTab("', '_buildCatTab(context, "')


# Fix Top Nav Tab
old_nav_tab = """  Widget _buildNavTab(String icon, String label, bool active) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
      decoration: BoxDecoration(
        color: active ? primaryColor : Colors.transparent,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Text(icon, style: const TextStyle(fontSize: 14)),
          const SizedBox(width: 8),
          Text(label, style: TextStyle(
            color: active ? Colors.white : textMuted, 
            fontWeight: FontWeight.bold, 
            fontSize: 14)
          ),
        ],
      ),
    );
  }"""
new_nav_tab = """  Widget _buildNavTab(BuildContext context, String icon, String label, bool active) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Navigation: $label"), duration: const Duration(seconds: 1)));
        },
        borderRadius: BorderRadius.circular(8),
        child: Ink(
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
          decoration: BoxDecoration(
            color: active ? primaryColor : Colors.transparent,
            borderRadius: BorderRadius.circular(8),
          ),
          child: Row(
            children: [
              Text(icon, style: const TextStyle(fontSize: 14)),
              const SizedBox(width: 8),
              Text(label, style: TextStyle(
                color: active ? Colors.white : textMuted, 
                fontWeight: FontWeight.bold, 
                fontSize: 14)
              ),
            ],
          ),
        ),
      ),
    );
  }"""
text = text.replace(old_nav_tab, new_nav_tab)
text = text.replace('_buildNavTab("', '_buildNavTab(context, "')
text = text.replace('_buildAppHeader()', '_buildAppHeader(context)')
text = text.replace('Widget _buildAppHeader()', 'Widget _buildAppHeader(BuildContext context)')



# Fix 🗑️ (Clear Cart Button)
# It was: InkWell( onTap: () {}, child: const Text("🗑️", style: TextStyle(fontSize: 18)) )
text = text.replace('onTap: () {}, // Clear cart', 'onTap: () { context.read<PosTerminalBloc>().add(ClearCartEvent()); }, // Clear cart')

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

