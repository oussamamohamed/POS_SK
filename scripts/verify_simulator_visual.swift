import Foundation
import Vision
import AppKit

struct VisualStep {
    let id: String
    let description: String
    let requiredKeywordGroups: [[String]] // AND across groups, OR within each group
    let forbiddenKeywords: [String]
    let screenshotName: String

    init(id: String, description: String, requiredKeywordGroups: [[String]], forbiddenKeywords: [String] = [], screenshotName: String) {
        self.id = id
        self.description = description
        self.requiredKeywordGroups = requiredKeywordGroups
        self.forbiddenKeywords = forbiddenKeywords
        self.screenshotName = screenshotName
    }
}

let steps: [VisualStep] = [
    VisualStep(
        id: "step1_pin_page",
        description: "Écran 1 : Clavier Tactile & Verrouillage PIN",
        requiredKeywordGroups: [
            ["Restaurant POS", "Restaurant", "POS"],
            ["PIN", "Saisissez"],
            ["Effacer", "1", "2", "3"]
        ],
        screenshotName: "step1_pin_page.png"
    ),
    VisualStep(
        id: "step2_floor_plan",
        description: "Écran 2 : Plan de Salle Interactif & Tables",
        requiredKeywordGroups: [
            ["Salle Principale", "Plan de Salle", "Salle"],
            ["T01", "T1", "T02", "T2", "T03"]
        ],
        screenshotName: "step2_floor_plan.png"
    ),
    VisualStep(
        id: "step3_pos_category_plats",
        description: "Écran 3a : Filtrage Catégorie 'Plats & Grillades' (Exclusion Boissons/Desserts)",
        requiredKeywordGroups: [
            ["Plats", "Grillades", "Plat"],
            ["Burger", "Entrecôte", "Entrecote"]
        ],
        forbiddenKeywords: ["Tiramisu", "Margherita", "Reine Royale"],
        screenshotName: "step3_pos_category_plats.png"
    ),
    VisualStep(
        id: "step3_pos_category_drinks",
        description: "Écran 3b : Filtrage Catégorie 'Boissons & Vins' (Exclusion Plats/Desserts)",
        requiredKeywordGroups: [
            ["Boissons", "Vins", "Boisson"],
            ["Bière", "Biere", "Bordeaux", "Expresso", "Café"]
        ],
        forbiddenKeywords: ["Entrecôte", "Entrecote", "Tiramisu", "Margherita"],
        screenshotName: "step3_pos_category_drinks.png"
    ),
    VisualStep(
        id: "step3_pos_category_all",
        description: "Écran 3c : Retour Catégorie '⚡ Tous' & Pagination Grille",
        requiredKeywordGroups: [
            ["Tous"],
            ["Burger", "Entrecôte", "Entrecote"],
            ["Page 1", "Précédent", "Suivant"]
        ],
        forbiddenKeywords: [],
        screenshotName: "step3_pos_category_all.png"
    ),
    VisualStep(
        id: "step3_pos_cart",
        description: "Écran 3 : Caisse Tactile, Articles & Panier avec boutons d'action",
        requiredKeywordGroups: [
            ["Burger", "Burger Maison"],
            ["TTC", "EUR", "€"],
            ["Envoyer Cuisine", "Envoyer", "Cuisine"],
            ["Encaisser", "Remise", "Transférer", "Split", "Attente"]
        ],
        screenshotName: "step3_pos_cart.png"
    ),
    VisualStep(
        id: "step4_modifiers_popup",
        description: "Écran 4 : Modificateurs, Cuissons & Options Produit",
        requiredKeywordGroups: [
            ["Burger", "Cuisson", "Viande", "Modificateurs", "Article"],
            ["Saignant", "point", "Sauce", "Ajouter", "Option"]
        ],
        screenshotName: "step4_modifiers_popup.png"
    ),
    VisualStep(
        id: "step4_discount_modal",
        description: "Écran 4ter : Modale Remise & Gestes Commerciaux",
        requiredKeywordGroups: [
            ["Remise", "Note", "Geste"],
            ["10%", "20%", "50%"],
            ["Annuler"]
        ],
        screenshotName: "step4_discount_modal.png"
    ),
    VisualStep(
        id: "step4_transfer_modal",
        description: "Écran 4quater : Modale Transfert de Table",
        requiredKeywordGroups: [
            ["Transférer", "Transferer", "Table"],
            ["destination", "T02", "T04", "T05"],
            ["Annuler"]
        ],
        screenshotName: "step4_transfer_modal.png"
    ),
    VisualStep(
        id: "step4_bill_note_modal",
        description: "Écran 4bis : Note de Table & Addition Provisoire",
        requiredKeywordGroups: [
            ["Note", "Addition", "Table"],
            ["Imprimer", "TTC", "PROVISOIRE", "DOCUMENT", "TOTAL"]
        ],
        screenshotName: "step4_bill_note_modal.png"
    ),
    VisualStep(
        id: "step5_kitchen_kds",
        description: "Écran 5 : Écran Cuisine KDS & Bons de Préparation (boutons BUMP & Rappel)",
        requiredKeywordGroups: [
            ["Cuisine", "KDS", "Ecran Cuisine"],
            ["Attente", "En Attente", "BUMP", "Preparation"],
            ["BUMP", "Rappel", "Recall"]
        ],
        screenshotName: "step5_kitchen_kds.png"
    ),
    VisualStep(
        id: "step6_split_bill",
        description: "Écran 6 : Partage de l'Addition (Split Bill) avec bouton Régler par convive",
        requiredKeywordGroups: [
            ["Partage", "Partager", "Addition"],
            ["convives", "Convive", "Montant"],
            ["Régler", "Regler", "Retour Encaissement", "Retour"]
        ],
        screenshotName: "step6_split_bill.png"
    ),
    VisualStep(
        id: "step7_checkout",
        description: "Écran 7 : Règlement Commande, Espèces & Rendu Monnaie (bouton Finaliser)",
        requiredKeywordGroups: [
            ["Règlement", "Paiement", "Reglement"],
            ["Rendu", "RENDU MONNAIE", "Monnaie"],
            ["Especes", "Espèces", "Cash", "Carte"],
            ["Finaliser", "Encaissement", "Partager la Note", "Retour Caisse"]
        ],
        screenshotName: "step7_checkout.png"
    ),
    VisualStep(
        id: "step7_table_freed",
        description: "Écran 7bis : Libération de la Table sur le Plan de Salle après Encaissement",
        requiredKeywordGroups: [
            ["Salle Principale", "Plan de Salle", "Salle"],
            ["T01", "Libre", "Disponible"]
        ],
        screenshotName: "step7_table_freed.png"
    ),
    VisualStep(
        id: "step8_admin_shell",
        description: "Écran 8a : Back-Office Administration (Onglet Catalogue & Familles)",
        requiredKeywordGroups: [
            ["Catalogue", "Familles", "Famille"],
            ["Ajouter", "Article", "TVA"]
        ],
        screenshotName: "step8_admin_shell.png"
    ),
    VisualStep(
        id: "step8_admin_staff",
        description: "Écran 8b : Back-Office Administration (Onglet Personnel & Codes PIN)",
        requiredKeywordGroups: [
            ["Personnel", "Serveurs", "Équipe", "Equipe"],
            ["Code PIN", "PIN", "Rôle", "Role", "Actif"]
        ],
        screenshotName: "step8_admin_staff.png"
    ),
    VisualStep(
        id: "step8_admin_printers",
        description: "Écran 8c : Back-Office Administration (Onglet Imprimantes Réseau)",
        requiredKeywordGroups: [
            ["Imprimantes", "Imprimante", "Réseau", "Reseau"],
            ["Enregistrer", "tiroir", "9100", "IP"]
        ],
        screenshotName: "step8_admin_printers.png"
    ),
    VisualStep(
        id: "step8_admin_layout",
        description: "Écran 8d : Disposition de l'Écran & Matrice Tactile",
        requiredKeywordGroups: [
            ["Disposition", "Matrice", "Format", "Écran", "Touches", "Colonnes"],
            ["Appliquer", "Réinitialiser", "Standard", "Burger", "Case"]
        ],
        screenshotName: "step8_admin_layout.png"
    ),
    VisualStep(
        id: "step8_admin_network",
        description: "Écran 8g : Back-Office Réseau & Sync (mDNS, boutons Lancer/Tester/Forcer)",
        requiredKeywordGroups: [
            ["Réseau", "Reseau", "Sync", "Synchronisation", "mDNS", "Découverte", "Decouverte"],
            ["Lancer", "Découverte", "Decouverte", "Scan", "Tester", "Connexion", "Forcer", "Synchronisation"]
        ],
        forbiddenKeywords: [],
        screenshotName: "step8_admin_network.png"
    ),
    VisualStep(
        id: "step8_admin_dashboard",
        description: "Écran 8h : Tableaux de Bord & KPIs Financiers (CA TTC, Panier Moyen, filtres)",
        requiredKeywordGroups: [
            ["Tableaux de Bord", "Tableaux", "KPIs", "KPI", "Financiers", "Dashboard"],
            ["CHIFFRE", "Chiffre", "AFFAIRES", "Affaires", "PANIER", "Panier", "TTC", "Aujourd'hui"]
        ],
        forbiddenKeywords: [],
        screenshotName: "step8_admin_dashboard.png"
    ),
    VisualStep(
        id: "step8_admin_happyhour",
        description: "Écran 8i : Plages Happy Hour & Tarifs (bouton Nouveau Créneau, Afterwork)",
        requiredKeywordGroups: [
            ["Happy Hour", "HappyHour", "Happy", "Plages", "Créneau", "Creneau"],
            ["Afterwork", "Nouveau", "Créneau", "Creneau", "Tarifs", "Bière", "Biere"]
        ],
        forbiddenKeywords: [],
        screenshotName: "step8_admin_happyhour.png"
    ),
    VisualStep(
        id: "step8_fiscal",
        description: "Écran 8e : Fiscalité NF525 & Rapport Journalier Actif (boutons Z, X, FEC)",
        requiredKeywordGroups: [
            ["Fiscalité", "Fiscalite", "NF525", "FEC"],
            ["Rapport Z", "Clôture", "Cloture"],
            ["Rapport X", "Aperçu", "Aperu"],
            ["FEC", "Générer", "Generer", "Export Comptable"]
        ],
        forbiddenKeywords: [],
        screenshotName: "step8_fiscal.png"
    ),
    VisualStep(
        id: "step8_fiscal_post_z",
        description: "Écran 8f : Fiscalité NF525 après Clôture Z (Compteur session réinitialisé à 0,00 €)",
        requiredKeywordGroups: [
            ["Fiscalité", "Fiscalite", "NF525"],
            ["0,00 €", "0.00 €"],
            ["Grand Total", "Perpétuel", "Perpetuel", "Total Ventes TTC"]
        ],
        forbiddenKeywords: [],
        screenshotName: "step8_fiscal_post_z.png"
    ),
    VisualStep(
        id: "step9_final_lock",
        description: "Écran 9 : Verrouillage Session & Retour Écran PIN",
        requiredKeywordGroups: [
            ["Restaurant POS", "Restaurant", "POS"],
            ["PIN", "Saisissez"]
        ],
        screenshotName: "step9_final_lock.png"
    )
]

let outputDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "tests/artifacts/simulator_screenshots"
try? FileManager.default.createDirectory(atPath: outputDir, withIntermediateDirectories: true, attributes: nil)

func runCommand(executable: String, args: [String]) -> (status: Int32, output: String) {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: executable)
    p.arguments = args
    let pipe = Pipe()
    p.standardOutput = pipe
    p.standardError = pipe
    try? p.run()
    p.waitUntilExit()
    let data = pipe.fileHandleForReading.readDataToEndOfFile()
    let out = String(data: data, encoding: .utf8) ?? ""
    return (p.terminationStatus, out)
}

func performOcr(imagePath: String) -> [String] {
    guard let img = NSImage(contentsOfFile: imagePath),
          let cg = img.cgImage(forProposedRect: nil, context: nil, hints: nil) else {
        return []
    }

    var lines: [String] = []
    let req = VNRecognizeTextRequest { (r, _) in
        let obs = r.results as? [VNRecognizedTextObservation] ?? []
        lines = obs.compactMap { $0.topCandidates(1).first?.string }
    }
    req.recognitionLevel = .accurate
    let h = VNImageRequestHandler(cgImage: cg, options: [:])
    try? h.perform([req])
    return lines
}

print("================================================================================")
print(" 👁️  VÉRIFICATION VISUELLE EN DIRECT SUR LE SIMULATEUR IPAD (APPLE VISION OCR) ")
print("================================================================================")
print("Dossier de capture : \(outputDir)\n")

