import re

# 1. Update pos_terminal_event.dart
with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_event.dart", "r") as f:
    events = f.read()

if "class SendToKitchenEvent" not in events:
    events += "\nclass SendToKitchenEvent extends PosTerminalEvent {}\n"
    with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_event.dart", "w") as f:
        f.write(events)

# 2. Update pos_terminal_bloc.dart
with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_bloc.dart", "r") as f:
    bloc = f.read()

if "on<SendToKitchenEvent>(" not in bloc:
    bloc = bloc.replace("on<ToggleHandednessEvent>(_onToggleHandedness);", "on<ToggleHandednessEvent>(_onToggleHandedness);\n    on<SendToKitchenEvent>(_onSendToKitchen);")
    
    send_kitchen_func = """
  void _onSendToKitchen(SendToKitchenEvent event, Emitter<PosTerminalState> emit) {
    if (state.cartItems.isEmpty) return;
    
    List<OrderItem> updatedItems = state.cartItems.map((item) {
      return item.copyWith(isDispatched: true);
    }).toList();
    
    emit(state.copyWith(cartItems: updatedItems));
  }
"""
    bloc += send_kitchen_func
    
    with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_bloc.dart", "w") as f:
        f.write(bloc)

# 3. Update cart_swipe_item.dart
with open("src/RestaurantPos.Client.Flutter/lib/presentation/widgets/cart_swipe_item.dart", "r") as f:
    swipe_item = f.read()

old_badge = """                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
                      decoration: BoxDecoration(
                        color: const Color(0x1AF59E0B),
                        border: Border.all(color: const Color(0x33F59E0B)),
                        borderRadius: BorderRadius.circular(2),
                      ),
                      child: const Text("➕ Nouveau", style: TextStyle(color: Color(0xFFFBBF24), fontSize: 10, fontWeight: FontWeight.bold)),
                    )"""

new_badge = """                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
                      decoration: BoxDecoration(
                        color: item.isDispatched ? const Color(0x1A10B981) : const Color(0x1AF59E0B),
                        border: Border.all(color: item.isDispatched ? const Color(0x3310B981) : const Color(0x33F59E0B)),
                        borderRadius: BorderRadius.circular(2),
                      ),
                      child: Text(item.isDispatched ? "✓ En cuisine" : "➕ Nouveau", style: TextStyle(color: item.isDispatched ? const Color(0xFF34D399) : const Color(0xFFFBBF24), fontSize: 10, fontWeight: FontWeight.bold)),
                    )"""

swipe_item = swipe_item.replace(old_badge, new_badge)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/widgets/cart_swipe_item.dart", "w") as f:
    f.write(swipe_item)

# 4. Update pos_terminal_page.dart (Actions)
with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    page = f.read()

# Make "Envoyer Cuisine" work:
old_action_cuisine = """_buildCartAction(context, "📤", "Envoyer Cuisine", const Color(0xFF0284C7), Colors.white),"""
new_action_cuisine = """_buildCartAction(context, "📤", "Envoyer Cuisine", const Color(0xFF0284C7), Colors.white, onTapOverride: () {
                      context.read<PosTerminalBloc>().add(SendToKitchenEvent());
                      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Bons de commande envoyés en cuisine.")));
                    }),"""

# Make "Attente" work:
old_action_attente = """_buildCartAction(context, "⏸️", "Attente", const Color(0xFF1E293B), const Color(0xFF94A3B8)),"""
new_action_attente = """_buildCartAction(context, "⏸️", "Attente", const Color(0xFF1E293B), const Color(0xFF94A3B8), onTapOverride: () {
                      context.read<PosTerminalBloc>().add(ClearCartEvent());
                      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Commande mise en attente (Parkée).")));
                    }),"""


page = page.replace(old_action_cuisine, new_action_cuisine)
page = page.replace(old_action_attente, new_action_attente)

# Add onTapOverride support to _buildCartAction
old_build_cart_action = """  Widget _buildCartAction(BuildContext context, String icon, String label, Color bgColor, Color textColor) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Action: $label"), duration: const Duration(seconds: 1)));
        },"""

new_build_cart_action = """  Widget _buildCartAction(BuildContext context, String icon, String label, Color bgColor, Color textColor, {VoidCallback? onTapOverride}) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          if (onTapOverride != null) {
            onTapOverride();
          } else {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Action: $label"), duration: const Duration(seconds: 1)));
          }
        },"""

page = page.replace(old_build_cart_action, new_build_cart_action)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(page)

