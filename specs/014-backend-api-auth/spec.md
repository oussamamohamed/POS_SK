# Feature Specification: Backend API Authentication & Multi-Role Authorization

**Feature Branch**: `014-backend-api-auth`

**Created**: 2026-09-03

**Updated**: 2026-09-03 (Added Development & Testing Environment Support)

**Status**: Draft

**Input**: User description: "il faut rajouter l'authentification API pour le backend" + "014 il faut gerer aussi l'environement de dev pour ne pas bloquer les testes"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fast Operator Authentication & Session Protection (Priority: P1)

Restaurant staff (servers, bartenders, managers) must authenticate with their individual operator PIN or credential on any connected terminal to obtain an authorized session. Once authenticated, the session must securely identify the operator for every action (order taking, table modification, receipt emission) and enforce appropriate permissions according to their assigned role.

**Why this priority**: Restaurant operations require strict accountability and security. Every order and financial transaction must be bound to an authenticated operator without slowing down rapid service.

**Independent Test**: An operator enters their PIN on the login interface. Verify that a valid session is created within 50ms, containing the operator's identity and role. Attempting to execute authorized operations succeeds, while unauthenticated requests to protected endpoints are rejected with a clear unauthorized status.

**Acceptance Scenarios**:

1. **Given** a registered operator with an active profile and PIN code, **When** submitting valid credentials to the authentication service, **Then** the system issues a secure session token containing operator identity and role claims, valid for the shift duration.
2. **Given** an invalid or non-existent PIN code, **When** authentication is attempted, **Then** the request is rejected with an invalid credentials error, without revealing whether the user exists.
3. **Given** an unauthenticated request to an order modification or payment endpoint, **When** processed by the backend, **Then** the request is denied with an authentication challenge before any business logic executes.

---

### User Story 2 - Role-Based Access Control (RBAC) on Sensitive Operations (Priority: P1)

The backend must enforce strict role-based access rules where regular servers can manage orders and table covers, kitchen staff can update ticket preparation states, while administrative and compliance-sensitive actions (voiding receipts, applying custom discounts above threshold, editing staff PINs, modifying tax rates, closing fiscal Z-reports) require `FloorManager` or `Admin` privileges.

**Why this priority**: Protects restaurant revenue and enforces NF525 fiscal auditability by ensuring high-risk actions cannot be executed by unauthorized staff members.

**Independent Test**: Authenticate as a server (`Waiter`) and attempt to void a finalized receipt or reset staff PINs. Verify the system denies the request with a forbidden status. Repeat the same request with a `FloorManager` or `Admin` session and verify it succeeds and records the authorizing operator ID.

**Acceptance Scenarios**:

1. **Given** an authenticated `Waiter` session, **When** attempting to access administrative configuration or trigger receipt cancellations, **Then** the backend rejects the request with a forbidden permission error.
2. **Given** an authenticated `FloorManager` or `Admin` session, **When** executing privileged operations (receipt voids, staff updates, daily fiscal closures), **Then** the backend permits the operation and records the operator's ID in the audit trail.
3. **Given** an action requiring manager override on a server's terminal, **When** a manager enters their authorization credential, **Then** the backend validates the supervisor's role and executes the privileged command under dual-operator traceability.

---

### User Story 3 - Development & Automated Testing Environment Support (Priority: P1)

Developers and automated CI/CD test suites must be able to run unit, integration, and E2E tests without being blocked or disrupted by interactive PIN authentication. In testing and development environments, the system must provide configurable test identities and predictable credentials, allowing tests to simulate any user role without brittle login dependencies.

**Why this priority**: Enforcing strict authentication without a dedicated testing strategy breaks automated test suites (`WebApplicationFactory`), delays development velocity, and introduces flaky tests.

**Independent Test**: Execute the automated test suite (`dotnet test`). Verify that integration tests can execute against protected endpoints by asserting specific roles (e.g. `Admin`, `FloorManager`, `Waiter`) seamlessly, achieving 100% test success without breaking existing tests.

**Acceptance Scenarios**:

