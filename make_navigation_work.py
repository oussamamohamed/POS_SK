import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

# Change PosTerminalPage to a StatefulWidget
old_class_def = """class PosTerminalPage extends StatelessWidget {
  const PosTerminalPage({super.key});

  @override
  Widget build(BuildContext context) {"""
new_class_def = """class PosTerminalPage extends StatefulWidget {
  const PosTerminalPage({super.key});

  @override
  State<PosTerminalPage> createState() => _PosTerminalPageState();
}

class _PosTerminalPageState extends State<PosTerminalPage> {
  int _currentTabIndex = 0;

  @override
  Widget build(BuildContext context) {"""
text = text.replace(old_class_def, new_class_def)

# Update Scaffolding to use IndexedStack
old_scaffold_body = """      body: BlocBuilder<PosTerminalBloc, PosTerminalState>(
        builder: (context, state) {
          return Container(
            color: const Color(0xFF0F172A), // --bg-main
            child: Column(
              children: [
                _buildAppHeader(context),
                Expanded(
                  child: Row(
                    children: [
                      if (state.isLeftHandedMode) _buildCartPanel(context, state),
                      _buildMainPanel(context, state),
                      if (!state.isLeftHandedMode) _buildCartPanel(context, state),
                    ],
                  ),
                )
              ],
            ),
          );
        },
      ),"""
new_scaffold_body = """      body: BlocBuilder<PosTerminalBloc, PosTerminalState>(
        builder: (context, state) {
          return Container(
            color: const Color(0xFF0F172A), 
            child: Column(
              children: [
                _buildAppHeader(context),
                Expanded(
                  child: IndexedStack(
                    index: _currentTabIndex,
                    children: [
                      // TAB 0: Caisse (Main POS Terminal)
                      Row(
                        children: [
                          if (state.isLeftHandedMode) _buildCartPanel(context, state),
                          _buildMainPanel(context, state),
                          if (!state.isLeftHandedMode) _buildCartPanel(context, state),
                        ],
                      ),
                      // TAB 1: Plan de Salle
                      _buildPlaceholderPage("Plan de Salle", Icons.map_outlined),
                      // TAB 2: Cuisine KDS
                      _buildPlaceholderPage("Cuisine (KDS)", Icons.restaurant_menu),
                      // TAB 3: Paramètres
                      _buildPlaceholderPage("Paramétrage", Icons.settings),
                      // TAB 4: Fiscalité
                      _buildPlaceholderPage("Fiscalité NF525", Icons.receipt_long),
                    ],
                  ),
                )
              ],
            ),
          );
        },
      ),"""
text = text.replace(old_scaffold_body, new_scaffold_body)


# Add placeholder method
placeholder = """  Widget _buildPlaceholderPage(String title, IconData icon) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 80, color: const Color(0x33FFFFFF)),
          const SizedBox(height: 20),
          Text(title, style: const TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.bold)),
          const SizedBox(height: 10),
          const Text("En cours de développement...", style: TextStyle(color: Color(0xFF94A3B8), fontSize: 16)),
        ],
      ),
    );
  }"""
text = text.replace("  Widget _buildAppHeader(context) {", placeholder + "\n\n  Widget _buildAppHeader(context) {")

# Update App Header Navigation
# old: _buildNavTab(context, "🛒", "Caisse", true),
old_nav_tabs = """                _buildNavTab(context, "🛒", "Caisse", true),
                _buildNavTab(context, "🗺️", "Plan de Salle", false),
                _buildNavTab(context, "👨‍🍳", "Cuisine KDS", false),
                _buildNavTab(context, "⚙️", "Paramétrage", false),
                _buildNavTab(context, "📜", "Fiscalité NF525", false),"""
new_nav_tabs = """                _buildNavTab(context, "🛒", "Caisse", 0),
                _buildNavTab(context, "🗺️", "Plan de Salle", 1),
                _buildNavTab(context, "👨‍🍳", "Cuisine KDS", 2),
                _buildNavTab(context, "⚙️", "Paramétrage", 3),
                _buildNavTab(context, "📜", "Fiscalité", 4),"""
text = text.replace(old_nav_tabs, new_nav_tabs)

old_nav_def = """  Widget _buildNavTab(BuildContext context, String icon, String label, bool active) {"""
new_nav_def = """  Widget _buildNavTab(BuildContext context, String icon, String label, int tabIndex) {
    bool active = _currentTabIndex == tabIndex;"""
text = text.replace(old_nav_def, new_nav_def)

old_nav_tap = """        onTap: () {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Navigation: $label"), duration: const Duration(seconds: 1)));
        },"""
new_nav_tap = """        onTap: () {
          setState(() {
            _currentTabIndex = tabIndex;
          });
        },"""
text = text.replace(old_nav_tap, new_nav_tap)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