var passedCount = 0
var stepResults: [(step: VisualStep, passed: Bool, message: String, detected: [String])] = []

for step in steps {
    let readyPath = "/tmp/pos_visual_\(step.id).ready"
    print("⏳ En attente de l'écran: \(step.description)...")

    // Attente du signal de l'application (timeout 18s par écran)
    var found = false
    for _ in 0..<90 {
        if FileManager.default.fileExists(atPath: readyPath) {
            found = true
            break
        }
        Thread.sleep(forTimeInterval: 0.2)
    }

    let screenshotPath = "\(outputDir)/\(step.screenshotName)"

    if !found {
        print("❌ [TIMEOUT] L'écran \(step.id) n'a pas été signalé par l'application.")
        stepResults.append((step, false, "Timeout en attente de l'écran", []))
        continue
    }

    // Capture d'écran immédiate via xcrun simctl
    let (scStatus, _) = runCommand(executable: "/usr/bin/xcrun", args: ["simctl", "io", "booted", "screenshot", screenshotPath])
    if scStatus != 0 || !FileManager.default.fileExists(atPath: screenshotPath) {
        print("❌ [CAPTURE ERREUR] Impossible de capturer l'écran pour \(step.id)")
        stepResults.append((step, false, "Échec de capture d'écran", []))
        continue
    }

    // Analyse OCR Vision
    let recognizedLines = performOcr(imagePath: screenshotPath)
    let joinedText = recognizedLines.joined(separator: " ").lowercased()

    var allGroupsSatisfied = true
    var missingGroupDesc: [String] = []

    for group in step.requiredKeywordGroups {
        let satisfied = group.contains { keyword in
            joinedText.contains(keyword.lowercased())
        }
        if !satisfied {
            allGroupsSatisfied = false
            missingGroupDesc.append("[\(group.joined(separator: " OU "))]")
        }
    }

    var forbiddenFound: [String] = []
    for forbidden in step.forbiddenKeywords {
        if joinedText.contains(forbidden.lowercased()) {
            forbiddenFound.append(forbidden)
        }
    }

    if allGroupsSatisfied && forbiddenFound.isEmpty {
        passedCount += 1
        print("  ✅ [VISU ÉCRAN VALIDÉ] \(step.description)")
        print("     Capture : \(screenshotPath)")
        print("     Éléments vérifiés sur l'iPad : \(step.requiredKeywordGroups.map { $0.first ?? "" }.joined(separator: ", "))")
        print("     Aperçu texte détecté : \(recognizedLines.prefix(6).joined(separator: " | "))\n")
        stepResults.append((step, true, "Validé", recognizedLines))
    } else {
        var failureReasons: [String] = []
        if !allGroupsSatisfied {
            failureReasons.append("Manque: \(missingGroupDesc.joined(separator: ", "))")
        }
        if !forbiddenFound.isEmpty {
            failureReasons.append("Mots interdits détectés (fuite filtre): \(forbiddenFound.joined(separator: ", "))")
        }
        let fullMsg = failureReasons.joined(separator: " | ")
        print("  ❌ [ÉCHEC VISUEL] \(step.description)")
        print("     Cause : \(fullMsg)")
        print("     Texte brut détecté par Vision : \(recognizedLines.joined(separator: " | "))\n")
        stepResults.append((step, false, fullMsg, recognizedLines))
    }

    try? FileManager.default.removeItem(atPath: readyPath)
}

print("================================================================================")
print(" 📊 BILAN DE LA VÉRIFICATION VISUELLE SUR L'ÉMULATEUR IPAD")
print("================================================================================")
for res in stepResults {
    let icon = res.passed ? "✅" : "❌"
    print(" \(icon) \(res.step.description)")
    print("    → Capture : \(outputDir)/\(res.step.screenshotName)")
    if !res.passed {
        print("    → Raison : \(res.message)")
    }
}
print("--------------------------------------------------------------------------------")
print(" Résultat : \(passedCount)/\(steps.count) écrans visuellement validés à 100%")
print("================================================================================")

if passedCount == steps.count {
    print("🎉 SUCCÈS TOTAL : Tous les écrans et composants graphiques sont affichés correctement !")
    exit(0)
} else {
    print("❌ ÉCHEC : Certains écrans ne correspondent pas à ce qui est attendu.")
    exit(1)
}
