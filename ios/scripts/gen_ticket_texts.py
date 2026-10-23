#!/usr/bin/env python3
"""Génère TicketTexts.swift depuis les .resx du .NET (textes des tickets).

Usage : python3 -I ios/scripts/gen_ticket_texts.py
"""
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RESX = ROOT / "src/RestaurantPos.Infrastructure/Localization"
OUT = ROOT / "ios/Packages/PosKit/Sources/PosKit/Printing/TicketTexts.swift"
PREFIXES = ("receipt.", "report.", "kitchen_ticket.", "admin.payment_method_")
FILES = {"en": "SharedResource.resx", "fr": "SharedResource.fr.resx", "ar": "SharedResource.ar.resx"}


def read(name):
    out = {}
    for data in ET.parse(RESX / name).getroot().iter("data"):
        key = data.get("name")
        value = data.find("value")
        if key and key.startswith(PREFIXES) and value is not None:
            out[key] = value.text or ""
    return out


def swift(s):
    s = s.replace("\\", "\\\\").replace('"', '\\"')
    s = s.replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")
    return '"' + s + '"'


lines = [
    "// Fichier généré par ios/scripts/gen_ticket_texts.py — ne pas modifier.",
    "// Source : SharedResource*.resx du .NET (préfixes receipt. report. kitchen_ticket. admin.payment_method_).",
    "",
    "/// Textes des tickets, identiques au `Texts.Get` du .NET (repli : langue, anglais, clé).",
    "enum TicketTexts {",
    '    static let supportedLanguages = ["en", "fr", "ar"]',
    "",
    "    static func get(_ language: String, _ key: String, _ args: [String: String] = [:]) -> String {",
    "        var text = table(language)[key] ?? en[key] ?? key",
    "        for name in args.keys.sorted() {",
    '            text = text.replacingOccurrences(of: "{" + name + "}", with: args[name] ?? "")',
    "        }",
    "        return text",
    "    }",
    "",
    "    private static func table(_ language: String) -> [String: String] {",
    '        switch language {',
    '        case "fr": fr',
    '        case "ar": ar',
    "        default: en",
    "        }",
    "    }",
    "",
]
for lang, name in FILES.items():
    table = read(name)
    lines.append(f"    private static let {lang}: [String: String] = [")
    lines += [f"        {swift(k)}: {swift(table[k])}," for k in sorted(table)]
    lines.append("    ]")
lines.append("}")
OUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
