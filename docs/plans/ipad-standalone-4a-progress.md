# iPad standalone 4a — suivi d'exécution (document et rendu des tickets)

Plan : `docs/superpowers/plans/2026-10-22-ipad-standalone-4a-rendu-tickets.md`

| Tâche | Statut | .NET | PosKit | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Fixture .NET `ticket_document_reference.json` | fait | 768 (Domain 69 + Infrastructure 442 + Api 257) | 557 | aucun |
| 2 Modèle `TicketDocument` et JSON | fait | 768 (inchangé) | 562 | aucun |
| 3 Rendu texte ESC/POS et commandes | fait | 768 (inchangé, aucun changement .NET) | 569 | aucun |
| 4 Entrées, formats et textes localisés | fait | 768 (inchangé, aucun changement .NET) | 578 | aucun |
| 5 `TicketBuilder` | fait | 768 (inchangé, aucun changement .NET) | 587 | aucun |
| 6 Rendu raster CoreText | fait | 768 (inchangé, aucun changement .NET) | 598 | aucun |
| 7 Documentation et vérification finale | à faire | - | - | - |

## Contrôle visuel du raster (tâche 6)

Rendu à 576 points des scénarios `receipt_fr_full` et `receipt_ar` (PNG jetables, non commités) : texte lisible, à l'endroit et non inversé ; séparateurs pointillés ; colonnes alignées (étiquette à gauche, valeur à droite en français ; inversé en arabe) ; arabe lié et écrit de droite à gauche, latin et arabe mélangés correctement. Blocs alignés en haut comme le .NET : dans les colonnes arabes la ligne de base de l'étiquette arabe est plus basse que celle de la valeur latine (métriques plus hautes de Noto Sans Arabic ; rendu .NET non comparé visuellement). Les paragraphes RTL réordonnent le texte neutre selon l'algorithme bidi (`5 Rue des Lilas` affiché `Rue des Lilas 5`, JSON de TVA `{132:"10"}`), comportement attendu d'une direction de base RTL.
