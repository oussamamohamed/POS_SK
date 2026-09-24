import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

# Add imports
imports = """import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';
import '../blocs/pos_terminal/pos_terminal_state.dart';
import 'table_map_page.dart';
import 'kitchen_kds_page.dart';"""

text = text.replace("import 'package:flutter_bloc/flutter_bloc.dart';\nimport '../blocs/pos_terminal/pos_terminal_bloc.dart';\nimport '../blocs/pos_terminal/pos_terminal_event.dart';\nimport '../blocs/pos_terminal/pos_terminal_state.dart';", imports)

# Replace table map tab with real
old_table_map = """                      // TAB 1: Plan de Salle
                      _buildPlaceholderPage("Plan de Salle", Icons.map_outlined),"""
new_table_map = """                      // TAB 1: Plan de Salle
                      TableMapPage(onTableSelected: () {
                        setState(() { _currentTabIndex = 0; });
                        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Table sélectionnée. Prêt à commander."), duration: Duration(seconds: 2)));
                      }),"""
text = text.replace(old_table_map, new_table_map)

# Replace kds tab with real
old_kds = """                      // TAB 2: Cuisine KDS
                      _buildPlaceholderPage("Cuisine (KDS)", Icons.restaurant_menu),"""
new_kds = """                      // TAB 2: Cuisine KDS
                      const KitchenKdsPage(),"""
text = text.replace(old_kds, new_kds)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

