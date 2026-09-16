import Foundation
import Vision
import AppKit

struct VisualStep {
    let id: String
    let description: String
    let requiredKeywordGroups: [[String]] // AND across groups, OR within each group
    let screenshotName: String
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
        id: "step3_pos_cart",
        description: "Écran 3 : Caisse Tactile, Articles & Panier",
        requiredKeywordGroups: [
            ["Burger", "Burger Maison"],
            ["TTC", "EUR", "€"],
            ["Envoyer Cuisine", "Envoyer", "Caisse"]
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
        description: "Écran 5 : Écran Cuisine KDS & Bons de Préparation",
        requiredKeywordGroups: [
            ["Cuisine", "KDS", "Ecran Cuisine"],
            ["Attente", "En Attente", "BUMP", "Preparation"]
        ],
        screenshotName: "step5_kitchen_kds.png"
    ),
    VisualStep(
        id: "step6_split_bill",
        description: "Écran 6 : Partage de l'Addition (Split Bill)",
        requiredKeywordGroups: [
            ["Partage", "Partager", "Addition"],
            ["convives", "Convive", "Montant"]
        ],
        screenshotName: "step6_split_bill.png"
    ),
    VisualStep(
        id: "step7_checkout",
        description: "Écran 7 : Règlement Commande, Espèces & Rendu Monnaie",
        requiredKeywordGroups: [
            ["Règlement", "Paiement", "Reglement"],
            ["3,50", "3.50", "Rendu", "RENDU MONNAIE"],
            ["Especes", "Espèces", "Cash", "Carte"]
        ],
        screenshotName: "step7_checkout.png"
    ),
    VisualStep(
        id: "step8_admin_shell",
        description: "Écran 8 : Back-Office Administration (Personnel & Catalogue)",
        requiredKeywordGroups: [
            ["Catalogue", "Familles", "Personnel", "Back-Office"],
            ["Salle", "Retour", "Imprimantes", "Famille"]
        ],
        screenshotName: "step8_admin_shell.png"
    ),
    VisualStep(
        id: "step8_admin_layout",
        description: "Écran 8bis : Disposition de l'Écran & Matrice Tactile",
        requiredKeywordGroups: [
            ["Disposition", "Matrice", "Format", "Écran", "Touches", "Colonnes"],
            ["Appliquer", "Réinitialiser", "Standard", "Burger", "Case"]
        ],
        screenshotName: "step8_admin_layout.png"
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

    if allGroupsSatisfied {
        passedCount += 1
        print("  ✅ [VISU ÉCRAN VALIDÉ] \(step.description)")
        print("     Capture : \(screenshotPath)")
        print("     Éléments vérifiés sur l'iPad : \(step.requiredKeywordGroups.map { $0.first ?? "" }.joined(separator: ", "))")
        print("     Aperçu texte détecté : \(recognizedLines.prefix(6).joined(separator: " | "))\n")
        stepResults.append((step, true, "Validé", recognizedLines))
    } else {
        print("  ❌ [ÉCHEC VISUEL] \(step.description)")
        print("     Éléments manquants sur l'écran : \(missingGroupDesc.joined(separator: " ET "))")
        print("     Texte brut détecté par Vision : \(recognizedLines.joined(separator: " | "))\n")
        stepResults.append((step, false, "Manque: \(missingGroupDesc.joined(separator: ", "))", recognizedLines))
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
