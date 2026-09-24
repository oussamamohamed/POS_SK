import 'package:equatable/equatable.dart';

enum InputBufferTarget { none, quantity, price, comment }

class InputBufferState extends Equatable {
  final InputBufferTarget activeField;
  final String currentBuffer;

  const InputBufferState({
    required this.activeField,
    required this.currentBuffer,
  });

  const InputBufferState.empty()
      : activeField = InputBufferTarget.none,
        currentBuffer = '';

  @override
  List<Object?> get props => [activeField, currentBuffer];
}
