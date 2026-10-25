# iPad standalone 4d — suivi d'exécution (écran Imprimantes)

Plan : `docs/plans/` (lot 4d)

Base de référence : PosKit 689, PosPrinters 31

| Tâche | Statut | PosKit | PosPrinters | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Validation par transport (PosKit) | fait | 702 | 33 (`swift test`) ; `./scripts/test.sh unit` vert | aucun |

## Constats

- Tâche 1 : `PrinterStore.validate` / `isValidDeviceIdentifier` sont `nonisolated static` (appelés depuis l'acteur `LocalPosAPI`). Star/Epson sans identifiant restent valides si l'IPv4 est valide (repli réseau de `PrinterAddress.parse`) ; avec identifiant, l'IP est ignorée.
- Tâche 1 : message de `testPrinter` non LAN : l'identifiant du périphérique remplace `ip:port` (`Printer.testTarget`).