1. **Given** an automated test running in the `Testing` environment, **When** dispatching HTTP requests to authenticated endpoints, **Then** the test harness can supply a test authorization token or mock claim that is recognized immediately by the backend without requiring an active database login flow.
2. **Given** a local development environment (`Development` mode), **When** a developer starts the system, **Then** standard demo accounts with known PINs (`1234` for Manager, `2468` for Waiter, `5678` for Chef, `9999` for Admin) are available out-of-the-box.
3. **Given** a production deployment, **When** the application starts, **Then** all test bypasses, mock claim handlers, and default test PIN configurations are strictly disabled and ignored.

---

### User Story 4 - Terminal Device Registration & Real-Time Channel Security (Priority: P2)

Connected POS terminals, mobile tablets, and kitchen display units must establish an authenticated device-level trust with the backend server. The real-time communication channel (SignalR / WebSockets) must verify device or session authorization upon connection, ensuring that only certified terminals can broadcast table status changes or receive kitchen dispatch events.

**Why this priority**: Prevents rogue devices or unauthorized network actors on the local Wi-Fi from tampering with live orders, snooping on customer tickets, or spoofing table statuses.

**Independent Test**: Connect an authorized terminal client to the backend and its real-time hub. Verify that communication succeeds. Attempt to connect an unauthenticated or rogue client to the real-time hub; verify that the connection is rejected at handshake time.

**Acceptance Scenarios**:

1. **Given** an authorized POS client device, **When** connecting to the real-time communication hub, **Then** the backend accepts the connection and allows subscription to station-specific ticket feeds and table status broadcasts.
2. **Given** a client connecting without valid authentication or credentials, **When** negotiating the real-time connection, **Then** the server aborts the connection handshake with an authorization failure.
3. **Given** non-sensitive public display queries (reading catalog products or viewing floor plan layout), **When** queried by recognized terminals, **Then** the system returns the data cleanly without requiring unnecessary interactive operator login popups.

---

### User Story 5 - Brute-Force Throttling & Security Audit Logging (Priority: P3)

The backend must detect repeated failed authentication attempts, apply temporary rate-limiting or progressive backoff delays to prevent PIN brute-force attacks, and record all authentication events (logins, failures, logouts, supervisor overrides) in the audit journal.

**Why this priority**: 4-digit PINs are convenient for tactile speed but vulnerable to rapid automated guessing without progressive delay and rate limiting.

**Independent Test**: Send 5 consecutive incorrect PIN authentication attempts within 10 seconds. Verify that subsequent attempts trigger an intentional rate-limit delay or temporary lockout, and verify that audit entries document the failed attempts.

**Acceptance Scenarios**:

1. **Given** multiple consecutive failed PIN entries on a terminal, **When** the threshold is reached, **Then** the backend imposes a progressive delay and temporary lock before permitting further attempts.
2. **Given** any successful or failed authentication event, **When** handled by the backend, **Then** a structured security audit log entry is written with timestamp, terminal identifier, operator ID (if identified), and outcome.

---

### Edge Cases

