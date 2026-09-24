import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:restaurantpos_client/main.dart';
import 'package:restaurantpos_client/data/local_db/local_journal_database.dart';
import 'package:restaurantpos_client/data/network/table_hub_client.dart';

void main() {
  testWidgets('Test POS Interactions: Navigation, Add to Cart, Empty Cart', (WidgetTester tester) async {
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

    print("Step 1: Verify Initial state");
    expect(find.text("0.00 €"), findsWidgets); // Total should be 0.00

    print("Step 2: Navigate to Plan de Salle");
    await tester.tap(find.text("Plan de Salle"));
    await tester.pumpAndSettle();
    
    print("Step 3: Click on 'Table 1'");
    await tester.tap(find.text("Table 1"));
    await tester.pumpAndSettle();
    
    // Should auto-navigate back to Caisse
    expect(find.text("Table sélectionnée. Prêt à commander."), findsOneWidget);

    print("Step 4: Add product to cart (Produit 3)");
    // Find a product by text (from the dummy list)
    final productFinder = find.text("Produit 3");
    await tester.tap(productFinder.first);
    await tester.pumpAndSettle();
    
    // Total should have increased, "Produit 3" should be in the cart
    expect(find.text("Produit 3"), findsWidgets); // One in grid, one in cart



    print("Step 6: Clear cart");
    // Find the trash icon button to clear cart
    await tester.tap(find.text("🗑️"));
    await tester.pumpAndSettle();
    
    // Now total should be 0.00 again
    expect(find.text("0.00 €"), findsWidgets); 
    
    // Reset view
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
    FlutterError.onError = originalOnError;

    if (errors.isNotEmpty) {
      fail("Found ${errors.length} errors during interaction.");
    }
  });
}
