// Під час публікації (GitHub Pages) створює для кожного перекладеного пачноуту коротку сторінку /p/<id>/:
// у ній прев'ю для Discord і соцмереж (назва + підсумок), а браузер одразу переходить на /#patch=<id>.
// Запуск: node aion2-guide/tools/patch-pages.mjs _site
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';

const out = process.argv[2] || '_site';
const site = 'https://guide.sulaslova.com';
const ctx = { window: {} };
vm.createContext(ctx);
vm.runInContext(fs.readFileSync(new URL('../data.js', import.meta.url), 'utf8'), ctx);
const patches = ctx.window.GUIDE.patches || [];

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const plain = (s) => String(s).replace(/\[([^|\]]+)\|([^\]]+)\]/g, '$1 ($2)');

let n = 0;
for (const pt of patches) {
  if (!/^[a-f0-9]{24}$/.test(pt.id)) continue;
  const target = `/#patch=${pt.id}`;
  const title = `${plain(pt.title)}: що змінилось і як вплине`;
  // цілі пункти підсумку, поки вміщаються у ~300 символів прев'ю
  const desc = pt.tldr.map(plain).reduce((acc, t) => (acc && (acc + ' · ' + t).length > 300 ? acc : acc ? acc + ' · ' + t : t), '');
  const html = `<!doctype html>
<html lang="uk">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc(title)} · 1 HP · Aion 2</title>
<meta name="description" content="${esc(desc)}">
<meta name="theme-color" content="#f5a623">
<meta property="og:type" content="article">
<meta property="og:site_name" content="1 HP · Aion 2">
<meta property="og:locale" content="uk_UA">
<meta property="og:title" content="${esc(title)}">
<meta property="og:description" content="${esc(desc)}">
<meta property="og:url" content="${site}/p/${pt.id}/">
<meta property="og:image" content="${site}/og.png">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta name="twitter:card" content="summary_large_image">
<link rel="canonical" href="${site}${target}">
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<meta http-equiv="refresh" content="0; url=${target}">
<style>body{margin:0;padding:24px;background:#0a0c12;color:#e8eaf0;font:16px/1.5 system-ui,sans-serif}a{color:#f5a623}</style>
</head>
<body>
<p>Відкриваю «${esc(plain(pt.title))}»… <a href="${target}">Перейти до пачноуту</a></p>
<script>location.replace(${JSON.stringify(target)});</script>
</body>
</html>
`;
  const dir = path.join(out, 'p', pt.id);
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'index.html'), html);
  n++;
}
console.log(`patch pages: ${n}`);
