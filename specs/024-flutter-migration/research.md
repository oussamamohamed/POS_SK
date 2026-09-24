# Research & Decisions: Flutter Migration

## 1. WebSockets / SignalR in Dart
**Decision**: Use `signalr_netcore` package.
**Rationale**: The central .NET 9 server utilizes ASP.NET Core SignalR out of the box. While standard web sockets can work, the SignalR protocol negotiates hubs and serialization seamlessly. `signalr_netcore` provides a Dart-native implementation compatible with .NET Core.
**Alternatives considered**: Standard `web_socket_channel` (requires manual JSON parsing and keep-alive tracking, brittle for POS).

## 2. State Management Architecture
**Decision**: Use `flutter_bloc` with `equatable`.
**Rationale**: The POS UI is heavily reactive (swipes triggers prices triggering taxation updates). BLoC enforces unidirectional data flow exactly like Redux but scoped, replacing `CommunityToolkit.Mvvm` `ObservableProperty`.
**Alternatives considered**: `Riverpod` (too flexible, allows global state abuse), `Provider` (too simplistic for complex POS transactions).

## 3. Local Persistence
**Decision**: Use `sqflite`.
**Rationale**: Native wrapping around iOS SQLite. Porting the existing C# models over to Dart data classes with SQL generators matches the offline-first transactional standard dictated by the Constitution seamlessly without needing NoSQL migration.
**Alternatives considered**: `Hive` or `Isar` (fast, but requires rewriting the entire data relation structure to NoSQL, jeopardizing NF525 tracing portability).

## 4. Haptics and Tactics
**Decision**: Use native `HapticFeedback` class in Flutter `services` library.
**Rationale**: `HapticFeedback.lightImpact()` routes directly to the iOS UIImpactFeedbackGenerator ensuring sub-50ms latency natively matching HIG rules.
**Alternatives considered**: Custom MethodChannels (Over-engineered when standard iOS hooks are provided by the Flutter team).
