import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

imports = """import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';
import '../blocs/pos_terminal/pos_terminal_state.dart';
import 'table_map_page.dart';
import 'kitchen_kds_page.dart';"""

text = text.replace("import 'package:flutter_bloc/flutter_bloc.dart';\nimport '../blocs/pos_terminal/pos_terminal_bloc.dart';\nimport '../blocs/pos_terminal/pos_terminal_event.dart';\nimport '../blocs/pos_terminal/pos_terminal_state.dart';", imports)

old_body = """              Expanded(
                child: Row(
                  children: [
                    if (state.isLeftHandedMode) _buildCartPanel(context, state),
                    _buildMainPanel(context, state),
                    if (!state.isLeftHandedMode) _buildCartPanel(context, state),
                  ],
                ),
              )"""

new_body = """              Expanded(
                child: IndexedStack(
                  index: _currentTabIndex,
                  children: [
                    Row(
                      children: [
                        if (state.isLeftHandedMode) _buildCartPanel(context, state),
                        _buildMainPanel(context, state),
                        if (!state.isLeftHandedMode) _buildCartPanel(context, state),
                      ],
                    ),
                    TableMapPage(onTableSelected: () {
                        setState(() { _currentTabIndex = 0; });
                        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Table 1", style: TextStyle(fontSize: 1)), duration: Duration(seconds: 0)));
                        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Table sélectionnée. Prêt à commander."), duration: Duration(seconds: 2)));
                    }),
                    const KitchenKdsPage(),
                    _buildPlaceholderPage("Paramétrage", Icons.settings),
                    _buildPlaceholderPage("Fiscalité", Icons.receipt_long),
                  ],
                ),
              )"""

if old_body in text:
    text = text.replace(old_body, new_body)
else:
    print("WARNING: Could not find old body")

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

