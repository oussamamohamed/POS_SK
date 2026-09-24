import os

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "r") as f:
    text = f.read()

# We completely rewrite pos_terminal_page.dart to include the web header and elements 
pos_terminal = """import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';
import '../blocs/pos_terminal/pos_terminal_state.dart';
import '../widgets/numeric_keypad.dart';
import '../widgets/cart_swipe_item.dart';

const bgColor = Color(0xFF0F172A);
const panelColor = Color(0xFF1E293B);
const cardColor = Color(0xFF334155);
const borderColor = Color(0x14FFFFFF);
const primaryColor = Color(0xFF3B82F6);
const textColor = Color(0xFFF8FAFC);
const textMuted = Color(0xFF94A3B8);
const successColor = Color(0xFF10B981);
const dangerColor = Color(0xFFEF4444);
const accentColor = Color(0xFFF59E0B);

class PosTerminalPage extends StatelessWidget {
  const PosTerminalPage({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<PosTerminalBloc, PosTerminalState>(
      builder: (context, state) {
        return Scaffold(
          backgroundColor: bgColor,
          body: Column(
            children: [
              _buildAppHeader(),
              Expanded(
                child: Row(
                  children: [
                    if (state.isLeftHandedMode) _buildCartPanel(context, state),
                    _buildMainPanel(context, state),
                    if (!state.isLeftHandedMode) _buildCartPanel(context, state),
                  ],
                ),
              )
            ],
          ),
        );
      },
    );
  }

  Widget _buildAppHeader() {
    return Container(
      height: 68,
      padding: const EdgeInsets.symmetric(horizontal: 20),
      decoration: const BoxDecoration(
        color: Color(0xD91E293B),
        border: Border(bottom: BorderSide(color: borderColor)),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Row(
            children: [
              Container(
                width: 28, height: 28,
                decoration: BoxDecoration(
                  color: primaryColor,
                  borderRadius: BorderRadius.circular(7),
                  border: Border.all(color: const Color(0x8038BDF8)),
                ),
                child: const Icon(Icons.restaurant, size: 16, color: Colors.white),
              ),
              const SizedBox(width: 10),
              RichText(
                text: const TextSpan(
                  style: TextStyle(color: textColor, fontSize: 18, fontWeight: FontWeight.bold, fontFamily: 'Plus Jakarta Sans'),
                  children: [
                    TextSpan(text: 'AGY ', style: TextStyle(color: primaryColor)),
                    TextSpan(text: 'POS'),
                  ]
                )
              ),
              const SizedBox(width: 16),
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: const Color(0x1F10B981),
                  border: Border.all(color: const Color(0x4D10B981)),
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Row(
                  children: [
                    Container(width: 6, height: 6, decoration: const BoxDecoration(color: successColor, shape: BoxShape.circle)),
                    const SizedBox(width: 6),
                    const Text('En Ligne', style: TextStyle(color: successColor, fontSize: 11, fontWeight: FontWeight.bold)),
                  ],
                ),
              )
            ],
          ),
          
          Container(
            padding: const EdgeInsets.all(4),
            decoration: BoxDecoration(
              color: const Color(0x990F172A),
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: borderColor),
            ),
            child: Row(
              children: [
                _buildNavTab("🛒", "Caisse", true),
                _buildNavTab("🗺️", "Plan de Salle", false),
                _buildNavTab("👨‍🍳", "Cuisine KDS", false),
                _buildNavTab("⚙️", "Paramétrage", false),
                _buildNavTab("📜", "Fiscalité NF525", false),
              ],
            ),
          ),
          
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                decoration: BoxDecoration(
                  color: const Color(0x0FFFFFFF),
                  borderRadius: BorderRadius.circular(30),
                  border: Border.all(color: borderColor),
                ),
                child: Row(
                  children: [
                    const Text("👤", style: TextStyle(fontSize: 16)),
                    const SizedBox(width: 10),
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: const [
                         Text("Alexandre D.", style: TextStyle(color: textColor, fontSize: 13, fontWeight: FontWeight.bold)),
                         Text("MANAGER", style: TextStyle(color: accentColor, fontSize: 10, fontWeight: FontWeight.bold)),
                      ]
                    )
                  ],
                ),
              ),
              const SizedBox(width: 12),
              Container(
                width: 42, height: 42,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: const Color(0x26EF4444),
                  border: Border.all(color: borderColor),
                ),
                child: const Icon(Icons.lock, color: dangerColor, size: 18),
              )
            ],
          )
        ],
      )
    );
  }

  Widget _buildNavTab(String icon, String label, bool active) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
      decoration: BoxDecoration(
        color: active ? primaryColor : Colors.transparent,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Text(icon, style: const TextStyle(fontSize: 14)),
          const SizedBox(width: 8),
          Text(label, style: TextStyle(
            color: active ? Colors.white : textMuted, 
            fontWeight: FontWeight.bold, 
            fontSize: 14)
          ),
        ],
      ),
    );
  }

  Widget _buildCartPanel(BuildContext context, PosTerminalState state) {
    final double total = (state.totalTtcCents) / 100.0;

    return Container(
      width: 380,
      decoration: const BoxDecoration(
        color: panelColor,
        border: Border(right: BorderSide(color: borderColor), left: BorderSide(color: borderColor)),
      ),
      child: Column(
        children: [
          // Header Cart
          Container(
            padding: const EdgeInsets.all(16),
            decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: borderColor))),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Row(
                  crossAxisAlignment: CrossAxisAlignment.center,
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
                      decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(6)),
                      child: const Text("Comptoir", style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 14)),
                    ),
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.all(2),
                      decoration: BoxDecoration(
                        color: const Color(0x990F172A),
                        borderRadius: BorderRadius.circular(6),
                        border: Border.all(color: borderColor),
                      ),
                      child: Row(
                        children: [
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                            decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(4)),
                            child: const Text("🥡 À Emporter", style: TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.bold)),
                          ),
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                            child: const Text("🍽️ Sur Place", style: TextStyle(color: textMuted, fontSize: 11, fontWeight: FontWeight.bold)),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                InkWell(
                  onTap: () => context.read<PosTerminalBloc>().add(ToggleHandednessEvent()),
                  child: Container(
                    padding: const EdgeInsets.all(4),
                    decoration: BoxDecoration(color: cardColor, borderRadius: BorderRadius.circular(6)),
                    child: const Icon(Icons.swap_horiz, color: textMuted, size: 20),
                  ),
                )
              ],
            ),
          ),
          
          Expanded(
            child: state.cartItems.isEmpty
                ? Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(Icons.receipt_long, size: 64, color: textMuted.withAlpha(128)),
                        const SizedBox(height: 16),
                        const Text("La commande est vide", style: TextStyle(color: textMuted, fontSize: 16)),
                      ],
                    ),
                  )
                : ListView.builder(
                    padding: const EdgeInsets.symmetric(vertical: 12),
                    itemCount: state.cartItems.length,
                    itemBuilder: (context, index) => CartSwipeItem(
                      item: state.cartItems[index],
                      onDelete: () => context.read<PosTerminalBloc>().add(RemoveCartItemEvent(state.cartItems[index])),
                    ),
                  ),
          ),
          
          Container(
            padding: const EdgeInsets.all(16),
            decoration: const BoxDecoration(
              color: Color(0xB20F172A),
              border: Border(top: BorderSide(color: borderColor)),
            ),
            child: Column(
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text("Total à payer", style: TextStyle(color: textMuted, fontSize: 14)),
                    Text("${total.toStringAsFixed(2)} €", style: const TextStyle(color: successColor, fontSize: 28, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                  ],
                ),
                const SizedBox(height: 12),
                
                Row(
                  children: [
                    Expanded(
                      child: Container(
                        height: 48,
                        decoration: BoxDecoration(color: const Color(0xFF06B6D4), borderRadius: BorderRadius.circular(8)),
                        alignment: Alignment.center,
                        child: const Text("TICKET", style: TextStyle(fontWeight: FontWeight.bold, color: Colors.white, fontSize: 13)),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      flex: 2,
                      child: Container(
                        height: 48,
                        decoration: BoxDecoration(color: successColor, borderRadius: BorderRadius.circular(8)),
                        alignment: Alignment.center,
                        child: const Text("ENCAISSER", style: TextStyle(fontWeight: FontWeight.bold, color: Colors.white, fontSize: 13)),
                      ),
                    ),
                  ],
                )
              ],
            ),
          )
        ],
      ),
    );
  }

  Widget _buildMainPanel(BuildContext context, PosTerminalState state) {
    return Expanded(
      child: Column(
        children: [
          // Quick Keys
          Container(
            padding: const EdgeInsets.only(top: 16, bottom: 8, left: 16, right: 16),
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: [
                  _buildQuickKey("⚡", "Café Express (1.50€)"),
                  _buildQuickKey("⚡", "Croissant (1.80€)"),
                  _buildQuickKey("⚡", "Menu Midi (14.50€)"),
                ],
              ),
            ),
          ),
          
          // Category Tabs
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                _buildCatTab("Tous", true),
                _buildCatTab("Plats", false),
                _buildCatTab("Boissons", false),
                _buildCatTab("Desserts", false),
              ],
            ),
          ),
          
          Expanded(
            child: Row(
              children: [
                Expanded(
                  child: Container(
                    margin: const EdgeInsets.all(16),
                    padding: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      color: const Color(0x730F172A),
                      border: Border.all(color: borderColor),
                      borderRadius: BorderRadius.circular(8),
                    ),
                    child: Column(
                      children: [
                        Expanded(
                          child: GridView.builder(
                            gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                              crossAxisCount: 3,
                              childAspectRatio: 0.9,
                              crossAxisSpacing: 8,
                              mainAxisSpacing: 8,
                            ),
                            itemCount: 9,
                            itemBuilder: (context, index) {
                              return InkWell(
                                onTap: () {
                                  context.read<PosTerminalBloc>().add(AddProductEvent(
                                    productId: "p$index", productName: "Produit ${index+1}", unitPriceCents: 1050 + (index * 100)
                                  ));
                                },
                                child: Container(
                                  decoration: BoxDecoration(
                                    color: panelColor,
                                    border: Border.all(color: const Color(0x1EFFFFFF)),
                                    borderRadius: BorderRadius.zero, 
                                  ),
                                  padding: const EdgeInsets.all(8),
                                  child: Column(
                                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                                    children: [
                                      const Align(alignment: Alignment.topRight, child: Text("BAR", style: TextStyle(fontSize: 10, color: textMuted))),
                                      const Icon(Icons.fastfood, color: textMuted, size: 32),
                                      const SizedBox(height: 4),
                                      Text("Produit ${index+1}", 
                                        style: const TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 13),
                                        textAlign: TextAlign.center,
                                        maxLines: 2,
                                        overflow: TextOverflow.ellipsis,
                                      ),
                                      Container(
                                        width: double.infinity,
                                        padding: const EdgeInsets.symmetric(vertical: 4),
                                        decoration: BoxDecoration(
                                          color: const Color(0x1E10B981),
                                          border: Border.all(color: const Color(0x4010B981)),
                                          borderRadius: BorderRadius.circular(2)
                                        ),
                                        child: Text("${((1050 + (index * 100))/100).toStringAsFixed(2)} €", 
                                          textAlign: TextAlign.center,
                                          style: const TextStyle(color: successColor, fontSize: 14, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono'),
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              );
                            },
                          ),
                        ),
                        
                        // Pagination
                        Container(
                          margin: const EdgeInsets.only(top: 12),
                          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                          height: 52,
                          decoration: BoxDecoration(
                            color: const Color(0xD91E293B),
                            border: Border.all(color: borderColor),
                            borderRadius: BorderRadius.circular(12)
                          ),
                          child: Row(
                            mainAxisAlignment: MainAxisAlignment.spaceBetween,
                            children: [
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                                decoration: BoxDecoration(color: cardColor, borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                                child: const Row(children: [Icon(Icons.chevron_left, color: textColor, size: 18), Text(" Précédent", style: TextStyle(color: textColor, fontWeight: FontWeight.bold))]),
                              ),
                              Column(
                                mainAxisAlignment: MainAxisAlignment.center,
                                children: [
                                  const Text("PAGE 1 / 3", style: TextStyle(color: textColor, fontSize: 12, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                                  const SizedBox(height: 4),
                                  Row(
                                    children: [
                                      Container(width: 10, height: 10, decoration: const BoxDecoration(color: primaryColor, shape: BoxShape.circle)),
                                      const SizedBox(width: 6),
                                      Container(width: 10, height: 10, decoration: const BoxDecoration(color: Color(0x33FFFFFF), shape: BoxShape.circle)),
                                      const SizedBox(width: 6),
                                      Container(width: 10, height: 10, decoration: const BoxDecoration(color: Color(0x33FFFFFF), shape: BoxShape.circle)),
                                    ]
                                  )
                                ]
                              ),
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                                decoration: BoxDecoration(color: cardColor, borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                                child: const Row(children: [Text("Suivant ", style: TextStyle(color: textColor, fontWeight: FontWeight.bold)), Icon(Icons.chevron_right, color: textColor, size: 18)]),
                              ),
                            ]
                          )
                        )
                      ],
                    ),
                  ),
                ),
                
                // Numpad Area - wait the Web mockup didn't have Numpad in main pos grid!
                // But the user requested pin-numpad style, so let's keep it here.
                Container(
                  width: 320,
                  decoration: const BoxDecoration(
                    color: panelColor,
                    border: Border(left: BorderSide(color: borderColor)),
                  ),
                  child: Column(
                    children: [
                      Container(
                        padding: const EdgeInsets.all(24),
                        alignment: Alignment.centerRight,
                        decoration: const BoxDecoration(border: Border(bottom: BorderSide(color: borderColor))),
                        height: 100,
                        child: Text(
                          state.numericBuffer.currentBuffer.isEmpty ? "0" : state.numericBuffer.currentBuffer,
                          style: TextStyle(
                            fontSize: 36, 
                            fontWeight: FontWeight.bold, 
                            color: state.numericBuffer.currentBuffer.isEmpty ? textMuted : textColor,
                            fontFamily: 'JetBrains Mono', 
                          ),
                        ),
                      ),
                      const Expanded(
                        child: Padding(
                          padding: EdgeInsets.all(16.0),
                          child: NumericKeypad(),
                        ),
                      ),
                    ],
                  ),
                )
              ],
            ),
          )
        ],
      ),
    );
  }

  Widget _buildQuickKey(String icon, String label) {
    return Container(
      margin: const EdgeInsets.only(right: 8),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
      decoration: BoxDecoration(
        color: const Color(0x1AF59E0B),
        border: Border.all(color: const Color(0x66F59E0B)),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        children: [
          Text(icon, style: const TextStyle(fontSize: 14, color: Color(0xFFFEF08A))),
          const SizedBox(width: 8),
          Text(label, style: const TextStyle(color: Color(0xFFFEF08A), fontWeight: FontWeight.bold, fontSize: 13)),
        ],
      ),
    );
  }

  Widget _buildCatTab(String label, bool active) {
    return Container(
      margin: const EdgeInsets.only(right: 10),
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
      decoration: BoxDecoration(
        color: panelColor,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: active ? Colors.white : borderColor),
      ),
      child: Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 14)),
    );
  }
}
"""

with open("src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart", "w") as f:
    f.write(pos_terminal)
