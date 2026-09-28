// Runtime i18n : clés sémantiques, repli anglais puis clé. Chargé avant app.js.
(() => {
    const SUPPORTED = ['en', 'fr', 'ar'];
    const stored = (() => { try { return localStorage.getItem('pos_lang'); } catch { return null; } })();
    const browser = (navigator.language || 'en').slice(0, 2).toLowerCase();
    const lang = SUPPORTED.includes(stored) ? stored : SUPPORTED.includes(browser) ? browser : 'en';
    const dir = lang === 'ar' ? 'rtl' : 'ltr';
    let dict = {}, fallback = {};

    document.documentElement.lang = lang;
    document.documentElement.dir = dir;

    const load = l => fetch(`i18n/${l}.json`, { cache: 'no-cache' }).then(r => (r.ok ? r.json() : {})).catch(() => ({}));

    function t(key, params) {
        let text = dict[key] ?? fallback[key];
        if (text === undefined) {
            console.warn(`[i18n] clé absente : ${key}`);
            text = key;
        } else if (dict[key] === undefined && lang !== 'en') {
            console.warn(`[i18n] ${key} absente en ${lang}, repli anglais`);
        }
        if (params) for (const [k, v] of Object.entries(params)) text = text.replaceAll(`{${k}}`, String(v));
        return text;
    }

    const ATTRS = [['data-i18n-placeholder', 'placeholder'], ['data-i18n-aria-label', 'aria-label'], ['data-i18n-title', 'title']];
    function apply(root = document) {
        root.querySelectorAll('[data-i18n]').forEach(el => { el.textContent = t(el.dataset.i18n); });
        for (const [data, attr] of ATTRS) root.querySelectorAll(`[${data}]`).forEach(el => el.setAttribute(attr, t(el.getAttribute(data))));
    }

    function setLanguage(l) {
        try { localStorage.setItem('pos_lang', l); } catch { /* stockage indisponible : choix perdu au rechargement */ }
        location.reload();
    }

    window.t = t;
    window.i18n = { lang, dir, locale: lang === 'ar' ? 'ar-u-nu-latn' : lang, setLanguage, apply };
    window.i18nReady = Promise.all([load(lang), lang === 'en' ? Promise.resolve({}) : load('en')])
        .then(([d, f]) => { dict = d; fallback = lang === 'en' ? d : f; })
        .then(() => new Promise(r => (document.readyState === 'loading' ? document.addEventListener('DOMContentLoaded', r) : r())))
        .then(() => apply());
})();
