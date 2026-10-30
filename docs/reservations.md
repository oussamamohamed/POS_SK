# Réservations

Entité `Reservation` (table `Reservations`, créée par `Program.cs` sur les bases existantes). Une entrée de la liste d'attente est une réservation avec `IsWaitlist = true` : pas de table, `StartsAtUtc` = heure d'arrivée.

## API (`/api/reservations`, opérateur connecté)

| Route | Rôle |
|---|---|
| `GET /?from=&to=` | Liste triée par heure (défaut : le jour UTC en cours). |
| `POST /` | Création. |
| `PUT /{id}` | Modification (seulement `Booked`). |
| `POST /{id}/seat` | Installe le client : ouvre la table (`OpenTableAsync`) et passe en `Seated`. |
| `POST /{id}/status` | `Done` (depuis `Seated`), `NoShow` ou `Cancelled` (depuis `Booked`). |

Statut en nombre : `Booked` 0, `Seated` 1, `Done` 2, `NoShow` 3, `Cancelled` 4. `isLate` est calculé par le serveur : `Booked`, hors liste d'attente, plus de 15 minutes après l'heure prévue.

Refus métier : 400 (`InvalidName`, `InvalidCovers` 1-30, `InvalidDuration` 30-240 min, `InvalidPhone`, `UnknownTable`, `TableRequired`, `TableCapacity`), 404 (`NotFound`), 409 (`TableConflict` : créneau déjà pris sur la table, `TableOccupied`, `InvalidState`). Message localisé : clé `errors.reservation_<code en minuscules>`.

Chaque mutation diffuse `OnReservationsChanged()` sur `/hubs/pos` (web et iPad rechargent la liste).

## Donnée personnelle : le téléphone

- Facultatif, format `^[+0-9 ().-]{6,20}$`.
- Renvoyé en clair aux responsables et administrateurs seulement ; les autres opérateurs reçoivent `hasPhone`.
- Une modification par un non-responsable conserve le numéro existant (il ne le voit pas, il ne doit pas l'effacer).
- Effacé 2 jours après l'heure de la réservation ; la ligne entière (nom, note) est supprimée après 90 jours. `ReservationPurgeService` s'exécute au lancement puis chaque heure ; constantes `ReservationService.PhoneRetention` et `RowRetention`.
- Jamais journalisé.

À faire valider (RGPD) : durées de conservation, et information du client sur l'enregistrement de son numéro.

## Compléments

- `QuoteMinutes` (1 à 240) : attente annoncée à un client en liste d'attente, ignorée hors liste d'attente ; une fois dépassée, l'entrée remonte dans « à traiter ».
- Une table soldée (`POST /api/checkout/pay` avec solde nul) ou libérée manuellement passe sa réservation `Seated` en `Done` ; l'événement `OnReservationsChanged` est diffusé après un paiement soldé.

