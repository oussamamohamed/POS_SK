import re

with open("src/RestaurantPos.Client.Flutter/test/widget_interaction_test.dart", "r") as f:
    text = f.read()

text = text.replace("ENCAISSER", "Encaisser")
text = text.replace("find.byIcon(Icons.delete_outline)", 'find.text("🗑️")')
# Also, after tapping 'Encaisser', the snackbar text is 'Action: Encaisser' according to my previous grep of _buildCartAction
text = text.replace("Fonction d'encaissement (à implémenter)", "Action: Encaisser")

with open("src/RestaurantPos.Client.Flutter/test/widget_interaction_test.dart", "w") as f:
    f.write(text)
