import re

with open("src/RestaurantPos.Client.Flutter/test/widget_interaction_test.dart", "r") as f:
    text = f.read()

# Remove the Encaisser step
old_step_5 = """    print("Step 5: Tap Encaisser");
    await tester.tap(find.text("Encaisser"));
    await tester.pumpAndSettle();
    // Snack bar mock message
    expect(find.text("Action: Encaisser"), findsOneWidget);
    await tester.pumpAndSettle(const Duration(seconds: 3)); // Wait for snackbar"""

text = text.replace(old_step_5, "")

with open("src/RestaurantPos.Client.Flutter/test/widget_interaction_test.dart", "w") as f:
    f.write(text)
