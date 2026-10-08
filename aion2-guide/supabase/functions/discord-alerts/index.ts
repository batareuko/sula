// Supabase Edge Function: сповіщення 1 HP у Discord — польові боси, Розлом, збір груп.
// Викликається щохвилини з pg_cron (див. cron.sql). Працює навіть коли сайт ніхто не відкрив.
//
// Секрети функції (Edge Functions → discord-alerts → Secrets):
//   DISCORD_WEBHOOK_URL   — обов'язково: вебхук каналу Discord (Налаштування каналу → Інтеграції → Вебхуки)
//   ALERT_LEAD_MIN        — за скільки хвилин попереджати (за замовчуванням 5)
//   ALERT_BOSSES          — 'priority' (за замовчуванням): лише пріоритетні боси (★, data.js → bossPriority);
//                           'all': усі з циклом від ALERT_MIN_CYCLE_MIN
//   ALERT_MIN_CYCLE_MIN   — для ALERT_BOSSES=all: лише боси з циклом від N хвилин (за замовчуванням 60, щоб не спамити)
//   ALERT_PRIORITY_LEAD_MIN — за скільки хвилин попереджати про пріоритетних (за замовчуванням 10); ще одне
//                           повідомлення — коли відкривається вікно появи
//   ALERT_PRIORITY_MENTION — кого кликати в повідомленнях про пріоритетних: '@here', '<@&ID ролі>' або порожньо (за замовчуванням)
//   ALERT_SERVER_ID       — id сервера гри 1 HP (як у профілі персонажа на сайті): таймери в кожного сервера свої;
//                           порожньо — відмітки всіх серверів
//   ALERT_RIFT            — '1' сповіщати про Розлом, '0' ні (за замовчуванням 1)
//   ALERT_GROUP_LEAD_MIN  — за скільки хвилин нагадати про збір групи (за замовчуванням 15)
//   SITE_URL              — посилання в повідомленнях (за замовчуванням https://guide.sulaslova.com/)
// SUPABASE_URL і SUPABASE_SERVICE_ROLE_KEY Supabase додає сам.

const SUPABASE_URL = Deno.env.get('SUPABASE_URL') ?? '';
const SERVICE_KEY = Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') ?? '';
const WEBHOOK = Deno.env.get('DISCORD_WEBHOOK_URL') ?? '';
const LEAD_MIN = Number(Deno.env.get('ALERT_LEAD_MIN') ?? 5);
const MIN_CYCLE = Number(Deno.env.get('ALERT_MIN_CYCLE_MIN') ?? 60);
const ALL_BOSSES = (Deno.env.get('ALERT_BOSSES') ?? 'priority') === 'all';
const PRIO_LEAD_MIN = Number(Deno.env.get('ALERT_PRIORITY_LEAD_MIN') ?? 10);
const MENTION = (Deno.env.get('ALERT_PRIORITY_MENTION') ?? '').trim();
const SERVER_ID = Number(Deno.env.get('ALERT_SERVER_ID') ?? 0) || 0;
const RIFT = (Deno.env.get('ALERT_RIFT') ?? '1') === '1';
const GROUP_LEAD_MIN = Number(Deno.env.get('ALERT_GROUP_LEAD_MIN') ?? 15);
const SITE = Deno.env.get('SITE_URL') ?? 'https://guide.sulaslova.com/';

/* Розлом: кожні 3 год від 00:00 UTC (як на сайті, data.js → rift) */
const RIFT_EVERY_MS = 3 * 3600_000;
const RIFT_ANCHOR_MS = 0;

