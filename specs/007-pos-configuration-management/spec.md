# Feature Specification: Configurable POS Management & Administration

**Feature Branch**: `007-pos-configuration-management`

**Created**: 2026-08-30

**Status**: Draft

**Input**: User description: "je veux que l'interface soit paramétrable pour l'ajout des article et les familles les serveur les imprimentes et les écrans de saisies"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Menu & Catalog Configuration (Articles, Categories, Pricing & Taxes) (Priority: P1) 🎯 MVP

As a restaurant manager or business owner,
I want to create, update, organize, and archive menu articles and product categories directly from a protected administration interface,
So that the POS terminals immediately reflect the active restaurant menu, seasonal specials, prices, and applicable tax rates.

**Why this priority**: Without the ability to define and adjust categories and articles, the cash register cannot adapt to daily restaurant operations, menu rotations, or price updates.

**Independent Test**:
1. Log in with a manager PIN and open the Catalog Configuration module.
2. Create a new category "Desserts Maison" with a display color and sort position.
3. Add a new article "Tiramisu Spéculos" priced at 7.50 € with 10% VAT assigned to "Desserts Maison".
4. Open the sales terminal: verify that the new category and article appear immediately, and can be added to an active order with exact price and tax calculations.

**Acceptance Scenarios**:

1. **Given** a manager logged into the administration screen, **When** they add a new product category with a name, display color, and sort order, **Then** the category is saved and made available for article assignment.
2. **Given** an existing category, **When** a manager creates a new article with title, description, selling price, VAT rate, and optional modifiers, **Then** the article is stored and instantly visible on sales screens.
3. **Given** an existing article, **When** a manager edits its price or disables its availability (out of stock/archive), **Then** the POS sales terminal updates immediately, preventing sales of archived items and applying the new price to new orders without altering past closed receipts.

---

### User Story 2 - Staff & Server Management with Secure Fast-Access PINs (Priority: P1)

As a restaurant director or floor manager,
I want to add, edit, and deactivate staff members (servers, bartenders, kitchen chefs, managers) and set their secret 4-6 digit access PIN codes,
So that employees can rapidly unlock terminals with role-appropriate permissions and all order actions are attributed to the correct operator.

**Why this priority**: Accurate staff management is critical for operational security, shift tracking, role-based access control, and sales attribution per server.

**Independent Test**:
1. Access the Staff Management panel with manager authorization.
2. Create a new server "Lucas" with role "Server" and set a 4-digit PIN "2468".
3. Lock the POS terminal. Enter "2468" on the keypad: verify instant login as Lucas with standard server permissions (sales allowed, back-office settings locked).

**Acceptance Scenarios**:

1. **Given** a manager in Staff Management, **When** they create a new employee profile with full name, role (Manager, Server, Bartender, Kitchen Cook), and a 4-6 digit PIN, **Then** the profile is saved and the PIN is validated for format and uniqueness.
2. **Given** an active staff member, **When** a manager updates their role or resets their PIN, **Then** the staff member can immediately authenticate with their updated credentials.
3. **Given** an operator with standard "Server" role, **When** they attempt to access configuration menus (catalog, staff, printers, layouts), **Then** access is denied and a manager PIN prompt is displayed.

---

### User Story 3 - Network Printers & Production Station Hardware Setup (Priority: P2)

As a restaurant manager or technical administrator,
I want to discover, declare, and configure thermal printers and assign them specific printing roles (Counter Receipt, Hot Kitchen, Cold Kitchen, Bar/Beverages),
So that kitchen preparation tickets and customer receipts are automatically routed to the proper physical printers across the restaurant network.

**Why this priority**: Reliable, flexible hardware setup enables seamless order dispatch to kitchen stations and checkout receipt delivery without hardcoded network settings.

**Independent Test**:
1. Open the Printer Configuration screen.
2. Scan the local network or manually add a printer at a designated IP address with name "Imprimante Cuisine Chaude".
3. Assign the printer the role "Hot Kitchen" and trigger a test print: verify the printer outputs a diagnostic alignment and status ticket.
4. Place an order containing a hot dish: verify the preparation ticket is routed specifically to that configured printer.

**Acceptance Scenarios**:

1. **Given** a manager on the Hardware Configuration page, **When** they initiate an automatic printer scan or enter an IP address manually, **Then** the printer is identified and added to the list of available devices.
2. **Given** a configured printer, **When** a manager assigns it one or more preparation station roles (e.g., Bar, Hot Kitchen, Receipt & Cash Drawer), **Then** future orders containing items matching those stations route to that physical device.
3. **Given** any configured printer, **When** the manager presses "Test Print", **Then** the system sends a diagnostic sample slip to verify connectivity, paper cut, and drawer kick functionality.

---

### User Story 4 - Touch Terminal Screen & Layout Customization (Priority: P2)

As a restaurant supervisor,
I want to customize the sales screen layout (category tab arrangement, item grid density, rush-hour quick-keys, default landing screen),
So that the ordering interface is tailored to our specific service workflow and optimizes speed during high-volume rush hours.

**Why this priority**: Different terminals have distinct workflows (e.g., Bar counter terminal vs. Table dining terminal vs. Mobile handheld order pad); flexible screen configuration maximizes tactile productivity.

**Independent Test**:
1. Open the Terminal Layout Configuration screen.
2. Reorder category tabs to place "Boissons & Cocktails" first, and assign 4 top-selling items to the "Quick Keys" rush bar.
3. Save layout profile and return to sales screen: verify the category tabs and quick keys reflect the new order and provide one-tap ordering.

