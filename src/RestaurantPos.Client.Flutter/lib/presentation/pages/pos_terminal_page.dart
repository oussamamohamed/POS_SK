import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_bloc.dart';
import '../blocs/pos_terminal/pos_terminal_event.dart';
import '../blocs/pos_terminal/pos_terminal_state.dart';
import 'table_map_page.dart';
import 'kitchen_kds_page.dart';
import 'table_map_page.dart';
import 'kitchen_kds_page.dart';
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

class PosTerminalPage extends StatefulWidget {
  const PosTerminalPage({super.key});

  @override
  State<PosTerminalPage> createState() => _PosTerminalPageState();
}

class _PosTerminalPageState extends State<PosTerminalPage> {
  int _currentTabIndex = 0;
  int _currentPage = 1;
  final int _totalPages = 3;

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<PosTerminalBloc, PosTerminalState>(
      builder: (context, state) {
        return Scaffold(
          backgroundColor: bgColor,
          body: Column(
            children: [
              _buildAppHeader(context),
              Expanded(
                child: IndexedStack(
                  index: _currentTabIndex,
                  children: [
                    Row(
                      children: [
                        if (state.isLeftHandedMode) _buildCartPanel(context, state),
                        _buildMainPanel(context, state),
                        if (!state.isLeftHandedMode) _buildCartPanel(context, state),
                      ],
                    ),
                    TableMapPage(onTableSelected: () {
                        setState(() { _currentTabIndex = 0; });
                        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Table 1", style: TextStyle(fontSize: 1)), duration: Duration(seconds: 0)));
                        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text("Table sélectionnée. Prêt à commander."), duration: Duration(seconds: 2)));
                    }),
                    const KitchenKdsPage(),
                    _buildPlaceholderPage("Paramétrage", Icons.settings),
                    _buildPlaceholderPage("Fiscalité", Icons.receipt_long),
                  ],
                ),
              )
            ],
          ),
        );
      },
    );
  }


  void _showModifiersDialog(BuildContext context, String pId, String pName, int price) {
    showDialog(
      context: context,
      builder: (BuildContext ctx) {
        return AlertDialog(
          backgroundColor: const Color(0xFF1E293B),
          title: Text("⚙️ Modificateurs - $pName", style: const TextStyle(color: Colors.white)),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text("Cuisson :", style: TextStyle(color: Color(0xFF94A3B8))),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                children: [
                  ElevatedButton(onPressed: () {}, style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF0F172A)), child: const Text("Saignant")),
                  ElevatedButton(onPressed: () {}, style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF0F172A)), child: const Text("À point")),
                ],
              ),
              const SizedBox(height: 16),
              const Text("Note Cuisine :", style: TextStyle(color: Color(0xFF94A3B8))),
              const SizedBox(height: 8),
              const TextField(
                style: TextStyle(color: Colors.white),
                decoration: InputDecoration(
                  hintText: "Ex: sans sauce...",
                  hintStyle: TextStyle(color: Color(0xFF475569)),
                  filled: true,
                  fillColor: Color(0xFF0F172A),
                  border: OutlineInputBorder(borderSide: BorderSide.none),
                ),
              )
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(),
              child: const Text("Annuler", style: TextStyle(color: Color(0xFF94A3B8))),
            ),
            ElevatedButton(
              style: ElevatedButton.styleFrom(backgroundColor: const Color(0xFF10B981)),
              onPressed: () {
                context.read<PosTerminalBloc>().add(AddProductEvent(productId: pId, productName: pName, unitPriceCents: price));
                Navigator.of(ctx).pop();
              },
              child: const Text("Valider", style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold)),
            ),
          ],
        );
      },
    );
  }

  Widget _buildPlaceholderPage(String title, IconData icon) {
    return Center(
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, size: 80, color: const Color(0x33FFFFFF)),
          const SizedBox(height: 20),
          Text(title, style: const TextStyle(color: Colors.white, fontSize: 24, fontWeight: FontWeight.bold)),
          const SizedBox(height: 10),
          const Text("En cours de développement...", style: TextStyle(color: Color(0xFF94A3B8), fontSize: 16)),
        ],
      ),
    );
  }

  Widget _buildAppHeader(context) {
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
          
          Expanded(
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Container(
                margin: const EdgeInsets.symmetric(horizontal: 16),
                padding: const EdgeInsets.all(4),
                decoration: BoxDecoration(
                  color: const Color(0x990F172A),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: borderColor),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _buildNavTab(context, "🛒", "Caisse", 0),
                _buildNavTab(context, "🗺️", "Plan de Salle", 1),
                _buildNavTab(context, "👨‍🍳", "Cuisine KDS", 2),
                _buildNavTab(context, "⚙️", "Paramétrage", 3),
                _buildNavTab(context, "📜", "Fiscalité", 4),
                  ],
                ),
              ),
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

  Widget _buildNavTab(BuildContext context, String icon, String label, int tabIndex) {
    bool active = _currentTabIndex == tabIndex;
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          setState(() {
            _currentTabIndex = tabIndex;
          });
        },
        borderRadius: BorderRadius.circular(8),
        child: Ink(
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
        ),
      ),
    );
  }

  Widget _buildCartPanel(BuildContext context, PosTerminalState state) {
    final double totalTtc = (state.totalTtcCents) / 100.0;
    final double totalHt = totalTtc / 1.10;
    final double tva = totalTtc - totalHt;

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
                Expanded(
                  child: SingleChildScrollView(
                    scrollDirection: Axis.horizontal,
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.center,
                      children: [
                        Container(
                          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                          decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(6)),
                          child: const Text("Comptoir", style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold, fontSize: 13)),
                        ),
                        const SizedBox(width: 4),
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
                                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                                decoration: BoxDecoration(color: primaryColor, borderRadius: BorderRadius.circular(4)),
                                child: const Text("🥡 À Emporter", style: TextStyle(color: Colors.white, fontSize: 10, fontWeight: FontWeight.bold)),
                              ),
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                                child: const Text("🍽️ Sur Place", style: TextStyle(color: textMuted, fontSize: 10, fontWeight: FontWeight.bold)),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                Row(
                  children: [
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                      decoration: BoxDecoration(
                        color: const Color(0x26F59E0B),
                        border: Border.all(color: const Color(0x66F59E0B)),
                        borderRadius: BorderRadius.circular(6)
                      ),
                      child: const Text("⏸️ 0", style: TextStyle(color: Color(0xFFFBBF24), fontSize: 14)),
                    ),
                    const SizedBox(width: 8),
                    InkWell(
                      onTap: () { context.read<PosTerminalBloc>().add(ClearCartEvent()); }, // Clear cart
                      child: const Text("🗑️", style: TextStyle(fontSize: 18)),
                    ),
                  ]
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
                      onAdd: () => context.read<PosTerminalBloc>().add(AddProductEvent(
                        productId: state.cartItems[index].productId,
                        productName: state.cartItems[index].productName,
                        unitPriceCents: state.cartItems[index].unitPriceCents,
                      )),
                      onRemove: () => context.read<PosTerminalBloc>().add(RemoveCartItemEvent(state.cartItems[index])),
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
                    const Text("Total HT :", style: TextStyle(color: textMuted, fontSize: 13)),
                    Text("${totalHt.toStringAsFixed(2)} €", style: const TextStyle(color: textMuted, fontSize: 13)),
                  ],
                ),
                const SizedBox(height: 4),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text("TVA (10%) :", style: TextStyle(color: textMuted, fontSize: 13)),
                    Text("${tva.toStringAsFixed(2)} €", style: const TextStyle(color: textMuted, fontSize: 13)),
                  ],
                ),
                const SizedBox(height: 8),
                Container(
                  padding: const EdgeInsets.only(top: 8),
                  decoration: const BoxDecoration(border: Border(top: BorderSide(color: Color(0x33FFFFFF), style: BorderStyle.none))), 
                  // BorderStyle.solid is required for dashed but we'll use a simple top border line
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    crossAxisAlignment: CrossAxisAlignment.center,
                    children: [
                      const Text("TOTAL TTC", style: TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.bold)),
                      Text("${totalTtc.toStringAsFixed(2)} €", style: const TextStyle(color: successColor, fontSize: 22, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                    ],
                  ),
                ),
                const SizedBox(height: 12),
                
                Row(
                  children: [
                    _buildFastCash(context, "10 €"), _buildFastCash(context, "20 €"), _buildFastCash(context, "50 €"), _buildFastCash(context, "Exact"),
                  ],
                ),
                const SizedBox(height: 10),

                GridView.count(
                  crossAxisCount: 3,
                  shrinkWrap: true,
                  physics: const NeverScrollableScrollPhysics(),
                  crossAxisSpacing: 8,
                  mainAxisSpacing: 8,
                  childAspectRatio: 2.2, // wide buttons
                  children: [
                    _buildCartAction(context, "📤", "Cuisine", const Color(0xFF3B82F6), Colors.white),
                    _buildCartAction(context, "⏸️", "Attente", cardColor, Colors.white),
                    _buildCartAction(context, "🏷️", "Remise", const Color(0xFF8B5CF6), Colors.white),
                    _buildCartAction(context, "🔄", "Transférer", const Color(0xFF06B6D4), Colors.white),
                    _buildCartAction(context, "➗", "Split", const Color(0xFFF59E0B), Colors.black),
                    _buildCartAction(context, "💳", "Encaisser", successColor, Colors.white),
                  ],
                )
              ],
            ),
          )
        ],
      ),
    );
  }

  Widget _buildFastCash(BuildContext context, String label) {
    return Expanded(
      child: Container(
        margin: const EdgeInsets.symmetric(horizontal: 3),
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            onTap: () {
              ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Fast Cash: $label"), duration: const Duration(seconds: 1)));
            },
            borderRadius: BorderRadius.circular(8),
            child: Ink(
              padding: const EdgeInsets.symmetric(vertical: 10),
              decoration: BoxDecoration(
                color: const Color(0x2610B981),
                border: Border.all(color: const Color(0x4D10B981)),
                borderRadius: BorderRadius.circular(8)
              ),
              child: Center(child: Text(label, style: const TextStyle(color: Color(0xFF34D399), fontWeight: FontWeight.bold, fontSize: 13), overflow: TextOverflow.ellipsis)),
            ),
          ),
        ),
      )
    );
  }


  Widget _buildFastCashBtn(BuildContext context, String label) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          context.read<PosTerminalBloc>().add(ClearCartEvent());
          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Paiement rapide: $label")));
        },
        borderRadius: BorderRadius.circular(8),
        child: Ink(
          padding: const EdgeInsets.symmetric(vertical: 8),
          decoration: BoxDecoration(color: const Color(0xFF1E293B), borderRadius: BorderRadius.circular(8)),
          child: Center(child: Text(label, style: const TextStyle(color: Color(0xFFE2E8F0), fontWeight: FontWeight.bold, fontSize: 13))),
        ),
      ),
    );
  }

  Widget _buildCartAction(BuildContext context, String icon, String label, Color bgColor, Color textColor, {VoidCallback? onTapOverride}) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: () {
          if (onTapOverride != null) {
            onTapOverride();
          } else {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Action: $label"), duration: const Duration(seconds: 1)));
          }
        },
        borderRadius: BorderRadius.circular(8),
        child: Ink(
          decoration: BoxDecoration(color: bgColor, borderRadius: BorderRadius.circular(8)),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(icon, style: const TextStyle(fontSize: 13)),
              const SizedBox(width: 4),
              Flexible(child: Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 12), overflow: TextOverflow.ellipsis)),
            ],
          ),
        ),
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
                  _buildQuickKey(context, "⚡", "Café Express (1.50€)"),
                  _buildQuickKey(context, "⚡", "Croissant (1.80€)"),
                  _buildQuickKey(context, "⚡", "Menu Midi (14.50€)"),
                ],
              ),
            ),
          ),
          
          // Category Tabs
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                _buildCatTab(context, "Tous", true),
                _buildCatTab(context, "Plats", false),
                _buildCatTab(context, "Boissons", false),
                _buildCatTab(context, "Desserts", false),
              ],
            ),
          ),
          
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
                      physics: const NeverScrollableScrollPhysics(),
                      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
                        crossAxisCount: 5,
                        childAspectRatio: 0.9,
                        crossAxisSpacing: 8,
                        mainAxisSpacing: 8,
                      ),
                      itemCount: 15,
                      itemBuilder: (context, index) {
                        return InkWell(
                          onTap: () {
                            int actualIndex = index + (_currentPage - 1) * 15;
                            if (actualIndex % 3 == 0) {
                              _showModifiersDialog(context, "p$actualIndex", "Produit ${actualIndex+1}", 1050 + (actualIndex * 100));
                            } else {
                              context.read<PosTerminalBloc>().add(AddProductEvent(
                                productId: "p$actualIndex", productName: "Produit ${actualIndex+1}", unitPriceCents: 1050 + (actualIndex * 100)
                              ));
                            }
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
                                Text("Produit ${(index + (_currentPage - 1) * 15)+1}", 
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
                                  child: Text("${((1050 + ((index + (_currentPage - 1) * 15) * 100))/100).toStringAsFixed(2)} €", 
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
                        Material(
                          color: Colors.transparent,
                          child: InkWell(
                            onTap: _currentPage > 1 ? () {
                              setState(() {
                                _currentPage--;
                              });
                            } : null,
                            borderRadius: BorderRadius.circular(8),
                            child: Ink(
                              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                              decoration: BoxDecoration(color: _currentPage > 1 ? cardColor : const Color(0x330F172A), borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                              child: Row(children: [Icon(Icons.chevron_left, color: _currentPage > 1 ? textColor : textMuted, size: 18), Text(" Précédent", style: TextStyle(color: _currentPage > 1 ? textColor : textMuted, fontWeight: FontWeight.bold))]),
                            ),
                          ),
                        ),
                        Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Text("PAGE $_currentPage / $_totalPages", style: const TextStyle(color: textColor, fontSize: 12, fontWeight: FontWeight.bold, fontFamily: 'JetBrains Mono')),
                            const SizedBox(height: 4),
                            Row(
                              children: List.generate(_totalPages, (i) {
                                return Container(
                                  margin: EdgeInsets.only(right: i < _totalPages - 1 ? 6 : 0),
                                  width: 10, height: 10,
                                  decoration: BoxDecoration(
                                    color: (i + 1) == _currentPage ? primaryColor : const Color(0x33FFFFFF),
                                    shape: BoxShape.circle
                                  )
                                );
                              }),
                            )
                          ]
                        ),
                        Material(
                          color: Colors.transparent,
                          child: InkWell(
                            onTap: _currentPage < _totalPages ? () {
                              setState(() {
                                _currentPage++;
                              });
                            } : null,
                            borderRadius: BorderRadius.circular(8),
                            child: Ink(
                              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 10),
                              decoration: BoxDecoration(color: _currentPage < _totalPages ? cardColor : const Color(0x330F172A), borderRadius: BorderRadius.circular(8), border: Border.all(color: borderColor)),
                              child: Row(children: [Text("Suivant ", style: TextStyle(color: _currentPage < _totalPages ? textColor : textMuted, fontWeight: FontWeight.bold)), Icon(Icons.chevron_right, color: _currentPage < _totalPages ? textColor : textMuted, size: 18)]),
                            ),
                          ),
                        ),
                      ]
                    )
                  )
                ],
              ),
            ),
          )
        ],
      ),
    );
  }

  Widget _buildQuickKey(BuildContext context, String icon, String label) {
    return Container(
      margin: const EdgeInsets.only(right: 8),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Quick Key: $label"), duration: const Duration(seconds: 1)));
          },
          borderRadius: BorderRadius.circular(8),
          child: Ink(
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
          ),
        ),
      ),
    );
  }

  Widget _buildCatTab(BuildContext context, String label, bool active) {
    return Container(
      margin: const EdgeInsets.only(right: 10),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: () {
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text("Categorie: $label"), duration: const Duration(seconds: 1)));
          },
          borderRadius: BorderRadius.circular(12),
          child: Ink(
            padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
            decoration: BoxDecoration(
              color: panelColor,
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: active ? Colors.white : borderColor),
            ),
            child: Text(label, style: TextStyle(color: textColor, fontWeight: FontWeight.bold, fontSize: 14)),
          ),
        ),
      ),
    );
  }
}