/* Польові боси (згенеровано з data.js): id, назва, цикл відродження (хв), зона · локація */
const BOSSES: [string, string, number, string][] = [
  ["2100050", "Kernon of the West", 30, "Вертерон · Cantas Valley"],
  ["2100003", "Neikel of the East", 30, "Вертерон · Cantas Valley"],
  ["2100040", "Rotten Kutar", 30, "Вертерон · Elun River Swamp"],
  ["2100141", "Blooming Korin", 60, "Вертерон · Elun River Midstream"],
  ["2100079", "Bodyguard Teegant", 90, "Вертерон · Fortress Ruins"],
  ["2100076", "Kusan the Mad Gladiator", 120, "Вертерон · Fortress Ruins"],
  ["2100077", "Ritualist Garshim", 120, "Вертерон · Fortress Ruins"],
  ["2100178", "Bloodfang Pnyn", 180, "Вертерон · Tolbas Forest"],
  ["2100177", "Furious Saursus", 180, "Вертерон · Tolbas Forest"],
  ["2100988", "Scholar Aulla", 120, "Вертерон · Aulau Village"],
  ["2100991", "Chaser Taulo", 120, "Вертерон · Aulau Village"],
  ["2100989", "Forest Warrior Aullamu", 120, "Вертерон · Aulau Village"],
  ["2100582", "Heretic Layla", 180, "Вертерон · Artamia Plateau"],
  ["2100617", "Black Tentacle Lawa", 120, "Вертерон · Artamia Gorge"],
  ["2100708", "Centurion Demiros", 120, "Вертерон · Artamia Plateau"],
  ["2100718", "Divine Ansas", 360, "Вертерон · Artamia Plateau"],
  ["2100876", "Harvest Manager Moshav", 180, "Вертерон · Drana Plantation"],
  ["2100877", "Sentinel K'nash", 120, "Вертерон · Drana Plantation"],
  ["2101016", "Researcher Setram", 180, "Вертерон · Nahid Legion Fortress"],
  ["2100661", "Phantasm Kasia", 360, "Вертерон · Garden of the Illusion God"],
  ["2101120", "Silent Dartan", 180, "Вертерон · Artamia Plateau South"],
  ["2101122", "Soul Ruler Kashapa", 360, "Вертерон · Artamia Plateau East"],
  ["2101131", "High Commander Lagta", 360, "Вертерон · Red Forest"],
  ["2101074", "Eternal Gartua", 360, "Вертерон · Isle of Eternity"],
  ["2400017", "Melted Danar", 30, "Альтгард · Dredgion Crash Site"],
  ["2400074", "Black Warrior Aed", 30, "Альтгард · Nameless Graveyard"],
  ["2400140", "Faithful Rajit", 30, "Альтгард · Sanctuary Watch Post"],
  ["2400141", "Berserker Vargor", 60, "Альтгард · Sanctuary Watch Post"],
  ["2400223", "Blood Warrior Lannar", 90, "Альтгард · Moslan Forest"],
  ["2400212", "Predator Garsan", 120, "Альтгард · Moslan Forest"],
  ["2400274", "Deceiver Trid", 120, "Альтгард · Urtumheim"],
  ["2400335", "Blue Wave Kelpina", 120, "Альтгард · Forest of Purification"],
  ["2400358", "Advisor Resana", 180, "Альтгард · Dranactus"],
  ["2400353", "High Overseer Nutah", 120, "Альтгард · Dranactus"],
  ["2400419", "Special Operations Leader Linx", 180, "Альтгард · Basfelt Ruins"],
  ["2400424", "Desecrator Newbold", 240, "Альтгард · Basfelt Ruins"],
  ["2400425", "Specter Archon Axios", 240, "Альтгард · Basfelt Ruins"],
  ["2400474", "Addicted Hardirun", 180, "Альтгард · Pafnite Burial Ground"],
  ["2400504", "Executioner Barthien", 240, "Альтгард · Gribade Canyon West"],
  ["2400593", "Drakan Battalion Weapon Guruta", 360, "Альтгард · Gribade Canyon East"],
  ["2400607", "Veteran Shujakan", 180, "Альтгард · Black Claw Village"],
  ["2400608", "Visionary Karuka", 240, "Альтгард · Black Claw Village"],
  ["2400659", "Dark Shadow Vishwada", 360, "Альтгард · Ragta Fortress"],
  ["2400709", "Sharp Shylak", 360, "Альтгард · Impetusium Plaza"],
  ["2400855", "Silent Dartan", 360, "Альтгард · Forest of Purification"],
  ["2400854", "Soul Ruler Kashapa", 360, "Альтгард · Pafnite Burial Ground"],
  ["2400853", "High Commander Lagta", 720, "Альтгард · Ragta Fortress"],
  ["2400800", "Immortal Gartua", 720, "Альтгард · Isle of Immortality"],
];
const BOSS = new Map(BOSSES.map((b) => [b[0], b]));
/* Пріоритетні (data.js → bossPriority): 48–51 рівень, свій Unique-сет, у середньому ~1 предмет за вбивство */
const PRIORITY = new Set(['2101120', '2101122', '2101131', '2101074', '2400855', '2400854', '2400853', '2400800']);
const PRIORITY_LOOT = 'іменний Unique-сет, ~1 предмет за вбивство';
const WINDOW_MIN = 10; // data.js → bossWindowMin
const ROLE_UA: Record<string, string> = { tank: 'танк', heal: 'хіл', dd: 'ДД', support: 'підтримка' };

