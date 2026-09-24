import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'core/utils/pos_bloc_observer.dart';
import 'presentation/blocs/pos_terminal/pos_terminal_bloc.dart';
import 'presentation/blocs/sync/sync_bloc.dart';
import 'presentation/pages/pos_terminal_page.dart';
import 'data/local_db/local_journal_database.dart';
import 'data/network/table_hub_client.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  Bloc.observer = PosBlocObserver();

  // DI
  final db = LocalJournalDatabase();
  final hub = TableHubClient();

  runApp(PosApp(db: db, hub: hub));
}

class PosApp extends StatelessWidget {
  final LocalJournalDatabase db;
  final TableHubClient hub;

  const PosApp({super.key, required this.db, required this.hub});

  @override
  Widget build(BuildContext context) {
    return MultiBlocProvider(
      providers: [
        BlocProvider(create: (_) => PosTerminalBloc()),
        BlocProvider(create: (_) => SyncBloc(db: db, network: hub)..add(ConnectSignalREvent())),
      ],
      child: MaterialApp(
        title: 'AGY POS Flutter',
        theme: ThemeData.light(),
        themeMode: ThemeMode.system,
        home: const PosTerminalPage(),
      ),
    );
  }
}
