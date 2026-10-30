// Builds overlay/data/world_maps.json from Aion2Data's interactive maps (https://aion2data.com/map): each map's size and
// areas and the places on it (field bosses, dungeon entrances, rifts, Kibelisks, monoliths, gathering, pet-soul
// monsters, Hidden Cube spots). Only names and points are kept; the map pictures stay on aion2data.com and the overlay
// downloads them when the map is opened (credit: Aion2Data).
// Icons: the game's own markers and item icons by their resource name (served by the game's CDN as
// https://assets.playnccdn.com/static-aion2-gamedata/resources/<name>.png); a place keeps its own icon in "i".
// Run from the repo root (Node 18+): node overlay/tools/world_maps.js overlay/data/world_maps.json aion2-guide/world_maps.json
const fs = require('fs');
const site = 'https://aion2data.com';
const keep = ['bosses', 'dungeons', 'rifts', 'kibelisks', 'monoliths', 'gather', 'pets', 'cubes'];

async function main() {
  const page = await (await fetch(site + '/map/verteron')).text();
  const flight = [...page.matchAll(/self\.__next_f\.push\(\[1,"((?:[^"\\]|\\.)*)"\]\)/g)].map(m => JSON.parse('"' + m[1] + '"')).join('');
  const start = flight.indexOf('{"maps":[');
  let depth = 0, end = start;
  for (; end < flight.length; end++) {
    if (flight[end] === '{' || flight[end] === '[') depth++;
    else if (flight[end] === '}' || flight[end] === ']') { if (--depth === 0) break; }
  }
  const meta = JSON.parse(flight.slice(start, end + 1));
  const maps = [];
  for (const m of meta.maps) {
    const data = await (await fetch(`${site}/map/${m.slug}/data.json`)).json();
    const layers = {};
    for (const key of keep) {
      const list = data.layers[key];
      if (!list?.length) continue;
      layers[key] = list.map(p => {
        const o = { n: p.n, pts: p.pts };
        if (p.s) o.s = p.s;
        if (p.f) o.f = p.f;
        // its own icon: a gathering node's item, a boss's portrait, a pet's portrait (shown when pointed at)
        if (p.i) o.i = p.i.replace(/^.*\//, '').replace(/\.\w+$/, '');
        return o;
      });
    }
    maps.push({
      slug: m.slug, name: m.name, faction: m.faction, metres: m.metres,
      image: { src: site + m.image.src, small: site + m.image.small, w: m.image.w, h: m.image.h },
      areas: (m.areas || []).filter(a => a.r <= 2).map(a => ({ n: a.n, x: a.x, y: a.y, r: a.r })),
      layers,
    });
    console.error(m.slug, Object.entries(layers).map(([k, v]) => `${k} ${v.length}`).join(', '));
  }
  const out = JSON.stringify({
    _source: 'Aion2Data interactive maps (https://aion2data.com/map), game version of the site at build time; points are in ' +
      'the map\'s own units: x 0–10000 across the picture\'s width, y in the same units down from the top',
    _built: new Date().toISOString().slice(0, 10),
    maps,
  }) + '\n';
  for (const file of process.argv.slice(2)) fs.writeFileSync(file, out);
}
main().catch(e => { console.error(e); process.exit(1); });
