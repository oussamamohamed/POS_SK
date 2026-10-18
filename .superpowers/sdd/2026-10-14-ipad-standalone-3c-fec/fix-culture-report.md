# Fix culture FEC

- FecExportService : les 2 libellés avec taux (`Vente ... (HT x%)`, `TVA Collectee x% ...`) passent par `FormatRateForLabel` (InvariantCulture `0.#`, puis '.' -> ','). Libellés de comptes (point) inchangés. Autres formats du fichier déjà en InvariantCulture.
- Test : tests/RestaurantPos.Infrastructure.Tests/FecCultureTests.cs (en-US vs de-DE / fr-FR, sorties identiques, contient "5,5%"). Échec constaté avant correction (2/2), vert après.
- Infrastructure.Tests : 439/439 verts, fec_reference.json inchangé (non régénéré).
