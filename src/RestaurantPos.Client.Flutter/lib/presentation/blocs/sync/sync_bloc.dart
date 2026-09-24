import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:equatable/equatable.dart';
import '../../../data/local_db/local_journal_database.dart';
import '../../../data/network/table_hub_client.dart';

abstract class SyncEvent extends Equatable {
  const SyncEvent();
  @override
  List<Object?> get props => [];
}

class ConnectSignalREvent extends SyncEvent {}

class SyncState extends Equatable {
  final bool isConnected;
  const SyncState({this.isConnected = false});
  @override
  List<Object?> get props => [isConnected];
}

class SyncBloc extends Bloc<SyncEvent, SyncState> {
  final LocalJournalDatabase db;
  final TableHubClient network;

  SyncBloc({required this.db, required this.network}) : super(const SyncState()) {
    on<ConnectSignalREvent>((event, emit) async {
      await network.connect();
      emit(const SyncState(isConnected: true)); 
    });
  }
}
