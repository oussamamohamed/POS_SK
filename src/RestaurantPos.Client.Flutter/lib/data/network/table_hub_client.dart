import 'package:signalr_netcore/signalr_client.dart';
import 'package:flutter/foundation.dart';

class TableHubClient {
  late HubConnection _hubConnection;

  // Connection URL maps typical .NET API Localhost
  TableHubClient({String serverUrl = "http://localhost:5038/hubs/table"}) {
    _hubConnection = HubConnectionBuilder()
        .withUrl(serverUrl)
        .build();

    _hubConnection.onclose(({error}) {
      debugPrint("SignalR Connection Closed: $error");
    });
  }

  Future<void> connect() async {
    try {
      if (_hubConnection.state == HubConnectionState.Disconnected) {
        await _hubConnection.start();
        debugPrint("SignalR Connected!");
      }
    } catch (e) {
      debugPrint("SignalR Connection Failed: $e");
    }
  }

  void onTableUpdated(void Function(List<Object?>?) callback) {
    _hubConnection.on("ReceiveTableUpdate", callback);
  }
}
