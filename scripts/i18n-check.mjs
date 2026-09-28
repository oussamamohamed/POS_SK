#!/usr/bin/env node
// Vérifie les dictionnaires web : clés utilisées ↔ clés définies, valeurs vides, français restant.
// Usage : node scripts/i18n-check.mjs [--langs en,fr,ar] [--no-french]
import { readFileSync } from 'node:fs';

const root = new URL('../src/RestaurantPos.Api/wwwroot/', import.meta.url);
const read = p => readFileSync(new URL(p, root), 'utf8');
const args = process.argv.slice(2);
const langsFlagIndex = args.indexOf('--langs');
const langs = (args.find(a => a.startsWith('--langs='))?.split('=')[1] ?? (langsFlagIndex === -1 ? undefined : args[langsFlagIndex + 1]) ?? 'en,fr,ar').split(',');
const js = read('app.js'), html = read('index.html');

const usedHtmlAttr = new Set();
for (const m of html.matchAll(/data-i18n(?:-[a-z-]+)?="([a-z_]+\.[a-z0-9_.]+)"/g)) usedHtmlAttr.add(m[1]);
const used = new Set(usedHtmlAttr);
for (const m of js.matchAll(/\bt\(\s*['"`]([a-z_]+\.[a-z0-9_.]+)['"`]/g)) used.add(m[1]);

let errors = 0;
const fail = msg => { errors++; console.error('✘ ' + msg); };

for (const lang of langs) {
  const dict = JSON.parse(read(`i18n/${lang}.json`));
  for (const k of used) if (!(k in dict)) fail(`${lang}: clé manquante ${k}`);
  for (const [k, v] of Object.entries(dict)) {
    if (!used.has(k)) fail(`${lang}: clé orpheline ${k}`);
    if (typeof v !== 'string' || !v.trim()) fail(`${lang}: valeur vide ${k}`);
  }
  // apply() lit data-i18n* sans passer de params : une clé rendue ainsi ne peut pas contenir de placeholder.
  for (const k of usedHtmlAttr) {
    if (typeof dict[k] === 'string' && dict[k].includes('{')) fail(`${lang}: clé ${k} utilisée par data-i18n* contient un placeholder non substitué`);
  }
}

if (args.includes('--no-french')) {
  // Heuristique : chaîne littérale contenant un accent français ou un mot FR courant, hors commentaires.
  const FR = /['"`>][^'"`<]*(?:[éèêàùçôî]|\b(?:le|la|les|des|du|une|pour|avec|introuvable|commande|annuler|valider|enregistrer)\b)[^'"`<]*['"`<]/i;
  const scan = (name, text, { skipI18nAttr = false } = {}) => text.split('\n').forEach((line, i) => {
    const code = line.replace(/\/\/.*$/, '').replace(/<!--.*?-->/g, '');
    if (/console\.(warn|error|log)/.test(code)) return;
    // Texte figé envoyé au serveur et stocké tel quel dans le journal NF525 (append-only) : la
    // traçabilité fiscale doit rester stable, indépendante de la langue de l'opérateur qui saisit.
    if (line.includes('nf525-texte-fixe')) return;
    // index.html garde volontairement le texte français comme contenu initial des éléments
    // data-i18n* (lu avant l'exécution de apply()) : une ligne portant cet attribut est déjà
    // couverte par une clé, ce n'est pas du français non extrait.
    if (skipI18nAttr && code.includes('data-i18n')) return;
    if (FR.test(code)) fail(`${name}:${i + 1} français en dur : ${code.trim().slice(0, 100)}`);
  });
  scan('app.js', js);
  scan('index.html', html.replace(/<option value="fr">Français<\/option>/, ''), { skipI18nAttr: true });
}

console.log(errors ? `${errors} problème(s)` : `✔ ${used.size} clés OK (${langs.join(', ')})`);
process.exit(errors ? 1 : 0);