- **Accidental Test Mode in Production**: The system must fail fast on startup if a test authentication bypass flag is detected in a `Production` environment configuration.
- **Expired Session during Rush Hour**: When an operator's token expires during an active order, the client must prompt for an instant PIN re-entry without clearing the active shopping cart or table context.
- **Clock Drift**: The backend must allow a reasonable clock skew tolerance (e.g., 60 seconds) when validating time-based session tokens across local devices.
- **Deactivated Operator Account**: If an operator's account is marked inactive by a manager, any active sessions for that operator must immediately be rejected by the backend.
- **Network Interruption / Offline Reconnection**: Offline terminals buffering sales locally must authenticate transactions with their local operator cache, and submit valid terminal authentication headers upon syncing with the central backend.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST authenticate operators using a secure credential validation service that verifies salted hashes with zero timing leakage.
- **FR-002**: System MUST issue cryptographically signed session tokens upon successful authentication containing operator identifier, display name, and assigned role claims.
- **FR-003**: System MUST protect all mutating endpoints (order submission, bill checkout, payment processing, table opening, ticket bumping) against unauthenticated access.
- **FR-004**: System MUST enforce Role-Based Access Control (RBAC) separating permissions across `Waiter`, `KitchenStaff`, `FloorManager`, and `Admin` roles.
- **FR-005**: System MUST require `FloorManager` or `Admin` privileges for high-risk operations including receipt cancellation/void, daily Z-closure generation, staff creation/modification, and discount overrides.
- **FR-006**: System MUST authenticate real-time communication channels (SignalR / WebSockets) to restrict live ticket dispatch and table broadcasts to authorized devices.
- **FR-007**: System MUST allow read-only terminal queries (catalog menus, active floor plan layout, network discovery health) to be accessed without requiring active operator session login.
- **FR-008**: System MUST apply progressive backoff throttling on repeated failed authentication attempts from the same source to mitigate PIN guessing attacks.
- **FR-009**: System MUST record all security-critical events (login success, login failure, session invalidation, supervisor override) in the system audit log.
- **FR-010**: System MUST immediately reject session tokens associated with deactivated or deleted operator accounts.
- **FR-011**: System MUST provide a test authentication mechanism in `Testing` environment allowing automated test suites (`WebApplicationFactory`) to execute endpoints with specified test roles (`Admin`, `FloorManager`, `Waiter`) without requiring live interactive PIN hashing.
- **FR-012**: System MUST guarantee that 100% of existing automated domain, application, client, and integration tests pass without regression when authentication is enabled.
- **FR-013**: System MUST provide pre-seeded development operator credentials (`1234` for Manager, `2468` for Waiter, `5678` for Chef, `9999` for Admin) when running in local `Development` mode.
- **FR-014**: System MUST strictly isolate and disable any test authentication handlers, mock claims, or development bypasses when running in `Production` environment.

### Key Entities *(include if feature involves data)*

- **OperatorSession**: Represents an active authenticated session for a staff member, including Session ID, Operator ID, Operator Name, Role, IssuedAtUtc, ExpiresAtUtc, and Client Terminal ID.
- **AuthenticationAuditRecord**: Immutable event log tracking authentication attempts, recording TimestampUtc, TerminalId, OperatorId (if resolved), AttemptResult (Success, InvalidPin, AccountDisabled, RateLimited), and SourceIp.
- **RolePermissionSet**: Configuration defining allowed operational boundaries and capability flags for each standard user role (`Waiter`, `KitchenStaff`, `FloorManager`, `Admin`).
- **TestAuthenticationProfile**: Configuration object used exclusively during automated tests to supply synthetic claims for specific roles (`Admin`, `FloorManager`, `Waiter`) without touching production authentication pipelines.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operator authentication validation and token issuance completes in under 50 milliseconds under standard load.
- **SC-002**: 100% of mutating API endpoints and administrative functions reject unauthenticated requests with appropriate standard error codes.
- **SC-003**: Zero unauthorized operations permitted when role privileges are lower than required (100% pass rate in RBAC security verification tests).
- **SC-004**: Real-time communication hub rejects 100% of unauthorized connection attempts while allowing seamless automatic reconnection for authorized terminals within 1 second of network restoration.
- **SC-005**: Brute-force guessing attacks on 4-digit PINs are prevented by rate-limiting within 5 failed attempts.
- **SC-006**: 100% of automated test suites (unit, integration, and E2E tests) run and pass without manual intervention or test blockage.
- **SC-007**: Development mode initialization allows engineers to authenticate and test any endpoint immediately with pre-configured roles in under 1 second.

## Assumptions

- Operating environment uses a centralized ASP.NET Core backend in a local restaurant network (LAN/Wi-Fi) connected to iPad and touchscreen terminals.
- Terminals maintain synchronized time via standard NTP or local host time with less than 60 seconds deviation.
- Existing NF525 fiscal signing rules remain intact, with the authenticated operator ID automatically bound to every signed receipt and daily closure.
- Terminal devices can store the acquired session token in memory/secure storage for the duration of the staff member's active shift.
- The `Testing` environment is clearly distinguished from `Production` via standard environment variables (`ASPNETCORE_ENVIRONMENT=Testing`).