const sec = (ms: number) => Math.floor(ms / 1000);
/* Discord сам показує час у часовому поясі кожного читача */
const when = (ms: number) => `<t:${sec(ms)}:t> (<t:${sec(ms)}:R>)`;

async function rest(path: string, init: RequestInit = {}): Promise<any> {
  const res = await fetch(`${SUPABASE_URL}/rest/v1/${path}`, {
    ...init,
    headers: { apikey: SERVICE_KEY, Authorization: `Bearer ${SERVICE_KEY}`, 'Content-Type': 'application/json', ...(init.headers ?? {}) },
  });
  if (!res.ok) throw new Error(`rest ${res.status}: ${await res.text()}`);
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

/* Атомарно «бронюємо» сповіщення: якщо ключ уже є — його надіслав попередній запуск */
async function claim(key: string): Promise<boolean> {
  const rows = await rest('alert_log', {
    method: 'POST',
    headers: { Prefer: 'resolution=ignore-duplicates,return=representation' },
    body: JSON.stringify([{ key }]),
  });
  return Array.isArray(rows) && rows.length > 0;
}

/** ping: дозволити згадку з ALERT_PRIORITY_MENTION (@here або роль); інакше ніхто не отримує пінг */
async function post(content: string, ping = false) {
  const res = await fetch(WEBHOOK, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ content: content.slice(0, 1900), allowed_mentions: { parse: ping ? ['everyone', 'roles'] : [] } }),
  });
  if (!res.ok) throw new Error('discord ' + res.status);
}

