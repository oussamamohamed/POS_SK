with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

text = text.replace(
    '''AddProductEvent(
                              OrderItem(id: "p\\$index", productId: "p\\$index", productName: "Produit \\${index+1}", quantity: 1, unitPriceCents: 1050 + (index * 100))
                            )''',
    '''AddProductEvent(
                              productId: "p\\$index", productName: "Produit \\${index+1}", unitPriceCents: 1050 + (index * 100)
                            )'''
)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)
