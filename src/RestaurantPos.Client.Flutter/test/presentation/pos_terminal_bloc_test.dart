import 'package:flutter_test/flutter_test.dart';
import 'package:restaurantpos_client/presentation/blocs/pos_terminal/pos_terminal_bloc.dart';
import 'package:restaurantpos_client/presentation/blocs/pos_terminal/input_buffer_state.dart';
import 'package:restaurantpos_client/presentation/blocs/pos_terminal/pos_terminal_event.dart';

void main() {
  group('PosTerminalBloc', () {
    late PosTerminalBloc bloc;

    setUp(() {
      bloc = PosTerminalBloc();
    });

    tearDown(() {
      bloc.close();
    });

    test('initial state is correct', () {
      expect(bloc.state.cartItems, isEmpty);
      expect(bloc.state.totalTtcCents, 0);
      expect(bloc.state.isLeftHandedMode, false);
      expect(bloc.state.numericBuffer.activeField, InputBufferTarget.none);
    });

    test('AddProductEvent adds item to cart and updates total', () {
      bloc.add(const AddProductEvent(
        productId: 'P1',
        productName: 'Café',
        unitPriceCents: 250,
      ));

      expectLater(
        bloc.stream,
        emitsInOrder([
          predicate<dynamic>((state) => 
            state.cartItems.length == 1 &&
            state.cartItems[0].productName == 'Café' &&
            state.totalTtcCents == 250
          ),
        ]),
      );
    });
  });
}