export async function collect(now: number, kills: any[], groups: any[], opts = { all: ALL_BOSSES, mention: MENTION }) {
  const out: { key: string; text: string; ping?: boolean }[] = [];
  const lead = LEAD_MIN * 60_000;

  for (const k of kills) {
    const b = BOSS.get(String(k.boss_id));
    const killed = Date.parse(k.killed_at);
    if (!b || !killed || k.alive) continue;
    // точний час з гри (оверлей), інакше відмітка «Вбито» + цикл
    const fromGame = k.source === 'game' && Date.parse(k.respawn_at);
    const at = fromGame || killed + b[2] * 60_000;
    const by = fromGame ? ' Час з гри.' : k.by_name ? ` Вбивство відмітив ${k.by_name}.` : '';
    if (PRIORITY.has(b[0])) {
      // ★ пріоритетні: заздалегідь і ще раз, коли відкривається вікно появи
      const tag = opts.mention ? `${opts.mention} ` : '';
      if (now >= at - PRIO_LEAD_MIN * 60_000 && now < at) {
        out.push({ key: `boss:${b[0]}:${at}`, ping: !!opts.mention,
          text: `${tag}★ **${b[1]}** (${b[3]}) відродиться ${when(at)} · ${PRIORITY_LOOT}.` + by });
      } else if (now >= at && now < at + WINDOW_MIN * 60_000) {
        out.push({ key: `boss-up:${b[0]}:${at}`, ping: !!opts.mention,
          text: `${tag}★ **${b[1]}** (${b[3]}) може з'явитися зараз (вікно до ${when(at + WINDOW_MIN * 60_000)}) · ${PRIORITY_LOOT}.` });
      }
      continue;
    }
    if (!opts.all || b[2] < MIN_CYCLE) continue;
    if (now >= at - lead && now < at) {
      out.push({ key: `boss:${b[0]}:${at}`, text: `**${b[1]}** (${b[3]}) відродиться ${when(at)}.` + by });
    }
  }

  if (RIFT) {
    const next = RIFT_ANCHOR_MS + Math.ceil((now - RIFT_ANCHOR_MS) / RIFT_EVERY_MS) * RIFT_EVERY_MS;
    if (now >= next - lead && now < next) out.push({ key: `rift:${next}`, text: `**Розлом** (Spacetime Rift) відкривається ${when(next)}. Портал відкритий 10 хвилин.` });
  }

  for (const g of groups) {
    const starts = Date.parse(g.starts_at);
    const created = Date.parse(g.created_at);
    const members = (g.group_members ?? []).map((m: any) => `${m.name ?? '?'} (${ROLE_UA[m.role] ?? m.role})`);
    const line = `**${g.activity}** ${when(starts)} · ${members.length}/${g.size}` + (g.note ? ` · ${g.note}` : '') + (g.created_name ? ` · збирає ${g.created_name}` : '');
    if (created && now - created < 30 * 60_000 && starts > now) {
      out.push({ key: `group-new:${g.id}`, text: `Новий збір групи: ${line}. Записатися: ${SITE}#groups` });
    }
    if (now >= starts - GROUP_LEAD_MIN * 60_000 && now < starts) {
      out.push({ key: `group-soon:${g.id}:${starts}`, text: `Скоро старт: ${line}.` + (members.length ? ` Учасники: ${members.join(', ')}.` : '') });
    }
  }
  return out;
}

export async function handler(_req: Request): Promise<Response> {
  if (!WEBHOOK || !SUPABASE_URL || !SERVICE_KEY) return Response.json({ error: 'not_configured' }, { status: 500 });
  const now = Date.now();
  try {
    const since = new Date(now - 26 * 3600_000).toISOString();
    const [kills, groups] = await Promise.all([
      rest(`boss_kills?select=boss_id,killed_at,by_name,respawn_at,alive,source&killed_at=gte.${encodeURIComponent(since)}` +
        (SERVER_ID ? `&server_id=eq.${SERVER_ID}` : '')),
      rest(`groups?select=id,activity,starts_at,size,note,created_name,created_at,group_members(name,role)&starts_at=gte.${encodeURIComponent(new Date(now).toISOString())}&starts_at=lte.${encodeURIComponent(new Date(now + 7 * 86400_000).toISOString())}`),
    ]);
    let sent = 0;
    for (const a of await collect(now, kills ?? [], groups ?? [])) {
      if (await claim(a.key)) { await post(a.text, a.ping); sent++; }
    }
    // прибираємо старі ключі
    await rest(`alert_log?sent_at=lt.${encodeURIComponent(new Date(now - 3 * 86400_000).toISOString())}`, { method: 'DELETE' });
    return Response.json({ sent });
  } catch (e) {
    return Response.json({ error: String((e as Error).message ?? e) }, { status: 502 });
  }
}

if (typeof Deno !== 'undefined' && Deno.serve) Deno.serve(handler);
