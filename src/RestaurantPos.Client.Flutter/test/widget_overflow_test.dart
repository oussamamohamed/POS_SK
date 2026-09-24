import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:restaurantpos_client/main.dart';
import 'package:restaurantpos_client/data/local_db/local_journal_database.dart';
import 'package:restaurantpos_client/data/network/table_hub_client.dart';

void main() {
  testWidgets('Check POS interface for layout overflows on iPad size', (WidgetTester tester) async {
    // iPad Pro 13-inch (M4/M5) landscape resolution is typically 1376x1032 or similar. 
    // Setting logical size:
    tester.view.physicalSize = const Size(2752, 2064); 
    tester.view.devicePixelRatio = 2.0;
    
    // Catch UI errors
    final List<FlutterErrorDetails> errors = [];
    final originalOnError = FlutterError.onError;
    FlutterError.onError = (FlutterErrorDetails details) {
      errors.add(details);
      if (originalOnError != null) originalOnError(details);
    };

    final db = LocalJournalDatabase();
    final hub = TableHubClient();
    
    await tester.pumpWidget(PosApp(db: db, hub: hub));
    await tester.pumpAndSettle();

    // Check tabs
    final tabs = ['Caisse', 'Plan de Salle', 'Cuisine KDS', 'Paramétrage', 'Fiscalité'];
    for (String tab in tabs) {
       final finder = find.text(tab);
       if (finder.evaluate().isNotEmpty) {
           await tester.tap(finder.first);
           await tester.pumpAndSettle();
       }
    }
    
    // Reset view
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
    FlutterError.onError = originalOnError;

    if (errors.isNotEmpty) {
      for (var e in errors) {
        print("UI ERROR DETECTED: ${e.exceptionAsString()}");
      }
      fail("Found ${errors.length} layout errors.");
    } else {
      print("SUCCESS: No layout overflows detected.");
    }
  });
}
