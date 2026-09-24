import 'package:equatable/equatable.dart';

class OrderItem extends Equatable {
  final String id;
  final String productId;
  final String productName;
  final int unitPriceCents;
  final int quantity;
  final List<String> selectedModifiers;
  final String? kitchenComment;
  final bool isDispatched;

  const OrderItem({
    required this.id,
    required this.productId,
    required this.productName,
    required this.unitPriceCents,
    this.quantity = 1,
    this.selectedModifiers = const [],
    this.kitchenComment,
    this.isDispatched = false,
  });

  OrderItem copyWith({
    int? quantity,
    List<String>? selectedModifiers,
    String? kitchenComment,
    bool? isDispatched,
  }) {
    return OrderItem(
      id: id,
      productId: productId,
      productName: productName,
      unitPriceCents: unitPriceCents,
      quantity: quantity ?? this.quantity,
      selectedModifiers: selectedModifiers ?? this.selectedModifiers,
      kitchenComment: kitchenComment ?? this.kitchenComment,
      isDispatched: isDispatched ?? this.isDispatched,
    );
  }

  @override
  List<Object?> get props => [
        id,
        productId,
        productName,
        unitPriceCents,
        quantity,
        selectedModifiers,
        kitchenComment,
        isDispatched,
      ];
}
