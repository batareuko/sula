// Supabase Edge Function: результати боїв з оверлея 1 HP → рейтинг DPS 1 HP.
//
// Оверлей надсилає лише результат СВОГО персонажа після вбивства боса:
//   POST { key, record: { character, class, serverId, bossCode, bossName, zone, dps, damage, durationMs, partySize, place, foughtAt } }
//   -> { saved, place, total, topPct, best, personalBest }   місце серед учасників 1 HP (за найкращим результатом кожного)
//   POST { key, check: true } -> { ok: true }               перевірка ключа з налаштувань оверлея
//   POST { key, bosses: { serverId, by, list: [{ code, alive, at }] } } -> { saved }
//        час польових босів з ігрового списку (оверлей): alive=false — повернеться о `at`, alive=true — живий з `at`;
//        пишеться в boss_kills для цього сервера (source='game'), сайт і Discord беруть його замість «вбито + цикл»
// Ключ створюється на сайті (розділ «Рейтинг DPS»); у базі лише його SHA-256 (таблиця overlay_keys).
// SUPABASE_URL і SUPABASE_SERVICE_ROLE_KEY Supabase додає сам.

const SUPABASE_URL = Deno.env.get('SUPABASE_URL') ?? '';
const SERVICE_KEY = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') ?? '';
const RATE_LIMIT = 30; // запитів на хвилину з одного ключа

const cors = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Headers': 'authorization, x-client-info, apikey, content-type',
  'Access-Control-Allow-Methods': 'POST, OPTIONS',
};

const hits = new Map<string, { t: number; n: number }>();

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { ...cors, 'Content-Type': 'application/json' } });
}

async function rest(path: string, init: RequestInit = {}): Promise<any> {
  const res = await fetch(`${SUPABASE_URL}/rest/v1/${path}`, {
    ...init,
    headers: { apikey: SERVICE_KEY, Authorization: `Bearer ${SERVICE_KEY}`, 'Content-Type': 'application/json', ...(init.headers ?? {}) },
  });
  if (!res.ok) throw new Error(`rest ${res.status}: ${await res.text()}`);
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

async function sha256(text: string): Promise<string> {
  const buf = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(buf)].map((b) => b.toString(16).padStart(2, '0')).join('');
}

function limited(id: string): boolean {
  const now = Date.now();
  const h = hits.get(id);
  if (!h || now - h.t > 60_000) { hits.set(id, { t: now, n: 1 }); return false; }
  h.n++;
  return h.n > RATE_LIMIT;
}

const str = (v: unknown, max: number) => String(v ?? '').trim().slice(0, max);
const int = (v: unknown) => (Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : NaN);
const CLASSES = ['Gladiator', 'Templar', 'Ranger', 'Assassin', 'Sorcerer', 'Elementalist', 'Cleric', 'Chanter', 'Brawler', 'Unknown', ''];

/** Перевірений запис або причина відмови */
export function validate(r: any): { row?: Record<string, unknown>; error?: string } {
  if (!r || typeof r !== 'object') return { error: 'bad_record' };
  const row = {
    character: str(r.character, 40),
    class: CLASSES.includes(String(r.class ?? '')) ? String(r.class ?? '') : '',
    server_id: int(r.serverId) || 0,
    boss_code: int(r.bossCode),
    boss_name: str(r.bossName, 80),
    zone: str(r.zone, 80) || null,
    dps: Number(r.dps),
    damage: int(r.damage),
    duration_ms: int(r.durationMs),
    party_size: int(r.partySize),
    place: int(r.place),
    fought_at: str(r.foughtAt, 40),
  };
  if (!row.character || row.character.startsWith('#')) return { error: 'no_character' };
  if (!(row.boss_code > 0) || !row.boss_name) return { error: 'no_boss' };
  if (!(row.dps > 0 && row.dps < 1e9) || !(row.damage > 0)) return { error: 'bad_numbers' };
  if (!(row.duration_ms >= 5000 && row.duration_ms <= 7_200_000)) return { error: 'bad_duration' };
  if (!(row.party_size >= 1 && row.party_size <= 50) || !(row.place >= 1 && row.place <= row.party_size)) return { error: 'bad_party' };
  // DPS має сходитися з уроном і тривалістю (з запасом на округлення та різні способи рахунку)
  const implied = row.damage / (row.duration_ms / 1000);
  if (row.dps > implied * 3 + 1) return { error: 'bad_numbers' };
  const at = Date.parse(row.fought_at);
  if (!Number.isFinite(at) || at > Date.now() + 10 * 60_000 || at < Date.now() - 30 * 86_400_000) return { error: 'bad_time' };
  row.fought_at = new Date(at).toISOString();
  return { row };
}

