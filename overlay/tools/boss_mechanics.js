// Builds overlay/data/boss_mechanics.json from the site's mechanics guide (aion2-guide/data.js → guides g-dungeons).
// Run from the repo root: node overlay/tools/boss_mechanics.js aion2-guide/data.js overlay/data/npcs/en.json overlay/data/boss_mechanics.json
const fs = require('fs');
global.window = {};
eval(fs.readFileSync(process.argv[2], 'utf8'));
const npcs = JSON.parse(fs.readFileSync(process.argv[3], 'utf8'));
const bossNames = new Set(Object.values(npcs).filter(n => n.isBoss).map(n => n.name));
const g = window.GUIDE.guides.find(x => x.id === 'g-dungeons');
const plain = s => s.replace(/\[([^\]|]+)\|([^\]]+)\]/g, '$1');
const out = {};
const unmatched = new Set();
for (const sec of g.sections) {
  let cur = null;
  for (const item of sec.list || []) {
    const names = [...item.matchAll(/\[[^\]|]+\|([^\]]+)\]/g)].map(m => m[1]);
    const known = names.filter(n => bossNames.has(n));
    names.filter(n => !bossNames.has(n)).forEach(n => unmatched.add(n));
    if (known.length) cur = known[known.length - 1];
    if (!cur) continue;
    // the overlay already names the boss: drop "first two bosses are easy", "Final — X." and a leading "X:" / "X (wolf):"
    const uk = (item.match(new RegExp('\\[([^\\]|]+)\\|' + cur.replace(/[.*+?^${}()|[\]\\']/g, '\\$&') + '\\]')) || [])[1];
    let text = plain(item).replace(/Перші два боси прості[^.]*\.\s*/, '').replace(/^Фінал\s*—\s*/, '');
    if (uk && text.startsWith(uk)) text = text.slice(uk.length).replace(/^\s*(\([^)]*\))?\s*[:.]\s*/, '');
    else if (text.startsWith('птах ' + uk)) text = text.slice(('птах ' + uk).length).replace(/^\s*[:.]\s*/, '');
    if (text.length < 8) continue;
    (out[cur] ||= []).push(text[0].toUpperCase() + text.slice(1));
  }
}
console.error('bosses', Object.keys(out).length, 'unmatched names', [...unmatched].join(' · '));
fs.writeFileSync(process.argv[4], JSON.stringify({ _source: '1 HP: aion2-guide/data.js → guides g-dungeons (our own texts), keyed by the boss English name in npcs/en.json', uk: out }, null, 1) + '\n');
