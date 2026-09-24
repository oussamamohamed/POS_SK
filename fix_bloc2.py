import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_bloc.dart", "r") as f:
    text = f.read()

handler = """
  void _onClearCart(ClearCartEvent event, Emitter<PosTerminalState> emit) {
    emit(state.copyWith(
      cartItems: [],
      totalTtcCents: 0,
    ));
  }

  void _onToggleHandedness(ToggleHandednessEvent event, Emitter<PosTerminalState> emit) {"""

text = text.replace("  void _onToggleHandedness(ToggleHandednessEvent event, Emitter<PosTerminalState> emit) {", handler)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_bloc.dart", "w") as f:
    f.write(text)

