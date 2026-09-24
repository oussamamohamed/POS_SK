import re

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'r') as f:
    content = f.read()

# Instead of returning swipeView directly, I'll return a layout containing it.
# Or better, remove SwipeView and just use a ContextAction or gesture if SwipeView is broken.
# But let's just wrap it in a ContentView.

pattern = r'var swipeView = new SwipeView.*?return swipeView;\s*\}'
replacement = """var swipeView = new SwipeView
                {
                    RightItems = swipeItems,
                    Content = frame
                };
                
                var rootWrapper = new ContentView 
                { 
                    Content = swipeView 
                };
                return rootWrapper;
            }"""

content = re.sub(pattern, replacement, content, flags=re.DOTALL)

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'w') as f:
    f.write(content)
