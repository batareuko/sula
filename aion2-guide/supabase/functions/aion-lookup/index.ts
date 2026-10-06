// Supabase Edge Function: пошук персонажа Aion 2 за ніком.
//
// Браузер не може звертатися до PlayNC напряму (CORS / 403 при заголовку Origin), тому запит іде через цю функцію.
// Джерело даних: неофіційні веб-ендпоінти офіційного сайту Aion 2. Вони не документовані й можуть змінитися.
// Коли PlayNC відкриє офіційне API для Aion 2, достатньо замінити `provider` нижче: решта сайту
// отримує той самий формат відповіді.
//
// POST { name }                             -> { character, game } | { matches } | { error }
// POST { characterId, serverId, region }    -> { character, game } | { error }   (оновлення без пошуку)
// `game` — компактний повний знімок: стати, дошки Даеваніона, титули, спорядження, скіни, пет, крила, вміння.

const SEARCH_URL = 'https://api-search.plaync.com/aion2global/search/v2/character';
const INFO_URL = 'https://aion2.plaync.com/api/character/info';
const EQUIP_URL = 'https://aion2.plaync.com/api/character/equipment';
const ICON_BASE = 'https://assets.playnccdn.com/static-aion2-gamedata/resources/';
const REGIONS = (Deno.env.get('AION_REGIONS') ?? 'nae,naw,eu,asia,latam').split(',');
const CACHE_TTL_MS = 10 * 60 * 1000;
const RATE_LIMIT = 20; // запитів на хвилину з однієї IP
const MAX_MATCHES = 6;

const cors = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Headers': 'authorization, x-client-info, apikey, content-type',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
};

interface Character {
  name: string;
  characterId: string;
  serverId: number;
  serverName: string;
  region: string;
  className: string;
  level: number;
  raceName: string;
  itemLevel: number; // шкала гайда (1000–2800): поле ItemLevel зі статів
  combatPower: number; // поле combatPower з API (інша, значно більша шкала)
  profileImage: string;
}

/* Масиви замість об'єктів, щоб знімок займав кілька КБ у базі */
interface Game {
  v: 1;
  title: [string, string];                                   // назва, рідкість
  gender: string;
  stats: [string, string, number][];                         // тип, назва, значення
  boards: [string, number, number][];                        // дошка Даеваніона, відкрито, усього вузлів
  titles: { owned: number; cats: [string, string, string, number, number][] };
  eq: [string, string, string, number, number, string][] | null;   // слот, назва, рідкість, заточка, exceed, іконка
  skins: [string, string, string, string][];                 // слот, назва, рідкість, іконка
  pet: [string, number, string] | null;                      // назва, рівень, іконка
  wing: [string, string, number, string] | null;             // назва, рідкість, заточка, іконка
  skills: [string, string, number, number, string][];        // назва, категорія, рівень, екіпіровано, іконка
}

const cache = new Map<string, { t: number; v: unknown }>();
const hits = new Map<string, { t: number; n: number }>();

async function cached<T>(key: string, load: () => Promise<T>): Promise<T> {
  const hit = cache.get(key);
  if (hit && Date.now() - hit.t < CACHE_TTL_MS) return hit.v as T;
  const v = await load();
  cache.set(key, { t: Date.now(), v });
  if (cache.size > 500) cache.delete(cache.keys().next().value as string);
  return v;
}

async function getJson(url: string): Promise<any> {
  const res = await fetch(url, { signal: AbortSignal.timeout(8000), headers: { Accept: 'application/json' } });
  if (!res.ok) throw new Error('upstream ' + res.status);
  return res.json();
}

const stripTags = (s: string) => s.replace(/<[^>]*>/g, '');
const str = (v: unknown, n: number) => String(v ?? '').slice(0, n);
const num = (v: unknown) => Number(v) || 0;
/** Іконки зберігаємо лише як ім'я файлу з CDN гри; усе інше відкидаємо */
const icon = (u: unknown) => {
  const s = String(u ?? '');
  const f = s.startsWith(ICON_BASE) ? s.slice(ICON_BASE.length) : '';
  return /^[A-Za-z0-9_.-]{1,80}\.png$/.test(f) ? f : '';
};

/** characterId у відповіді пошуку вже закодований (%3D); нормалізуємо, щоб не закодувати двічі */
const encId = (id: string) => encodeURIComponent(decodeURIComponent(id));

