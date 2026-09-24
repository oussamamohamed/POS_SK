import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:uuid/uuid.dart';
import 'pos_terminal_event.dart';
import 'pos_terminal_state.dart';
import 'input_buffer_state.dart';
import '../../../domain/entities/order_item.dart';

class PosTerminalBloc extends Bloc<PosTerminalEvent, PosTerminalState> {
  final Uuid _uuid = const Uuid();

  PosTerminalBloc() : super(const PosTerminalState()) {
    on<AddProductEvent>(_onAddProduct);
    on<RemoveCartItemEvent>(_onRemoveCartItem);
    on<ClearCartEvent>(_onClearCart);
    on<ToggleHandednessEvent>(_onToggleHandedness);
    on<SendToKitchenEvent>(_onSendToKitchen);
    on<NumpadDigitPressedEvent>(_onNumpadDigitPressed);
    on<NumpadBackspaceEvent>(_onNumpadBackspace);
    on<NumpadClearEvent>(_onNumpadClear);
  }

  void _onAddProduct(AddProductEvent event, Emitter<PosTerminalState> emit) {
    // Check if identical un-dispatched product exists to increment quantity
    final existingIndex = state.cartItems.indexWhere((i) => 
      i.productId == event.productId && 
      !i.isDispatched && 
      i.selectedModifiers.isEmpty && 
      i.kitchenComment == null
    );

    List<OrderItem> updatedItems = List.from(state.cartItems);

    if (existingIndex >= 0) {
      final existingItem = updatedItems[existingIndex];
      updatedItems[existingIndex] = existingItem.copyWith(quantity: existingItem.quantity + 1);
    } else {
      updatedItems.add(OrderItem(
        id: _uuid.v7(),
        productId: event.productId,
        productName: event.productName,
        unitPriceCents: event.unitPriceCents,
        quantity: 1,
      ));
    }

    emit(state.copyWith(
      cartItems: updatedItems,
      totalTtcCents: _calculateTotal(updatedItems),
    ));
  }

  void _onRemoveCartItem(RemoveCartItemEvent event, Emitter<PosTerminalState> emit) {
    if (event.item.isDispatched) return;
    
    List<OrderItem> updatedItems = List.from(state.cartItems);
    updatedItems.removeWhere((i) => i.id == event.item.id);
    
    emit(state.copyWith(
      cartItems: updatedItems,
      totalTtcCents: _calculateTotal(updatedItems),
    ));
  }


  void _onClearCart(ClearCartEvent event, Emitter<PosTerminalState> emit) {
    emit(state.copyWith(
      cartItems: [],
      totalTtcCents: 0,
    ));
  }

  void _onToggleHandedness(ToggleHandednessEvent event, Emitter<PosTerminalState> emit) {
    emit(state.copyWith(isLeftHandedMode: !state.isLeftHandedMode));
  }

  void _onNumpadDigitPressed(NumpadDigitPressedEvent event, Emitter<PosTerminalState> emit) {
    if (state.numericBuffer.activeField == InputBufferTarget.none) return;
    
    final newBuffer = state.numericBuffer.currentBuffer + event.digit.toString();
    emit(state.copyWith(
      numericBuffer: InputBufferState(
        activeField: state.numericBuffer.activeField,
        currentBuffer: newBuffer,
      ),
    ));
  }

  void _onNumpadBackspace(NumpadBackspaceEvent event, Emitter<PosTerminalState> emit) {
    if (state.numericBuffer.activeField == InputBufferTarget.none || state.numericBuffer.currentBuffer.isEmpty) return;
    
    final newBuffer = state.numericBuffer.currentBuffer.substring(0, state.numericBuffer.currentBuffer.length - 1);
    emit(state.copyWith(
      numericBuffer: InputBufferState(
        activeField: state.numericBuffer.activeField,
        currentBuffer: newBuffer,
      ),
    ));
  }
  
  void _onNumpadClear(NumpadClearEvent event, Emitter<PosTerminalState> emit) {
    if (state.numericBuffer.activeField == InputBufferTarget.none) return;
    
    emit(state.copyWith(
      numericBuffer: InputBufferState(
        activeField: state.numericBuffer.activeField,
        currentBuffer: '',
      ),
    ));
  }

  int _calculateTotal(List<OrderItem> items) {
    return items.fold(0, (total, item) => total + (item.unitPriceCents * item.quantity));
  }
  void _onSendToKitchen(SendToKitchenEvent event, Emitter<PosTerminalState> emit) {
    if (state.cartItems.isEmpty) return;
    
    List<OrderItem> updatedItems = state.cartItems.map((item) {
      return item.copyWith(isDispatched: true);
    }).toList();
    
    emit(state.copyWith(cartItems: updatedItems));
  }
}

