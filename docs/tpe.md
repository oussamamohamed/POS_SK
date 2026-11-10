# Paiement par TPE

Le serveur envoie le montant au terminal de paiement, attend la décision du client, et ne solde le ticket qu'après l'accord.

## Flux

1. L'opérateur choisit « Carte » : le client (web ou iPad) appelle `POST /api/checkout/pay` ou `POST /api/orders/counter/checkout`, comme avant.
2. Pour chaque règlement `CreditCard`, le serveur envoie le montant (pourboire inclus) au TPE **avant toute écriture** (`CardTerminalCharger`).
3. Refus, délai dépassé ou terminal injoignable : réponse `402 {code, message}`, aucun reçu écrit, commande et pourboire intacts. L'opérateur relance en appuyant à nouveau sur « Payer ».
4. Accord : le reçu fiscal NF525 est écrit et le ticket est soldé, comme pour un paiement classique.

Codes de refus : `card_declined`, `card_timeout` (rien n'est débité, le terminal est annulé), `card_terminal_unavailable`, `card_terminal_busy` (un autre paiement est en cours : jamais de double débit).

Sans fournisseur configuré, rien ne change : la carte reste saisie à la main (TPE autonome).

## Configuration

| Clé (variable d'environnement) | Rôle |
|---|---|
| `CardTerminal:Provider` (`CardTerminal__Provider`) | `Simulator` ou `Stripe`. Absent = pas de TPE piloté. |
| `CardTerminal:TimeoutSeconds` | Attente maximale de la décision, 60 par défaut. L'iPad coupe à 90 s. |
| `CardTerminal:Stripe:SecretKey` | Clé secrète Stripe (`sk_…`). |
| `CardTerminal:Stripe:ReaderId` | Identifiant du lecteur (`tmr_…`). |

`Simulator` : accepte tout montant, refuse ceux dont les centimes valent 13 (ex. 10,13 €).

## Marques

| Marque | Statut | Voie d'intégration |
|---|---|---|
| Simulateur | fait | essais, démonstration |
| Stripe Terminal (BBPOS WisePOS E, Stripe S700) | fait, testé contre un faux serveur HTTP, **pas contre un vrai lecteur** | API REST « server-driven » |
| SumUp (Solo) | à faire | API cloud REST, un adaptateur de la même taille |
| Adyen, Mollie | à faire | API cloud REST |
| Ingenico, Verifone, PAX (Worldline, Crédit Agricole…) | à faire | protocole Concert/Nexo sur le réseau local : adaptateur plus long, certification de la banque |

Ajouter une marque = une classe `ICardTerminal` dans `Infrastructure/Payments/` et un `case` dans `Program.cs`.

## Limites connues

- Un seul lecteur pour tout le restaurant, un encaissement à la fois.
- Mode autonome de l'iPad (sans serveur) : non pris en charge, le TPE est piloté par le serveur.
- Si la carte est débitée mais que le reçu ne peut pas être écrit (conflit rare), une erreur est journalisée avec la référence du TPE : le remboursement se fait à la main depuis le tableau de bord du fournisseur.
- Remboursement automatique lors de l'annulation d'un reçu (`/void`) : non fait.