// ----- provider: неофіційний веб-API (замінити на офіційний, коли з'явиться) -----
const provider = {
  async search(name: string): Promise<{ characterId: string; serverId: number; region: string }[]> {
    const lists = await Promise.all(REGIONS.map((region) =>
      cached('s:' + region + ':' + name.toLowerCase(), () =>
        getJson(`${SEARCH_URL}?keyword=${encodeURIComponent(name)}&region=${region}&localeInfo=en-US`)
          .then((d) => (d.list ?? []) as any[])
          .catch(() => [] as any[]) // регіон може не відповідати — решта регіонів усе одно працює
      )
    ));
    return lists.flat()
      .filter((x) => stripTags(String(x.name)).toLowerCase() === name.toLowerCase())
      .map((x) => ({ characterId: String(x.characterId), serverId: Number(x.serverId), region: String(x.region) }));
  },

  rawInfo(characterId: string, serverId: number, region: string): Promise<any> {
    return cached(`i:${region}:${serverId}:${encId(characterId)}`, () =>
      getJson(`${INFO_URL}?lang=en-US&characterId=${encId(characterId)}&serverId=${serverId}&region=${region}`));
  },

  rawEquipment(characterId: string, serverId: number, region: string): Promise<any> {
    return cached(`e:${region}:${serverId}:${encId(characterId)}`, () =>
      getJson(`${EQUIP_URL}?lang=en-US&characterId=${encId(characterId)}&serverId=${serverId}&region=${region}`));
  },

  async info(characterId: string, serverId: number, region: string): Promise<Character> {
    const d = await this.rawInfo(characterId, serverId, region);
    const p = d.profile;
    if (!p) throw new Error('no profile');
    const item = (d.stat?.statList ?? []).find((s: any) => s.type === 'ItemLevel');
    const img = String(p.profileImage ?? '');
    return {
      name: String(p.characterName),
      characterId: encId(characterId),
      serverId: Number(p.serverId),
      serverName: String(p.serverName),
      region,
      className: String(p.className),
      level: Number(p.characterLevel) || 0,
      raceName: String(p.raceName ?? ''),
      itemLevel: Number(item?.value) || 0,
      combatPower: Number(p.combatPower) || 0,
      profileImage: img.startsWith('https://') ? img : '',
    };
  },

  async game(characterId: string, serverId: number, region: string): Promise<Game> {
    const [d, e] = await Promise.all([
      this.rawInfo(characterId, serverId, region),
      this.rawEquipment(characterId, serverId, region).catch(() => null), // без спорядження решта знімка все одно корисна
    ]);
    const p = d.profile ?? {};
    const pw = e?.petwing ?? {};
    return {
      v: 1,
      title: [str(p.titleName, 40), str(p.titleGrade, 16)],
      gender: str(p.genderName, 10),
      stats: (d.stat?.statList ?? []).slice(0, 40).map((x: any) => [str(x.type, 16), str(x.name, 40), num(x.value)]),
      boards: (d.daevanion?.boardList ?? []).slice(0, 12).map((b: any) => [str(b.name, 24), num(b.openNodeCount), num(b.totalNodeCount)]),
      titles: {
        owned: num(d.title?.ownedCount),
        cats: (d.title?.titleList ?? []).slice(0, 8).map((t: any) => [str(t.equipCategory, 16), str(t.name, 40), str(t.grade, 16), num(t.ownedCount), num(t.totalCount)]),
      },
      eq: e ? (e.equipment?.equipmentList ?? []).slice(0, 30).map((x: any) =>
        [str(x.slotPosName, 16), str(x.name, 60), str(x.grade, 16), num(x.enchantLevel), num(x.exceedLevel), icon(x.icon)]) : null,
      skins: (e?.equipment?.skinList ?? []).slice(0, 20).map((x: any) => [str(x.slotPosName, 16), str(x.name, 60), str(x.grade, 16), icon(x.icon)]),
      pet: pw.pet?.name ? [str(pw.pet.name, 40), num(pw.pet.level), icon(pw.pet.icon)] : null,
      wing: pw.wing?.name ? [str(pw.wing.name, 40), str(pw.wing.grade, 16), num(pw.wing.enchantLevel), icon(pw.wing.icon)] : null,
      skills: (e?.skill?.skillList ?? []).filter((x: any) => x.acquired).slice(0, 60).map((x: any) =>
        [str(x.name, 40), str(x.category, 10), num(x.skillLevel), x.equip ? 1 : 0, icon(x.icon)]),
    };
  },
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { ...cors, 'Content-Type': 'application/json' } });
}

function limited(ip: string): boolean {
  const now = Date.now();
  const h = hits.get(ip);
  if (!h || now - h.t > 60_000) { hits.set(ip, { t: now, n: 1 }); return false; }
  h.n++;
  return h.n > RATE_LIMIT;
}

export async function handler(req: Request): Promise<Response> {
  if (req.method === 'OPTIONS') return new Response(null, { headers: cors });
  if (req.method !== 'POST') return json({ error: 'method_not_allowed' }, 405);
  if (limited(req.headers.get('x-forwarded-for')?.split(',')[0].trim() ?? 'unknown')) return json({ error: 'rate_limited' }, 429);

  let body: any;
  try { body = await req.json(); } catch { return json({ error: 'bad_request' }, 400); }

  try {
    if (body.characterId) {
      const serverId = Number(body.serverId);
      const region = String(body.region ?? '');
      if (!Number.isInteger(serverId) || !REGIONS.includes(region) || String(body.characterId).length > 200) return json({ error: 'bad_request' }, 400);
      const id = String(body.characterId);
      const [character, game] = await Promise.all([provider.info(id, serverId, region), provider.game(id, serverId, region)]);
      return json({ character, game });
    }

    const name = String(body.name ?? '').trim();
    if (name.length < 2 || name.length > 24 || !/^[\p{L}\p{N}]+$/u.test(name)) return json({ error: 'bad_name' }, 400);

    const found = await provider.search(name);
    if (!found.length) return json({ error: 'not_found' }, 404);
    const chars = await Promise.all(found.slice(0, MAX_MATCHES).map((f) => provider.info(f.characterId, f.serverId, f.region)));
    if (chars.length > 1) return json({ matches: chars }); // повний знімок — після вибору персонажа
    const c = chars[0];
    return json({ character: c, game: await provider.game(c.characterId, c.serverId, c.region) });
  } catch {
    return json({ error: 'upstream' }, 502);
  }
}

if (typeof Deno !== 'undefined') Deno.serve(handler);
