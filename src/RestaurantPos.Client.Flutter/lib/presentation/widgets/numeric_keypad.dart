import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';

class NumericKeypad extends StatelessWidget {
  const NumericKeypad({super.key});

  @override
  Widget build(BuildContext context) {
    return GridView.count(
      crossAxisCount: 3,
      childAspectRatio: 1.2,
      crossAxisSpacing: 12,
      mainAxisSpacing: 12,
      physics: const NeverScrollableScrollPhysics(),
      children: [
        _buildKey(context, '7'),
        _buildKey(context, '8'),
        _buildKey(context, '9'),
        _buildKey(context, '4'),
        _buildKey(context, '5'),
        _buildKey(context, '6'),
        _buildKey(context, '1'),
        _buildKey(context, '2'),
        _buildKey(context, '3'),
        _buildKey(context, 'C', color: const Color(0xFFEF4444)),
        _buildKey(context, '0'),
        _buildKey(context, '⌫', color: const Color(0xFFF59E0B)),
      ],
    );
  }

  Widget _buildKey(BuildContext context, String label, {Color? color}) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          if (label == 'C') {
            context.read<PosTerminalBloc>().add(NumpadClearEvent());
          } else if (label == '⌫') {
             context.read<PosTerminalBloc>().add(NumpadBackspaceEvent());
          } else {
            context.read<PosTerminalBloc>().add(NumpadDigitPressedEvent(int.parse(label)));
          }
        },
        borderRadius: BorderRadius.circular(12),
        child: Ink(
          decoration: BoxDecoration(
            color: const Color(0xFF334155),
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: const Color(0x14FFFFFF)),
          ),
          child: Center(
            child: Text(
              label,
              style: TextStyle(
                fontSize: 24,
                fontWeight: FontWeight.bold,
                color: color ?? const Color(0xFFF8FAFC),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
