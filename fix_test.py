import re

with open("src/RestaurantPos.Client.Flutter/test/widget_interaction_test.dart", "r") as f:
    text = f.read()

text = text.replace("Burger Artisan", "Produit 3")

with open("src/RestaurantPos.Client.Flutter/test/widget_interaction_test.dart", "w") as f:
    f.write(text)
