// Supabase Edge Function: пошук персонажа Aion 2 за ніком.
//
// Браузер не може звертатися до PlayNC напряму (CORS / 403 при заголовку Origin), тому запит іде через цю функцію.
// Джерело даних: неофіційні веб-ендпоінти офіційного сайту Aion 2. Вони не документовані й можуть змінитися.
// Коли PlayNC відкриє офіційне API для Aion 2, достатньо замінити `provider` нижче: решта сайту
// отримує той самий формат відповіді.
//
// POST { name }                             -> { character } | { matches } | { error }
// POST { characterId, serverId, region }    -> { character } | { error }   (оновлення без пошуку)

const SEARCH_URL = 'https://api-search.plaync.com/aion2global/search/v2/character';
const INFO_URL = 'https://aion2.plaync.com/api/character/info';
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

  async info(characterId: string, serverId: number, region: string): Promise<Character> {
    const d = await cached(`i:${region}:${serverId}:${characterId}`, () =>
      getJson(`${INFO_URL}?lang=en-US&characterId=${encId(characterId)}&serverId=${serverId}&region=${region}`));
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
      return json({ character: await provider.info(String(body.characterId), serverId, region) });
    }

    const name = String(body.name ?? '').trim();
    if (name.length < 2 || name.length > 24 || !/^[\p{L}\p{N}]+$/u.test(name)) return json({ error: 'bad_name' }, 400);

    const found = await provider.search(name);
    if (!found.length) return json({ error: 'not_found' }, 404);
    const chars = await Promise.all(found.slice(0, MAX_MATCHES).map((f) => provider.info(f.characterId, f.serverId, f.region)));
    return chars.length === 1 ? json({ character: chars[0] }) : json({ matches: chars });
  } catch {
    return json({ error: 'upstream' }, 502);
  }
}

if (typeof Deno !== 'undefined') Deno.serve(handler);
