import 'package:equatable/equatable.dart';
import '../../../domain/entities/order_item.dart';

abstract class PosTerminalEvent extends Equatable {
  const PosTerminalEvent();
  
  @override
  List<Object?> get props => [];
}

class AddProductEvent extends PosTerminalEvent {
  final String productId;
  final String productName;
  final int unitPriceCents;
  
  const AddProductEvent({
    required this.productId,
    required this.productName,
    required this.unitPriceCents,
  });

  @override
  List<Object?> get props => [productId, productName, unitPriceCents];
}

class RemoveCartItemEvent extends PosTerminalEvent {
  final OrderItem item;
  const RemoveCartItemEvent(this.item);

  @override
  List<Object?> get props => [item];
}

class ToggleHandednessEvent extends PosTerminalEvent {}

class NumpadDigitPressedEvent extends PosTerminalEvent {
  final int digit;
  const NumpadDigitPressedEvent(this.digit);

  @override
  List<Object?> get props => [digit];
}

class NumpadBackspaceEvent extends PosTerminalEvent {}
class NumpadClearEvent extends PosTerminalEvent {}

class ClearCartEvent extends PosTerminalEvent {}

class SendToKitchenEvent extends PosTerminalEvent {}