/** Рядки boss_kills з ігрового списку босів або причина відмови */
export function bossRows(b: any, now = Date.now()): { rows?: Record<string, unknown>[]; error?: string } {
  if (!b || typeof b !== 'object' || !Array.isArray(b.list)) return { error: 'bad_bosses' };
  const server = int(b.serverId);
  if (!(server >= 1000 && server <= 9999)) return { error: 'bad_server' };
  if (b.list.length > 120) return { error: 'too_many' };
  const by = str(b.by, 40) || null;
  const rows: Record<string, unknown>[] = [];
  for (const x of b.list) {
    const code = int(x?.code);
    const at = Number(x?.at);
    if (!(code >= 2_000_000 && code <= 2_999_999)) continue; // польові боси
    if (!Number.isFinite(at) || at < now - 26 * 3600_000 || at > now + 26 * 3600_000) continue;
    const alive = x.alive === true;
    if (!alive && at < now - 3600_000) continue; // «повернеться о» в минулому: застаріле
    const iso = new Date(at).toISOString();
    rows.push({ server_id: server, boss_id: String(code), killed_at: alive ? iso : new Date(now).toISOString(),
      respawn_at: iso, alive, source: 'game', by_name: by, by_user: null });
  }
  return { rows };
}

/** Місце користувача серед учасників за найкращим результатом кожного на цьому босі */
export function rank(rows: { user_id: string; dps: number }[], userId: string) {
  const best = new Map<string, number>();
  for (const r of rows) if (!best.has(r.user_id) || r.dps > best.get(r.user_id)!) best.set(r.user_id, r.dps);
  const sorted = [...best.entries()].sort((a, b) => b[1] - a[1]);
  const place = sorted.findIndex(([u]) => u === userId) + 1;
  const total = sorted.length;
  return { place, total, topPct: place > 0 ? Math.max(1, Math.round((100 * place) / total)) : null, best: best.get(userId) ?? null };
}

export async function handler(req: Request): Promise<Response> {
  if (req.method === 'OPTIONS') return new Response(null, { headers: cors });
  if (req.method !== 'POST') return json({ error: 'method_not_allowed' }, 405);
  let body: any;
  try { body = await req.json(); } catch { return json({ error: 'bad_request' }, 400); }

  const key = String(body?.key ?? '');
  if (!/^[A-Za-z0-9_-]{32,64}$/.test(key)) return json({ error: 'bad_key' }, 401);
  const hash = await sha256(key);
  if (limited(hash)) return json({ error: 'rate_limited' }, 429);

  try {
    const owners = await rest(`overlay_keys?key_hash=eq.${hash}&select=user_id`);
    const userId = owners?.[0]?.user_id as string | undefined;
    if (!userId) return json({ error: 'bad_key' }, 401);
    if (body.check) return json({ ok: true });

    if (body.bosses) {
      const { rows, error } = bossRows(body.bosses);
      if (!rows) return json({ error }, 400);
      if (rows.length) {
        await rest('boss_kills?on_conflict=server_id,boss_id', {
          method: 'POST',
          headers: { Prefer: 'resolution=merge-duplicates,return=minimal' },
          body: JSON.stringify(rows),
        });
      }
      return json({ saved: rows.length });
    }

    const { row, error } = validate(body.record);
    if (!row) return json({ error }, 400);
    const saved = await rest('dps_records', {
      method: 'POST',
      headers: { Prefer: 'resolution=ignore-duplicates,return=representation' },
      body: JSON.stringify([{ ...row, user_id: userId }]),
    });
    const all = await rest(`dps_records?boss_code=eq.${row.boss_code}&select=user_id,dps&order=dps.desc&limit=5000`);
    const r = rank(all ?? [], userId);
    return json({ saved: Array.isArray(saved) && saved.length > 0, ...r, personalBest: r.best !== null && (row.dps as number) >= r.best });
  } catch (e) {
    console.error(e);
    return json({ error: 'server' }, 502);
  }
}

if (typeof Deno !== 'undefined' && (Deno as any).serve) Deno.serve(handler);
