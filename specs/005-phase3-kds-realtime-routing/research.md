# Research & Architecture Decisions: Phase 3 - Kitchen Display System (KDS) & Real-Time Routing

**Feature**: `005-phase3-kds-realtime-routing`
**Date**: 2026-08-15
**Status**: Completed

## 1. SignalR Real-Time Transport & Station Grouping

### Decision
Implement real-time bidirectional communication using ASP.NET Core SignalR (`KitchenHub`). KDS display stations join topic groups based on their preparation role:
- `Group_Station_All`: Master kitchen overview
- `Group_Station_HotKitchen`: Grill, fryers, stoves
- `Group_Station_ColdKitchen`: Salads, starters, cold prep
- `Group_Station_Bar`: Drinks, cocktails, beers
- `Group_Station_Pastry`: Desserts, ice creams

### Rationale
- **Sub-200ms Latency**: Native WebSockets deliver new tickets in $< 10\text{ms}$ on local Wi-Fi.
- **Selective Broadcasting**: Tickets are streamed exclusively to the relevant preparation station screens, avoiding screen clutter.
- **Built-in Resilience**: SignalR provides automatic heartbeat monitoring and client-side reconnect handlers.

### Alternatives Considered
- **HTTP REST Polling**: Rejected because polling at 500ms intervals generates unnecessary network chatter and adds average 250ms latency.
- **Raw TCP Sockets**: Rejected because SignalR provides structured JSON RPC, connection multiplexing, and reconnection logic out-of-the-box.

---

## 2. In-Memory Ticket State Management & Bump Flow

### Decision
KDS client manages active tickets in a reactive `ObservableCollection<KitchenTicketDto>` within `KdsViewModel`. Tapping a ticket or item mutates the state locally for immediate UI feedback ($< 16\text{ms}$) and sends a `BumpItem` / `BumpTicket` message to `KitchenHub` asynchronously.

### Rationale
- Cooks experience instantaneous touch responsiveness without waiting for network acknowledgment.
- If a collision occurs (e.g. two cooks bumping at the exact same millisecond), the server broadcasts the reconciled state.

---

## 3. Preparation Elapsed Timers & Heatmap Color Coding

### Decision
Run a local UI timer updating active ticket badge colors:
- 🟢 **Green** ($0 - 9\text{ min}$): Normal preparation window.
- 🟠 **Amber** ($10 - 19\text{ min}$): Preparation taking longer than expected.
- 🔴 **Flashing Red** ($\ge 20\text{ min}$): Urgent / delayed ticket requiring expediter attention.

### Rationale
- Highly visible visual cue for chefs and expediter line managers during rush periods.
