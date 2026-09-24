import re

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'r') as f:
    content = f.read()

# Replace DataTemplate Grid with SwipeView
pattern = r'(ItemTemplate = new DataTemplate\(\(\) =>\s*\{\s*)(var row = new Grid)'
replacement = r"""\1var swipeDeleteBtn = new SwipeItem
                {
                    Text = "Supprimer",
                    BackgroundColor = Colors.Red,
                    IconImageSource = "trash.png"
                };
                swipeDeleteBtn.SetBinding(MenuItem.CommandProperty, new Binding("BindingContext.RemoveItemCommand", source: new RelativeBindingSource(RelativeBindingSourceMode.FindAncestorBindingContext, typeof(PosTerminalViewModel))));
                swipeDeleteBtn.SetBinding(MenuItem.CommandParameterProperty, new Binding("."));
                var swipeItems = new SwipeItems { swipeDeleteBtn };
                \2"""

content = re.sub(pattern, replacement, content)

pattern2 = r'(var frame = new Frame\s*\{\s*Padding = 0,\s*CornerRadius = 8,\s*BackgroundColor = Microsoft\.Maui\.Graphics\.Color\.FromArgb\("#1E293B"\),\s*BorderColor = Microsoft\.Maui\.Graphics\.Colors\.Transparent,\s*HasShadow = false,\s*Content = row\s*\};.*?)(return frame;\s*\})'
replacement2 = r"""\1var swipeView = new SwipeView
                {
                    RightItems = swipeItems,
                    Content = frame
                };
                return swipeView;
            }"""

content = re.sub(pattern2, replacement2, content, flags=re.DOTALL)

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'w') as f:
    f.write(content)
