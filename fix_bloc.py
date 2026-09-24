with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_bloc.dart", "r") as f:
    lines = f.readlines()

# The class ends at a certain point.
new_lines = []
func = []
recording = False
for line in lines:
    if "void _onSendToKitchen" in line:
        recording = True
    
    if recording:
        func.append(line)
    else:
        new_lines.append(line)

# new_lines has "}\n" at the end. We insert func before it.
for i in range(len(new_lines)-1, -1, -1):
    if new_lines[i].strip() == "}":
        new_lines.insert(i, "".join(func))
        break

with open("src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal/pos_terminal_bloc.dart", "w") as f:
    f.writelines(new_lines)
