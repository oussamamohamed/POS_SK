import 'package:equatable/equatable.dart';
import '../../../domain/entities/order_item.dart';
import 'input_buffer_state.dart';

class PosTerminalState extends Equatable {
  final List<OrderItem> cartItems;
  final String activeTable;
  final InputBufferState numericBuffer;
  final bool isLeftHandedMode;
  final int totalTtcCents;
  
  const PosTerminalState({
    this.cartItems = const [],
    this.activeTable = '',
    this.numericBuffer = const InputBufferState.empty(),
    this.isLeftHandedMode = false,
    this.totalTtcCents = 0,
  });

  PosTerminalState copyWith({
    List<OrderItem>? cartItems,
    String? activeTable,
    InputBufferState? numericBuffer,
    bool? isLeftHandedMode,
    int? totalTtcCents,
  }) {
    return PosTerminalState(
      cartItems: cartItems ?? this.cartItems,
      activeTable: activeTable ?? this.activeTable,
      numericBuffer: numericBuffer ?? this.numericBuffer,
      isLeftHandedMode: isLeftHandedMode ?? this.isLeftHandedMode,
      totalTtcCents: totalTtcCents ?? this.totalTtcCents,
    );
  }

  @override
  List<Object?> get props => [
    cartItems, 
    activeTable, 
    numericBuffer, 
    isLeftHandedMode, 
    totalTtcCents
  ];
}