**Acceptance Scenarios**:

1. **Given** a manager in the Layout Configuration view, **When** they drag-and-drop or reorder category tabs, **Then** the sales screen updates its tab sequence accordingly.
2. **Given** a layout configuration, **When** a manager assigns high-frequency products to the Quick Keys section, **Then** those items remain pinned for single-tap addition regardless of the selected category.
3. **Given** multiple device profiles (e.g., 10.9" Counter Tablet vs. 8.3" Handheld), **When** a layout profile is selected, **Then** the interface dynamically adapts its tile sizes, column counts, and button targets to the configured preference.

---

### Edge Cases

- **Duplicate PIN Collision**: If a manager attempts to assign a PIN code that is already in use by another active employee, the system must reject the entry with a clear validation message.
- **Archiving Categories with Active Articles**: When a category containing active products is archived or hidden, the system must prompt the manager to reassign products or confirm archiving child articles to prevent orphaned menu items.
- **Printer Offline or Unreachable**: If a configured kitchen printer becomes offline or disconnected, the system must log an alert and allow rerouting production tickets to a backup printer or displaying them on the KDS without crashing or blocking sales.
- **Offline Configuration Synchronization**: When catalog or staff changes are made in offline/standalone mode, the changes must be applied locally immediately and queued for synchronization when connectivity to the central server is restored.
- **Simultaneous Price Update During Open Orders**: If an article's price is modified while open dining tables have unfinalized orders containing that article, existing open order lines retain their recorded order price, while new lines added subsequently receive the updated price.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a PIN-protected Administration & Configuration portal accessible only to users with Manager or Admin privileges.
- **FR-002**: System MUST allow managers to create, view, edit, reorder, and archive product categories with customizable display names, color codes, and visual icons.
- **FR-003**: System MUST allow managers to create, edit, duplicate, and archive articles with title, short description, selling price (exact cent decimal), tax rate bracket, category link, and stock availability status.
- **FR-004**: System MUST allow managers to attach modifier groups (e.g., cooking temperatures, sauces, side dishes) and individual modifier options with optional price surcharges to articles.
- **FR-005**: System MUST allow managers to create, update, and deactivate staff member profiles, including full name, designated role (Admin, Manager, Server, Bartender, Kitchen Cook), and encrypted 4-6 digit fast-login PIN.
- **FR-006**: System MUST enforce PIN uniqueness and validate that PIN codes contain between 4 and 6 numeric digits.
- **FR-007**: System MUST allow discovery and manual declaration of thermal network printers with IP address, port, custom name, and character width (80mm / 58mm).
- **FR-008**: System MUST support assigning distinct operational roles to printers (Customer Receipt & Cash Drawer, Hot Kitchen, Cold Kitchen, Bar/Beverages).
- **FR-009**: System MUST provide an interactive "Test Print" function for each configured printer to verify network connectivity, formatting, paper cutting, and cash drawer kick.
- **FR-010**: System MUST allow managers to configure the sales terminal screen layout, including category tab order, grid tile dimensions, and quick-key shortcut buttons.
- **FR-011**: System MUST support creating and switching terminal configuration profiles tailored to specific station types (e.g., Counter Bar, Main Dining Room, Mobile Order Taker, KDS Screen).
- **FR-012**: System MUST persist all configuration changes to the local sandboxed database for 100% offline autonomy and dispatch updates to connected terminals in real time.
- **FR-013**: System MUST preserve historical transactional integrity: updating or archiving an article, category, or staff member MUST NEVER alter past closed fiscal receipts or audit records.
- **FR-014**: System MUST provide search and filter capabilities across catalog items and staff lists for quick administrative management.

### Key Entities

- **ProductCategory**: Represents a logical grouping of menu items (e.g., Entrées, Plats, Desserts, Boissons) with attributes for name, color, icon, display order, and active status.
- **ProductItem**: Represents a sellable menu article with attributes for name, category reference, price, tax rate, modifier groups, and availability flag.
- **StaffMember**: Represents an operator with name, employee ID, role enum, salted PIN hash, active status, and permission level.
- **PrinterDevice**: Represents a physical receipt or ticket printer with IP address, port, protocol, station role assignment, and cash drawer trigger capability.
- **TerminalLayoutProfile**: Represents UI presentation settings for a terminal or station, including active category sequence, quick-key bindings, grid column density, and station role.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A manager can create a new product category and add a new article with pricing and tax in under 60 seconds.
- **SC-002**: Changes to categories, articles, or staff credentials are synchronized and effective on all active sales terminals within 2 seconds.
- **SC-003**: Adding a new staff member and testing their fast PIN login takes under 30 seconds.
- **SC-004**: Adding a new network printer and executing a successful test print takes under 45 seconds.
- **SC-005**: 100% of catalog and layout configurations remain fully accessible and editable in offline mode without requiring active cloud connectivity.
- **SC-006**: Zero regression or corruption of existing fiscal audit receipts when articles or tax rates are modified or archived.

## Assumptions

- **Manager Authentication**: Administration and configuration screens are locked behind a manager PIN verification modal or elevated role check.
- **Network Printers**: Thermal printers support standard raw TCP socket connectivity (port 9100) or ESC/POS protocols over the local restaurant Wi-Fi/Ethernet network.
- **Single-Location & Multi-Station Scope**: Configuration covers a physical restaurant outlet with multiple POS terminals, KDS screens, and network printers.
- **Offline Autonomy**: All administrative changes can be performed locally on the primary POS terminal and propagate seamlessly to client devices upon network availability.
