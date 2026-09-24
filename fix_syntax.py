import re

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

old_code = """                _buildNavTab(context, "📜", "Fiscalité", 4),
              ],
            ),
          ),
          
          Row("""

new_code = """                _buildNavTab(context, "📜", "Fiscalité", 4),
                  ],
                ),
              ),
            ),
          ),
          
          Row("""

text = text.replace(old_code, new_code)

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(text)

