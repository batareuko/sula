/* Aion 2 · Дорожня карта 45 рівня — логіка сторінки.
   Дані: data.js · Discord-вхід: cloud.js · Збереження: localStorage + (опційно) Supabase. */
(function () {
  'use strict';

  var G = window.GUIDE;
  var Cloud = window.Cloud;
  var STORE = 'aion2-guide-v1';
  var RING_LEN = 119.4;

  function $(id) { return document.getElementById(id); }

  /* ---------- DOM helper ---------- */
  function h(tag, attrs) {
    var el = document.createElement(tag);
    Object.keys(attrs || {}).forEach(function (k) {
      var v = attrs[k];
      if (v == null || v === false) return;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k.slice(0, 2) === 'on') el.addEventListener(k.slice(2), v);
      else el.setAttribute(k, v === true ? '' : v);
    });
    for (var i = 2; i < arguments.length; i++) {
      var c = arguments[i];
      if (c == null || c === false) continue;
      el.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    }
    return el;
  }

  function clamp(n, lo, hi) { return Math.min(hi, Math.max(lo, n)); }
  function toInt(v) { var n = parseInt(v, 10); return isNaN(n) ? 0 : n; }

  /* ---------- Ідентифікатори чекбоксів ---------- */
  var START_IDS = G.start.items.map(function (i) { return i.id; });
  G.stages.forEach(function (s) {
    s.ids = s.items.map(function (_, i) { return s.id + '-' + (i + 1); });
  });
  var ROAD_IDS = START_IDS.concat.apply(START_IDS, G.stages.map(function (s) { return s.ids; }));
  var MAP_IDS = [];
  G.map.zones.forEach(function (z) {
    G.map.categories.forEach(function (c) { MAP_IDS.push(mapId(z.slug, c.id)); });
  });
  var ALL_IDS = ROAD_IDS.concat(MAP_IDS); /* порядок важливий: дорожня карта — перші ROAD_IDS.length біт */

  function mapId(zone, cat) { return 'map-' + zone + '-' + cat; }

  /* ---------- Стан ---------- */
  var state = loadState() || { v: 1, active: null, profiles: {} };
  var sortKey = 'pct';
  var cloudRows = [];
  var cloudTimer = null;

  function loadState() {
    try {
      var s = JSON.parse(localStorage.getItem(STORE));
      if (s && s.profiles && typeof s.profiles === 'object') return s;
    } catch (e) { /* приватний режим тощо */ }
    return null;
  }

  function saveState() {
    try { localStorage.setItem(STORE, JSON.stringify(state)); } catch (e) { /* ігноруємо */ }
  }

  function newId() { return 'p' + Date.now().toString(36) + Math.random().toString(36).slice(2, 6); }

  function makeProfile(name, extra) {
    var p = { id: newId(), name: name, cp: 0, checks: {}, counters: {}, updated: Date.now() };
    return Object.assign(p, extra || {});
  }

  function addProfile(p) {
    state.profiles[p.id] = p;
    return p;
  }

  function ensureProfile() {
    if (!state.profiles[state.active]) {
      var ids = Object.keys(state.profiles);
      if (!ids.length) ids = [addProfile(makeProfile('Мій профіль')).id];
      state.active = ids[0];
    }
    saveState();
  }

  function active() { return state.profiles[state.active]; }

  function localProfiles() {
    return Object.keys(state.profiles).map(function (k) { return state.profiles[k]; })
      .filter(function (p) { return !p.cloud; });
  }

  function touch(p) {
    p.updated = Date.now();
    saveState();
    syncUI();
    if (p.cloud) cloudPushSoon();
  }

  /* ---------- Прогрес ---------- */
  function countDone(checks, ids) {
    return ids.reduce(function (n, id) { return n + (checks[id] ? 1 : 0); }, 0);
  }
  function roadPct(checks) { return Math.round(100 * countDone(checks, ROAD_IDS) / ROAD_IDS.length); }

  function toBits(checks) { return ALL_IDS.map(function (id) { return checks[id] ? '1' : '0'; }).join(''); }
  function fromBits(bits) {
    var out = {};
    String(bits || '').split('').forEach(function (c, i) { if (c === '1' && ALL_IDS[i]) out[ALL_IDS[i]] = true; });
    return out;
  }
  function bitsRoadPct(bits) {
    var s = String(bits || '').slice(0, ROAD_IDS.length);
    return Math.round(100 * (s.split('1').length - 1) / ROAD_IDS.length);
  }

  /* індекс етапу за БМ: -1 = «Старт» (<1000), null = БМ не вказано */
  function stageIndexByCp(cp) {
    if (!(cp > 0)) return null;
    var idx = -1;
    G.stages.forEach(function (s, i) { if (cp >= s.from) idx = i; });
    return idx;
  }
  function stageLabel(cp) {
    var i = stageIndexByCp(cp);
    if (i === null) return '—';
    return i < 0 ? 'Старт' : 'Етап ' + G.stages[i].num;
  }
  function currentStageIndex(p) {
    var byCp = stageIndexByCp(p.cp);
    if (byCp !== null) return byCp;
    for (var i = 0; i < G.stages.length; i++) {
      if (countDone(p.checks, G.stages[i].ids) < G.stages[i].ids.length) return i;
    }
    return G.stages.length - 1;
  }

  /* ---------- Щоденні / щотижневі лічильники ---------- */
  function pad2(n) { return n < 10 ? '0' + n : '' + n; }
  var DAY_MS = 86400000;
  var STALE_DAYS = 14; /* скільки днів без оновлень вважати «давно» */

  /* Початок поточного періоду, виражений як UTC-північ «ігрової» доби (доба починається о G.reset.utcHour UTC) */
  function periodStart(period, ms) {
    var s = new Date(ms - G.reset.utcHour * 3600000);
    var d = Date.UTC(s.getUTCFullYear(), s.getUTCMonth(), s.getUTCDate());
    if (period === 'week') d -= ((s.getUTCDay() - G.reset.weeklyDay + 7) % 7) * DAY_MS;
    return d;
  }
  function periodKey(period) {
    var d = new Date(periodStart(period, Date.now()));
    return period + ':' + d.getUTCFullYear() + '-' + pad2(d.getUTCMonth() + 1) + '-' + pad2(d.getUTCDate());
  }
  /* Момент наступного ресету (мс) */
  function nextReset(period, ms) {
    return periodStart(period, ms) + (period === 'week' ? 7 : 1) * DAY_MS + G.reset.utcHour * 3600000;
  }
  function timeLeft(ms) {
    if (ms < 60000) return '<1 хв';
    var m = Math.ceil(ms / 60000);
    var d = Math.floor(m / 1440), hh = Math.floor((m % 1440) / 60), mm = m % 60;
    if (d) return d + ' дн ' + hh + ' год';
    return hh ? hh + ' год' + (mm ? ' ' + mm + ' хв' : '') : mm + ' хв';
  }
  function renderResetInfo() {
    var now = Date.now();
    var day = nextReset('day', now), week = nextReset('week', now);
    var hhmm = function (t) { return new Date(t).toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' }); };
    var wd = new Date(week).toLocaleDateString('uk-UA', { weekday: 'long' });
    $('resetInfo').textContent = 'Лічильники скидаються за часом гри (Global): щодня о 07:00 UTC, щотижня в середу. ' +
      'Щоденний ресет о ' + hhmm(day) + ' за вашим часом, через ' + timeLeft(day - now) + '. ' +
      'Щотижневий: ' + wd + ' о ' + hhmm(week) + ', через ' + timeLeft(week - now) + '.';
  }
  function counterValue(p, item) {
    var c = p.counters[item.id];
    return c && c.p === periodKey(item.period) ? c.n : 0;
  }
  function setCounter(p, item, n) {
    p.counters[item.id] = { n: clamp(n, 0, item.max), p: periodKey(item.period) };
    touch(p);
  }

  /* ---------- Поширення профілю (код / посилання) ---------- */
  function b64encode(str) { return btoa(unescape(encodeURIComponent(str))).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, ''); }
  function b64decode(str) {
    var s = str.replace(/-/g, '+').replace(/_/g, '/');
    while (s.length % 4) s += '=';
    return decodeURIComponent(escape(atob(s)));
  }
  function encodeProfile(p) { return b64encode(JSON.stringify({ v: 1, n: p.name, c: p.cp, b: toBits(p.checks) })); }
  function shareLink(p) { return location.href.split('#')[0] + '#share=' + encodeProfile(p); }

  function sanitizeProfile(raw) {
    var name = String(raw && raw.name != null ? raw.name : '').trim().slice(0, 40) || 'Без імені';
    var checks = {};
    if (raw && raw.checks && typeof raw.checks === 'object') {
      ALL_IDS.forEach(function (id) { if (raw.checks[id] === true) checks[id] = true; });
    }
    return { name: name, cp: clamp(toInt(raw && raw.cp), 0, 9999), checks: checks };
  }

  var REGIONS = ['nae', 'naw', 'eu', 'asia', 'latam'];

  /* Прив'язаний персонаж (з Edge Function або з БД). Формат: n, s(сервер), cls, rg, cid, sid, lvl, il, cp, t */
  function sanitizeCharacter(c) {
    if (!c || typeof c !== 'object') return null;
    var str = function (v, n) { return String(v == null ? '' : v).slice(0, n); };
    var o = {
      n: str(c.n, 24), s: str(c.s, 32), cls: str(c.cls, 32), rg: str(c.rg, 8), cid: str(c.cid, 200),
      sid: toInt(c.sid), lvl: clamp(toInt(c.lvl), 0, 99), il: clamp(toInt(c.il), 0, 99999),
      cp: clamp(toInt(c.cp), 0, 99999999), t: toInt(c.t), r: str(c.r, 20),
      img: /^https:\/\/profileimg\.plaync\.com\/[A-Za-z0-9_\/?=&.%-]{1,200}$/.test(String(c.img || '')) ? String(c.img) : '',
    };
    if (!o.n || REGIONS.indexOf(o.rg) < 0 || !/^[A-Za-z0-9_.%-]{1,200}$/.test(o.cid) || o.sid <= 0) return null;
    return o;
  }

  /* Повний знімок з гри (з функції або з бази іншого учасника) — перевіряємо типи й довжини */
  var ICON_RE = /^[A-Za-z0-9_.-]{1,80}\.png$/;
  function sanitizeGame(g) {
    if (!g || typeof g !== 'object' || g.v !== 1) return null;
    var S = function (v, n) { return String(v == null ? '' : v).slice(0, n); };
    var N = function (v) { var n = Number(v); return isFinite(n) ? Math.round(n) : 0; };
    var I = function (v) { return ICON_RE.test(String(v || '')) ? String(v) : ''; };
    var A = function (v, max, f) { return Array.isArray(v) ? v.slice(0, max).filter(Array.isArray).map(f) : []; };
    return {
      v: 1,
      title: Array.isArray(g.title) ? [S(g.title[0], 40), S(g.title[1], 16)] : ['', ''],
      gender: S(g.gender, 10),
      stats: A(g.stats, 40, function (x) { return [S(x[0], 16), S(x[1], 40), N(x[2])]; }),
      boards: A(g.boards, 12, function (x) { return [S(x[0], 24), N(x[1]), N(x[2])]; }),
      titles: { owned: N(g.titles && g.titles.owned), cats: A(g.titles && g.titles.cats, 8, function (x) { return [S(x[0], 16), S(x[1], 40), S(x[2], 16), N(x[3]), N(x[4])]; }) },
      eq: g.eq == null ? null : A(g.eq, 30, function (x) { return [S(x[0], 16), S(x[1], 60), S(x[2], 16), N(x[3]), N(x[4]), I(x[5])]; }),
      skins: A(g.skins, 20, function (x) { return [S(x[0], 16), S(x[1], 60), S(x[2], 16), I(x[3])]; }),
      pet: Array.isArray(g.pet) ? [S(g.pet[0], 40), N(g.pet[1]), I(g.pet[2])] : null,
      wing: Array.isArray(g.wing) ? [S(g.wing[0], 40), S(g.wing[1], 16), N(g.wing[2]), I(g.wing[3])] : null,
      skills: A(g.skills, 60, function (x) { return [S(x[0], 40), S(x[1], 10), N(x[2]), x[3] ? 1 : 0, I(x[4])]; }),
    };
  }

  function parseCode(input) {
    var s = String(input || '').trim();
    var m = s.match(/share=([A-Za-z0-9_-]+)/);
    if (m) s = m[1];
    try {
      var o = JSON.parse(b64decode(s));
      if (!o || o.v !== 1 || typeof o.n !== 'string' || !/^[01]{0,512}$/.test(String(o.b || ''))) return null;
      return sanitizeProfile({ name: o.n, cp: o.c, checks: fromBits(o.b) });
    } catch (e) { return null; }
  }

  /* зливає профіль у список локальних: той самий нік (без урахування регістру) — оновлює */
  function mergeImported(data) {
    var existing = localProfiles().filter(function (p) { return p.name.toLowerCase() === data.name.toLowerCase(); })[0];
    if (existing) {
      if (!window.confirm('Профіль «' + existing.name + '» уже є. Оновити його імпортованими даними?')) return null;
      existing.cp = data.cp;
      existing.checks = data.checks;
      existing.updated = Date.now();
      return existing;
    }
    return addProfile(Object.assign(makeProfile(data.name), { cp: data.cp, checks: data.checks }));
  }

  function importCode(input) {
    var data = parseCode(input);
    if (!data) { toast('Не вдалося прочитати код. Перевірте, що скопійовано повністю.'); return false; }
    var p = mergeImported(data);
    if (!p) return false;
    saveState();
    syncUI();
    toast('Імпортовано: ' + p.name);
    return true;
  }

  function handleHash() {
    var m = location.hash.match(/^#share=(.+)$/);
    if (!m) return;
    var ok = importCode(m[1]);
    history.replaceState(null, '', location.pathname + location.search);
    if (ok) $('team').scrollIntoView();
  }

  /* ---------- Toast / буфер обміну ---------- */
  var toastTimer;
  function toast(msg) {
    var t = $('toast');
    t.textContent = msg;
    t.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.classList.remove('show'); }, 3200);
  }

  function copyText(text, okMsg) {
    var done = function () { toast(okMsg); };
    if (navigator.clipboard && window.isSecureContext) {
      navigator.clipboard.writeText(text).then(done, function () { window.prompt('Скопіюйте вручну:', text); });
    } else {
      window.prompt('Скопіюйте вручну:', text);
    }
  }

  /* ---------- Терміни: «[українська|English]» у текстах data.js ---------- */
  var TERM_RE = /\[([^\]|]+)\|([^\]]+)\]/g;

  /* Текст із розміткою -> фрагмент: термін + англійська назва з гри в дужках (лише текстові вузли) */
  function rich(str) {
    var frag = document.createDocumentFragment();
    var s = String(str), last = 0, m;
    TERM_RE.lastIndex = 0;
    while ((m = TERM_RE.exec(s))) {
      if (m.index > last) frag.appendChild(document.createTextNode(s.slice(last, m.index)));
      frag.appendChild(h('span', { class: 'term' }, m[1], h('span', { class: 'en', lang: 'en' }, ' ', h('span', { text: m[2] }))));
      last = TERM_RE.lastIndex;
    }
    if (last < s.length) frag.appendChild(document.createTextNode(s.slice(last)));
    return frag;
  }
  /* Той самий текст без англійських назв (для aria-label, сповіщень) */
  function plain(str) { return String(str).replace(TERM_RE, '$1'); }

  /* ---------- Побудова статичних блоків ---------- */
  function checkbox(id, text, extraAttrs) {
    var input = h('input', Object.assign({ type: 'checkbox', 'data-id': id }, extraAttrs || {}));
    input.addEventListener('change', function () {
      var p = active();
      if (input.checked) p.checks[id] = true; else delete p.checks[id];
      touch(p);
    });
    return h('label', { class: 'check' }, input, h('span', { class: 'box' }), text ? h('span', { class: 'txt' }, rich(text)) : null);
  }

  function buildStatic() {
    G.rules.forEach(function (t) { $('rules').appendChild(h('li', null, rich(t))); });

    $('startTitle').textContent = G.start.title;
    $('startGoal').textContent = G.start.goal;
    G.start.items.forEach(function (it) { $('startList').appendChild(h('li', null, checkbox(it.id, it.text))); });

    G.stages.forEach(function (s) {
      var list = h('ul', { class: 'checklist' });
      s.items.forEach(function (t, i) { list.appendChild(h('li', null, checkbox(s.ids[i], t))); });
      var side = h('div', { class: 'stage-side' },
        h('div', { class: 'step', text: 'ЕТАП ' + s.num }),
        h('div', { class: 'range' }, h('span', { text: s.range[0] + '–' }), h('span', { text: s.range[1] })),
        h('div', { class: 'bar' }, h('i')),
        h('div', { class: 'count', 'data-count': s.id }));
      var body = h('div', { class: 'stage-body' },
        h('div', { class: 'stage-head' }, h('h3', null, rich(s.title)), h('span', { class: 'badge' + (s.badgeHot ? ' hot' : ''), text: s.badge })),
        list);
      s.el = h('article', { class: 'stage', id: 'stage-' + s.id }, side, body);
      $('stages').appendChild(s.el);
    });

    G.systems.forEach(function (sys) {
      var el = h('div', { class: 'sys' }, h('h3', null, rich(sys.title)), h('p', null, rich(sys.text)));
      if (sys.tiers) {
        var tiers = h('div', { class: 'tiers' });
        sys.tiers.forEach(function (t) { tiers.appendChild(h('span', { class: t.color, text: t.label })); });
        el.appendChild(tiers);
      }
      $('systemsList').appendChild(el);
    });

    G.energy.stats.forEach(function (s) {
      $('energyStats').appendChild(h('div', null, h('b', { class: s.hot ? 'hot' : null, text: String(s.value) }), h('small', { text: s.label })));
    });
    G.energy.bullets.forEach(function (t) { $('energyList').appendChild(h('li', null, rich(t))); });

    G.priorities.forEach(function (it) { $('prioList').appendChild(buildPriority(it)); });
    $('prioNote').appendChild(rich(G.prioritiesNote));

    G.thresholds.forEach(function (t) {
      t.el = h('li', { class: t.final ? 'final' : null }, h('b', { text: String(t.cp) }), h('small', null, rich(t.text)));
      $('timeline').appendChild(t.el);
    });

    buildMapMatrix();
  }

  function buildPriority(it) {
    var name = h('span', { class: 'nm' }, rich(it.name), h('span', { class: 'hint', text: it.hint }));
    var ctl;
    if (it.max === 1) {
      var input = h('input', { type: 'checkbox', 'data-prio': it.id });
      input.addEventListener('change', function () { setCounter(active(), it, input.checked ? 1 : 0); });
      ctl = h('label', { class: 'check' }, input, h('span', { class: 'box' }));
    } else {
      var out = h('output', { 'data-prio': it.id });
      ctl = h('div', { class: 'counter' },
        h('button', { type: 'button', 'aria-label': 'Менше: ' + plain(it.name), text: '−', onclick: function () { setCounter(active(), it, counterValue(active(), it) - 1); } }),
        out,
        h('button', { type: 'button', 'aria-label': 'Більше: ' + plain(it.name), text: '+', onclick: function () { setCounter(active(), it, counterValue(active(), it) + 1); } }));
    }
    return h('li', null, name, ctl);
  }

  function buildMapMatrix() {
    var table = $('mapMatrix');
    var head = h('tr', null, h('th', { text: 'Категорія' }));
    G.map.zones.forEach(function (z) { head.appendChild(h('th', { class: 'center', text: z.name })); });
    table.appendChild(h('thead', null, head));
    var body = h('tbody');
    G.map.categories.forEach(function (c) {
      var row = h('tr', null, h('td', null,
        h('b', { text: c.title }), ' ', h('span', { class: 'muted mono small', text: c.cat }),
        h('div', { class: 'muted small', text: c.why })));
      G.map.zones.forEach(function (z) {
        row.appendChild(h('td', { class: 'center' }, checkbox(mapId(z.slug, c.id), null, { 'aria-label': c.title + ' — ' + z.name })));
      });
      body.appendChild(row);
    });
    table.appendChild(body);
  }

  /* ---------- Синхронізація інтерфейсу зі станом ---------- */
  function syncUI() {
    var p = active();
    if (!p) return;

    document.querySelectorAll('input[data-id]').forEach(function (input) {
      var on = !!p.checks[input.getAttribute('data-id')];
      input.checked = on;
      var lab = input.closest('label');
      if (lab) lab.classList.toggle('done', on);
    });

    var cur = currentStageIndex(p);
    G.stages.forEach(function (s, i) {
      var done = countDone(p.checks, s.ids);
      s.el.querySelector('.bar i').style.width = (100 * done / s.ids.length) + '%';
      s.el.querySelector('.count').textContent = done + ' / ' + s.ids.length;
      s.el.classList.toggle('complete', done === s.ids.length);
      s.el.classList.toggle('current', i === cur && stageIndexByCp(p.cp) !== null);
    });

    G.priorities.forEach(function (it) {
      var n = counterValue(p, it);
      var node = document.querySelector('[data-prio="' + it.id + '"]');
      if (!node) return;
      if (it.max === 1) {
        node.checked = n >= 1;
        node.closest('label').classList.toggle('done', n >= 1);
      } else {
        node.textContent = n + ' / ' + it.max;
        node.classList.toggle('full', n >= it.max);
      }
    });

    var pct = roadPct(p.checks);
    $('ringFg').style.strokeDashoffset = String(RING_LEN * (1 - pct / 100));
    $('ringPct').textContent = pct + '%';
    $('topChipName').textContent = p.name;
    $('topChipPct').textContent = pct + '%';
    $('profileSummary').textContent = stageLabel(p.cp) + ' · виконано ' + countDone(p.checks, ROAD_IDS) + ' з ' + ROAD_IDS.length + ' пунктів дорожньої карти';

    var sel = $('profileSelect');
    sel.textContent = '';
    Object.keys(state.profiles).forEach(function (k) {
      var q = state.profiles[k];
      sel.appendChild(h('option', { value: q.id, text: (q.cloud ? '☁ ' : '') + q.name }));
    });
    sel.value = p.id;
    if (document.activeElement !== $('profileName')) $('profileName').value = p.name;
    $('profileName').disabled = !!p.cloud;
    if (document.activeElement !== $('profileCp')) $('profileCp').value = p.cp > 0 ? String(p.cp) : '';
    $('btnDelete').textContent = p.cloud ? 'Видалити з 1 HP' : 'Видалити профіль';

    syncThresholds(p);
    syncLookup(p);
    renderOwnCharacter(p);
    syncAuthUI();
    renderTeam();
  }

  function syncThresholds(p) {
    var next = null;
    G.thresholds.forEach(function (t) {
      var reached = p.cp > 0 && p.cp >= t.cp;
      t.el.classList.toggle('reached', reached);
      t.el.classList.remove('next');
      if (!reached && !next && p.cp > 0) next = t;
    });
    var st = $('thStatus');
    st.textContent = '';
    if (!(p.cp > 0)) {
      st.textContent = 'Вкажіть свою бойову міць у розділі «Мій прогрес» — досягнуті пороги підсвітяться.';
    } else if (next) {
      next.el.classList.add('next');
      st.append('Ваша БМ: ', h('b', { text: String(p.cp) }), '. Наступний поріг — ', h('b', { text: String(next.cp) }),
        ' (ще ' + (next.cp - p.cp) + '): ', rich(next.text), '.');
    } else {
      st.textContent = 'Усі пороги досягнуто — ви готові до рейду Лудри.';
    }
  }

  /* ---------- Склад 1 HP ---------- */
  function sortRows(rows) {
    return rows.slice().sort(function (a, b) {
      if (sortKey === 'name') return a.name.localeCompare(b.name, 'uk');
      if (sortKey === 'updated') return (a.ts || 0) - (b.ts || 0) || a.name.localeCompare(b.name, 'uk'); /* давно не оновлені — зверху */
      if (sortKey === 'cp') return b.cp - a.cp || a.name.localeCompare(b.name, 'uk');
      return b.pct - a.pct || b.cp - a.cp || a.name.localeCompare(b.name, 'uk');
    });
  }

  function sortHead(label, key) {
    return h('th', null, h('button', {
      class: 'sortbtn', type: 'button', text: label, 'aria-pressed': sortKey === key ? 'true' : 'false',
      title: 'Сортувати',
      onclick: function () { sortKey = key; renderTeam(); },
    }));
  }

  function barCell(pct) {
    return h('td', { class: 'pctcell' }, h('span', { class: 'num', text: pct + '%' }), h('div', { class: 'bar' }, h('i', { style: 'width:' + pct + '%' })));
  }

  function renderTable(table, rows, opts) {
    table.textContent = '';
    var head = h('tr', null, sortHead('Нік', 'name'), sortHead('БМ', 'cp'), h('th', { text: 'Етап' }), sortHead('Прогрес', 'pct'));
    if (opts.updated) head.appendChild(sortHead('Оновлено', 'updated'));
    head.appendChild(h('th'));
    table.appendChild(h('thead', null, head));
    var body = h('tbody');
    if (!rows.length) body.appendChild(h('tr', null, h('td', { class: 'empty', colspan: opts.updated ? 6 : 5, text: opts.empty })));
    sortRows(rows).forEach(function (r) {
      var nick = h('div', { class: 'nick' });
      if (r.avatar && /^https:\/\//.test(r.avatar)) nick.appendChild(h('img', { src: r.avatar, alt: '', width: 24, height: 24, loading: 'lazy', referrerpolicy: 'no-referrer' }));
      var nameEl = r.character && opts.onOpen
        ? h('button', { class: 'linkbtn', type: 'button', text: r.name, title: 'Відкрити картку персонажа', onclick: function () { opts.onOpen(r); } })
        : document.createTextNode(r.name);
      nick.appendChild(h('div', null, nameEl, r.character ? h('span', { class: 'sub', text: r.character.cls + ' · ' + r.character.s }) : null));
      var tr = h('tr', { class: r.isActive ? 'active' : null },
        h('td', null, nick),
        h('td', { class: 'num', text: r.cp > 0 ? String(r.cp) : '—' }),
        h('td', { text: stageLabel(r.cp) }),
        barCell(r.pct));
      if (opts.updated) {
        var age = r.ts ? Math.floor((Date.now() - r.ts) / DAY_MS) : null;
        tr.appendChild(h('td', { class: 'small ' + (age !== null && age >= STALE_DAYS ? 'stale' : 'muted'),
          text: age === null ? '' : age <= 0 ? 'сьогодні' : age === 1 ? 'вчора' : age + ' дн. тому' }));
      }
      tr.appendChild(h('td', { class: 'acts' }, opts.actions ? opts.actions(r) : null));
      body.appendChild(tr);
    });
    table.appendChild(body);
  }

  function renderTeam() {
    var local = localProfiles().map(function (p) {
      return { id: p.id, name: p.name, cp: p.cp, pct: roadPct(p.checks), isActive: p.id === state.active, profile: p, character: p.character, ts: p.updated };
    });
    renderTable($('localTable'), local, {
      empty: 'Профілів немає.',
      onOpen: function (r) { openCharacterDialog(r.profile.character, r.profile.game, r.name); },
      actions: function (r) {
        var box = document.createDocumentFragment();
        box.appendChild(h('button', { class: 'btn', type: 'button', text: 'Відкрити', onclick: function () {
          state.active = r.id; saveState(); syncUI(); $('progress').scrollIntoView();
        } }));
        box.appendChild(document.createTextNode(' '));
        box.appendChild(h('button', { class: 'btn btn-ghost', type: 'button', text: 'Посилання', onclick: function () {
          copyText(shareLink(r.profile), 'Посилання на профіль «' + r.name + '» скопійовано');
        } }));
        box.appendChild(document.createTextNode(' '));
        box.appendChild(h('button', { class: 'btn btn-ghost', type: 'button', text: '✕', 'aria-label': 'Видалити ' + r.name, onclick: function () { deleteLocal(r.profile); } }));
        return box;
      },
    });

    var loggedIn = Cloud && Cloud.user;
    $('cloudBlock').hidden = !loggedIn;
    var rows = local;
    if (loggedIn) {
      rows = cloudRows.map(function (m) {
        return { userId: m.user_id, ts: Date.parse(m.updated_at) || 0, name: m.name, avatar: m.avatar_url, cp: m.cp, pct: bitsRoadPct(m.checks), updated: m.updated_at, isActive: m.user_id === Cloud.user.id, character: sanitizeCharacter(m.character) };
      });
      renderTable($('cloudTable'), rows, {
        empty: 'Поки що нікого немає.',
        updated: true,
        onOpen: function (r) {
          openCharacterDialog(r.character, null, r.name);
          Cloud.fetchMemberGame(r.userId).then(function (row) {
            if (row) openCharacterDialog(sanitizeCharacter(row.character), sanitizeGame(row.game), row.name);
          }).catch(function () { /* показуємо те, що вже є */ });
        },
      });
    }
    var avg = rows.length ? Math.round(rows.reduce(function (s, r) { return s + r.pct; }, 0) / rows.length) : 0;
    $('teamSummary').textContent = rows.length ? rows.length + ' учасн. · середній прогрес ' + avg + '%' : '';
  }

  function deleteLocal(p) {
    if (!window.confirm('Видалити профіль «' + p.name + '»? Це не можна скасувати.')) return;
    delete state.profiles[p.id];
    ensureProfile();
    syncUI();
  }

  /* ---------- Дії панелі профілю ---------- */
  function bindProfileControls() {
    $('profileSelect').addEventListener('change', function (e) {
      if (state.profiles[e.target.value]) { state.active = e.target.value; saveState(); syncUI(); }
    });
    $('profileName').addEventListener('change', function (e) {
      var p = active();
      var v = e.target.value.trim().slice(0, 40);
      if (v && !p.cloud) { p.name = v; touch(p); } else { e.target.value = p.name; }
    });
    $('profileCp').addEventListener('input', function (e) {
      var p = active();
      p.cp = clamp(toInt(e.target.value), 0, 9999);
      touch(p);
    });
    $('profileCp').addEventListener('blur', syncUI);

    $('btnNew').addEventListener('click', function () {
      var name = window.prompt('Нік учасника:');
      name = name && name.trim().slice(0, 40);
      if (!name) return;
      state.active = addProfile(makeProfile(name)).id;
      saveState();
      syncUI();
    });
    $('btnCopyCode').addEventListener('click', function () {
      var p = active();
      copyText(shareLink(p), 'Посилання скопійовано — надішліть його лідеру 1 HP');
    });
    $('btnReset').addEventListener('click', function () {
      var p = active();
      if (!window.confirm('Скинути прогрес профілю «' + p.name + '»? Бойова міць залишиться.')) return;
      p.checks = {};
      p.counters = {};
      touch(p);
    });
    $('btnDelete').addEventListener('click', function () {
      var p = active();
      if (p.cloud) {
        if (!window.confirm('Видалити ваш запис зі складу 1 HP на сервері? Локальний прогрес збережеться.')) return;
        Cloud.deleteMine().then(function () { toast('Запис видалено зі складу 1 HP'); loadRoster(); })
          .catch(function (err) { toast('Помилка: ' + err.message); });
        return;
      }
      deleteLocal(p);
    });

    $('btnImport').addEventListener('click', function () {
      if (importCode($('importInput').value)) $('importInput').value = '';
    });
    $('btnExport').addEventListener('click', exportAll);
    $('fileInput').addEventListener('change', function (e) {
      var f = e.target.files[0];
      e.target.value = '';
      if (f) importFile(f);
    });
    window.addEventListener('hashchange', handleHash);
  }

  function exportAll() {
    var data = { v: 1, profiles: localProfiles().map(function (p) { return { name: p.name, cp: p.cp, checks: p.checks }; }) };
    var a = h('a', { href: URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })), download: 'aion2-kp-progress.json' });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(function () { URL.revokeObjectURL(a.href); }, 1000);
  }

  function importFile(file) {
    var reader = new FileReader();
    reader.onload = function () {
      try {
        var data = JSON.parse(reader.result);
        if (!data || !Array.isArray(data.profiles)) throw new Error('формат');
        var n = 0;
        data.profiles.slice(0, 200).forEach(function (raw) { if (mergeImported(sanitizeProfile(raw))) n++; });
        saveState();
        syncUI();
        toast('Імпортовано профілів: ' + n);
      } catch (e) { toast('Не вдалося прочитати файл.'); }
    };
    reader.readAsText(file);
  }

  /* ---------- Discord-вхід та синхронізація ---------- */
  function setSync(msg) { $('syncNote').textContent = msg; }

  function syncAuthUI() {
    var on = !!(Cloud && Cloud.enabled);
    var user = Cloud && Cloud.user;
    $('btnLogin').hidden = !(on && !user);
    $('btnLogout').hidden = !user;
    var who = $('whoami');
    who.hidden = !user;
    who.textContent = '';
    if (user) {
      if (user.avatar && /^https:\/\//.test(user.avatar)) who.appendChild(h('img', { src: user.avatar, alt: '', referrerpolicy: 'no-referrer' }));
      who.appendChild(document.createTextNode(user.name));
    }
    if (on && !user && !$('syncNote').textContent) {
      setSync('Увійдіть через Discord, щоб прогрес зберігався на сервері, а ви з’явились у складі 1 HP. Без входу прогрес лишається лише в цьому браузері.');
    }
  }

  var pushTimer;
  function cloudPushSoon() {
    if (!(Cloud && Cloud.user)) return;
    setSync('Зберігаю…');
    clearTimeout(pushTimer);
    pushTimer = setTimeout(cloudPush, 800);
  }

  function cloudPush() {
    var p = state.profiles.cloud;
    if (!p || !Cloud.user) return Promise.resolve();
    return Cloud.saveMine({ cp: p.cp, checks: toBits(p.checks), character: p.character || null, game: p.game || null }).then(function () {
      setSync('Синхронізовано о ' + new Date().toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' }));
      loadRoster();
    }).catch(function (err) { setSync('Помилка синхронізації: ' + err.message); });
  }

  function loadRoster() {
    if (!(Cloud && Cloud.user)) return;
    Cloud.fetchAll().then(function (rows) {
      cloudRows = rows || [];
      $('cloudStatus').textContent = 'Оновлено о ' + new Date().toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' }) + '. Нікнейми та прогрес бачать усі, хто увійшов.';
      renderTeam();
    }).catch(function (err) { $('cloudStatus').textContent = 'Не вдалося завантажити склад: ' + err.message; });
  }

  function onCloudUser(user) {
    if (!user) {
      delete state.profiles.cloud;
      cloudRows = [];
      clearInterval(cloudTimer);
      stopKillsSync();
      ensureProfile();
      setSync('');
      syncUI();
      return;
    }
    var p = state.profiles.cloud || addProfile(makeProfile(user.name, { id: 'cloud', cloud: true }));
    p.name = user.name;
    p.avatar = user.avatar;
    var prev = active();
    Cloud.fetchMine().then(function (row) {
      if (row) {
        p.cp = row.cp;
        p.checks = fromBits(row.checks);
        p.character = sanitizeCharacter(row.character);
        p.game = sanitizeGame(row.game);
      } else if (prev && !prev.cloud && countDone(prev.checks, ALL_IDS) + prev.cp > 0 &&
        window.confirm('Перенести прогрес профілю «' + prev.name + '» у ваш акаунт Discord?')) {
        p.cp = prev.cp;
        p.checks = Object.assign({}, prev.checks);
        p.character = prev.character || null;
        p.game = prev.game || null;
      }
      state.active = 'cloud';
      saveState();
      syncUI();
      return cloudPush();
    }).then(function () {
      loadRoster();
      startKillsSync();
      autoRefreshCharacter();
      clearInterval(cloudTimer);
      cloudTimer = setInterval(function () { if (!document.hidden) { loadRoster(); loadKills(); } }, 60000);
    }).catch(function (err) { setSync('Помилка: ' + err.message); });
  }

  function initCharacterDialog() {
    var dlg = $('charDialog');
    $('charDialogClose').addEventListener('click', function () { if (dlg.close) dlg.close(); else dlg.removeAttribute('open'); });
    dlg.addEventListener('click', function (e) { if (e.target === dlg && dlg.close) dlg.close(); });
  }

  function initCloud() {
    $('btnRefresh').addEventListener('click', loadRoster);
    $('btnLogin').addEventListener('click', function () {
      Cloud.signIn().catch(function (err) { toast('Не вдалося почати вхід: ' + err.message); });
    });
    $('btnLogout').addEventListener('click', function () {
      Cloud.signOut().catch(function (err) { toast('Помилка виходу: ' + err.message); });
    });
    if (!Cloud || !Cloud.enabled) return;
    Cloud.init().then(function (user) {
      syncUI();
      if (!user) autoRefreshCharacter();
      Cloud.onChange(onCloudUser);
      if (user) onCloudUser(user); else onCloudUser(null);
    }).catch(function (err) {
      setSync('Вхід через Discord недоступний: ' + err.message);
    });
  }

  /* ---------- Пошук персонажа за ніком ---------- */
  var lookupBusy = false;
  var lookupLast = 0;
  var lookupFor = null;
  var LOOKUP_ERR = {
    bad_name: 'Нік має містити від 2 до 24 літер або цифр.',
    not_found: 'Персонажа не знайдено в жодному регіоні. Перевірте нік.',
    rate_limited: 'Забагато запитів. Спробуйте за хвилину.',
    upstream: 'Сайт Aion 2 зараз не відповідає. Введіть бойову міць вручну.',
    network: 'Не вдалося звернутися до сервісу пошуку. Введіть бойову міць вручну.',
  };

  function lookupMsg(t) { $('lookupStatus').textContent = t; }

  function describeCharacter(c) {
    var when = c.t ? ' Оновлено ' + new Date(c.t).toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) + '.' : '';
    return c.n + ' · ' + c.s + ' · ' + c.cls + ', ' + c.lvl + ' рів. Рівень предметів ' + c.il + ' → це БМ за шкалою гайда. Бойова міць за API: ' + c.cp + '.' + when;
  }

  function syncLookup(p) {
    var on = !!(Cloud && Cloud.enabled && Cloud.ready);
    $('lookup').hidden = !on;
    if (!on) return;
    $('btnLookupRefresh').hidden = !p.character;
    $('btnLookupUnlink').hidden = !p.character;
    if (lookupFor !== p.id) {
      lookupFor = p.id;
      $('lookupName').value = p.character ? p.character.n : '';
      $('lookupCandidates').hidden = true;
      lookupMsg(p.character ? describeCharacter(p.character) : 'Введіть нік персонажа — бойова міць підтягнеться з сайту Aion 2.');
    }
  }

  function setLookupBusy(busy) {
    ['btnLookup', 'btnLookupRefresh', 'btnLookupUnlink'].forEach(function (id) { $(id).disabled = busy; });
  }

  function runLookup(body, onData, force) {
    if (lookupBusy || (!force && Date.now() - lookupLast < 3000)) return;
    lookupBusy = true;
    lookupLast = Date.now();
    setLookupBusy(true);
    $('lookupCandidates').hidden = true;
    lookupMsg('Шукаю…');
    Cloud.lookup(body).then(onData).catch(function (err) {
      lookupMsg(LOOKUP_ERR[err.code] || LOOKUP_ERR.network);
    }).then(function () {
      lookupBusy = false;
      setLookupBusy(false);
    });
  }

  function applyCharacter(ch, game, p) {
    var c = sanitizeCharacter({ n: ch.name, s: ch.serverName, cls: ch.className, rg: ch.region, cid: ch.characterId, sid: ch.serverId, lvl: ch.level, il: ch.itemLevel, cp: ch.combatPower, t: Date.now(), r: ch.raceName, img: ch.profileImage });
    if (!c) { lookupMsg(LOOKUP_ERR.upstream); return; }
    p = p || active();
    p.character = c;
    if (game) p.game = sanitizeGame(game);
    p.cp = clamp(c.il, 0, 9999);
    var isActive = p === active();
    if (isActive) {
      $('lookupName').value = c.n;
      $('lookupCandidates').hidden = true;
    }
    touch(p);
    if (isActive) lookupMsg(describeCharacter(c));
  }

  function showCandidates(list) {
    var ul = $('lookupCandidates');
    ul.textContent = '';
    list.forEach(function (ch) {
      ul.appendChild(h('li', null, h('button', { type: 'button', onclick: function () {
        applyCharacter(ch);
        runLookup({ characterId: ch.characterId, serverId: ch.serverId, region: ch.region }, function (data) {
          if (data.character) applyCharacter(data.character, data.game);
        }, true);
      } },
        h('b', { text: ch.name }),
        h('span', { text: ch.serverName + ' · ' + ch.className + ' · ' + ch.level + ' рів.' }),
        h('span', { text: 'Рівень предметів ' + ch.itemLevel }))));
    });
    ul.hidden = false;
    lookupMsg('Знайдено кілька персонажів із таким ніком. Оберіть свого.');
  }

  function initLookup() {
    $('btnLookup').addEventListener('click', function () {
      runLookup({ name: $('lookupName').value.trim() }, function (data) {
        if (data.character) applyCharacter(data.character, data.game);
        else if (data.matches) showCandidates(data.matches);
        else lookupMsg(LOOKUP_ERR.upstream);
      });
    });
    $('lookupName').addEventListener('keydown', function (e) { if (e.key === 'Enter') $('btnLookup').click(); });
    $('btnLookupRefresh').addEventListener('click', function () {
      var c = active().character;
      if (!c) return;
      runLookup({ characterId: c.cid, serverId: c.sid, region: c.rg }, function (data) {
        if (data.character) applyCharacter(data.character, data.game); else lookupMsg(LOOKUP_ERR.upstream);
      });
    });
    $('btnLookupUnlink').addEventListener('click', function () {
      var p = active();
      p.character = null;
      p.game = null;
      lookupFor = null;
      touch(p);
    });
  }

  /* ---------- Картка персонажа: усі дані з гри + підказки за правилами гайда ---------- */
  var ICON_BASE = 'https://assets.playnccdn.com/static-aion2-gamedata/resources/';
  var AUTO_REFRESH_MS = 6 * 3600000;
  var autoRefreshed = {};
  var SLOT_ORDER = ['MainHand', 'SubHand', 'Helmet', 'Shoulder', 'Torso', 'Pants', 'Gloves', 'Boots', 'Cape',
    'Necklace', 'Earring1', 'Earring2', 'Ring1', 'Ring2', 'Bracelet1', 'Bracelet2', 'Belt', 'Amulet', 'Rune1', 'Rune2'];
  var SLOT_UA = {
    MainHand: 'Основна зброя', SubHand: 'Друга рука', Helmet: 'Шолом', Shoulder: 'Наплічники', Torso: 'Нагрудник',
    Pants: 'Поножі', Gloves: 'Рукавиці', Boots: 'Чоботи', Cape: 'Плащ', Necklace: 'Намисто', Earring1: 'Сережка 1',
    Earring2: 'Сережка 2', Ring1: 'Каблучка 1', Ring2: 'Каблучка 2', Bracelet1: 'Браслет 1', Bracelet2: 'Браслет 2',
    Belt: 'Пояс', Amulet: 'Амулет', Rune1: 'Руна 1', Rune2: 'Руна 2',
  };
  var SPECIAL_SLOTS = ['Belt', 'Amulet', 'Rune1', 'Rune2']; /* мають власний шлях покращення */

  function hideBroken() { this.classList.add('broken'); }
  /* Іконки декоративні (назва поруч), тому alt порожній; якщо CDN гри не відповів — лишається порожній квадрат */
  function gameIcon(file) {
    return file ? h('img', { src: ICON_BASE + file, alt: '', width: 40, height: 40, loading: 'lazy', referrerpolicy: 'no-referrer', decoding: 'async', onerror: hideBroken }) : h('span', { class: 'noicon' });
  }
  function gradeClass(g) { return 'grade-' + String(g || 'none').toLowerCase().replace(/[^a-z]/g, ''); }
  function fmtNum(n) { return Number(n || 0).toLocaleString('uk-UA'); }

  /* Підказки з правил гайда за реальним спорядженням */
  function gameHints(game) {
    if (!game || !game.eq) return [];
    var by = {};
    game.eq.forEach(function (e) { by[e[0]] = e; });
    var out = [];
    var runes = ['Rune1', 'Rune2'].filter(function (s) { return by[s]; });
    out.push({ ok: runes.length === 2, text: '[Руни Зіткнення|Clash Runes]: ' + runes.length + ' з 2' +
      (runes.length ? ' (' + runes.map(function (s) { return '+' + by[s][3]; }).join(', ') + ')' : '') +
      (runes.length < 2 ? '. Гайд: екіпіруйте обидві.' : '. Покращуйте обережно: руни можуть зламатися.') });
    var empty = SLOT_ORDER.filter(function (s) { return !by[s]; });
    out.push({ ok: !empty.length, text: empty.length ? 'Порожні слоти: ' + empty.map(function (s) { return SLOT_UA[s]; }).join(', ') + '.' : 'Усі 20 слотів заповнені.' });
    [['Belt', '[Благородний Пояс|Noble Belt]'], ['Amulet', 'Амулет']].forEach(function (x) {
      var e = by[x[0]];
      if (!e) return;
      var name = x[0] === 'Amulet' ? e[1] : x[1];
      out.push({ ok: e[3] >= 10, text: name + ': +' + e[3] + ' (' + e[2] + '). ' + (e[3] >= 10 ? 'Готово до [морфу|Substance Morph] в наступну рідкість.' : 'Ціль +10, потім [морф|Substance Morph].') });
    });
    var low = game.eq.filter(function (e) { return SPECIAL_SLOTS.indexOf(e[0]) < 0 && e[3] < 5; });
    out.push({ ok: !low.length, text: low.length
      ? 'Нижче +5: ' + low.length + ' (' + low.map(function (e) { return SLOT_UA[e[0]] || e[0]; }).join(', ') + '). Правило гайда: спершу все до +5.'
      : 'Усе спорядження вже +5 або вище: можна вести ключові речі до +10.' });
    return out;
  }

  function characterCard(c, game) {
    var card = h('article', { class: 'char-card' });
    var head = h('div', { class: 'char-head' });
    head.appendChild(c.img ? h('img', { class: 'char-ava', src: c.img, alt: '', width: 72, height: 72, referrerpolicy: 'no-referrer', onerror: hideBroken }) : h('div', { class: 'char-ava' }));
    var title = game && game.title && game.title[0] ? h('span', { class: 'char-title ' + gradeClass(game.title[1]), text: game.title[0] }) : null;
    head.appendChild(h('div', { class: 'char-id' },
      h('h3', { text: c.n }),
      h('p', { text: [c.cls, c.lvl + ' рів.', c.s + ' (' + c.rg.toUpperCase() + ')', c.r, game && game.gender].filter(Boolean).join(' · ') }),
      title));
    card.appendChild(head);

    var tiles = h('div', { class: 'char-tiles' },
      h('div', null, h('b', { text: fmtNum(c.il) }), h('small', { text: 'рівень предметів · БМ гайда' })),
      h('div', null, h('b', { text: fmtNum(c.cp) }), h('small', { text: 'бойова міць (API)' })));
    if (game) tiles.appendChild(h('div', null, h('b', { text: fmtNum(game.titles.owned) }), h('small', { text: 'титулів' })));
    card.appendChild(tiles);

    if (!game) {
      card.appendChild(h('p', { class: 'muted small', text: 'Повні дані ще не підтягнуто: натисніть «Оновити» біля пошуку персонажа.' }));
      return card;
    }

    var hints = gameHints(game);
    if (hints.length) {
      var ul = h('ul', { class: 'char-hints' });
      hints.forEach(function (x) { ul.appendChild(h('li', { class: x.ok ? 'ok' : 'todo' }, rich(x.text))); });
      card.appendChild(h('section', null, h('h4', { text: 'Підказки за гайдом' }), ul));
    }

    if (game.eq) {
      var by = {};
      game.eq.forEach(function (e) { by[e[0]] = e; });
      var grid = h('div', { class: 'eq-grid' });
      SLOT_ORDER.concat(game.eq.map(function (e) { return e[0]; }).filter(function (s) { return SLOT_ORDER.indexOf(s) < 0; })).forEach(function (slot) {
        var e = by[slot];
        if (!e) { grid.appendChild(h('div', { class: 'eq empty' }, h('span', { class: 'noicon' }), h('div', null, h('small', { text: SLOT_UA[slot] || slot }), h('span', { text: 'порожньо' })))); return; }
        grid.appendChild(h('div', { class: 'eq ' + gradeClass(e[2]), title: e[1] + ' · ' + e[2] + ' · +' + e[3] + (e[4] ? ' · exceed ' + e[4] : '') },
          gameIcon(e[5]),
          h('div', null, h('small', { text: SLOT_UA[slot] || slot }), h('span', { class: 'eq-name', text: e[1] }),
            h('span', { class: 'eq-lv', text: '+' + e[3] + (e[4] ? ' · ★' + e[4] : '') + ' · ' + e[2] }))));
      });
      card.appendChild(h('section', null, h('h4', { text: 'Спорядження' }), grid));
    }

    var extra = h('div', { class: 'char-extra' });
    if (game.pet) extra.appendChild(h('div', { class: 'eq' }, gameIcon(game.pet[2]), h('div', null, h('small', { text: 'Пет' }), h('span', { class: 'eq-name', text: game.pet[0] }), h('span', { class: 'eq-lv', text: game.pet[1] + ' рів.' }))));
    if (game.wing) extra.appendChild(h('div', { class: 'eq ' + gradeClass(game.wing[1]) }, gameIcon(game.wing[3]), h('div', null, h('small', { text: 'Крила' }), h('span', { class: 'eq-name', text: game.wing[0] }), h('span', { class: 'eq-lv', text: '+' + game.wing[2] + ' · ' + game.wing[1] }))));
    if (extra.childNodes.length) card.appendChild(h('section', null, h('h4', { text: 'Пет і крила' }), extra));

    if (game.boards.length) {
      var bl = h('div', { class: 'boards' });
      game.boards.forEach(function (b) {
        var pct = b[2] ? Math.round(100 * b[1] / b[2]) : 0;
        bl.appendChild(h('div', { class: 'board' },
          h('span', { lang: 'en', text: b[0] }),
          h('span', { class: 'num', text: b[1] + ' / ' + b[2] }),
          h('div', { class: 'bar' }, h('i', { style: 'width:' + pct + '%' }))));
      });
      card.appendChild(h('section', null, h('h4', null, rich('Дошки [Даеваніона|Daevanion Boards]')), bl));
    }

    if (game.skills.length) {
      var sk = h('div', { class: 'skills' });
      game.skills.slice().sort(function (a, b) { return b[3] - a[3] || b[2] - a[2]; }).forEach(function (x) {
        sk.appendChild(h('span', { class: 'skill' + (x[3] ? ' on' : ''), title: x[1] + (x[3] ? ' · екіпіровано' : '') },
          x[4] ? h('img', { src: ICON_BASE + x[4], alt: '', width: 20, height: 20, loading: 'lazy', referrerpolicy: 'no-referrer', onerror: hideBroken }) : null,
          x[0] + ' ', h('b', { text: String(x[2]) })));
      });
      card.appendChild(h('section', null, h('h4', { text: 'Вміння (екіпіровані — першими)' }), sk));
    }

    var stats = game.stats.filter(function (x) { return x[0] !== 'ItemLevel'; });
    if (stats.length) {
      var st = h('dl', { class: 'stats-list' });
      stats.forEach(function (x) { st.appendChild(h('div', null, h('dt', { text: x[1] }), h('dd', { text: String(x[2]) }))); });
      card.appendChild(h('section', null, h('h4', { text: 'Характеристики' }), st));
    }

    if (game.titles.cats.length) {
      var tl = h('ul', { class: 'titles-list' });
      game.titles.cats.forEach(function (x) {
        tl.appendChild(h('li', null, h('span', { text: x[0] + ': ' }), x[1] ? h('b', { class: gradeClass(x[2]), text: x[1] }) : h('span', { class: 'muted', text: 'не обрано' }), h('span', { class: 'muted', text: ' · ' + x[3] + ' / ' + x[4] })));
      });
      card.appendChild(h('section', null, h('h4', { text: 'Титули' }), tl));
    }

    if (game.skins.length) {
      card.appendChild(h('p', { class: 'muted small', text: 'Скіни: ' + game.skins.map(function (x) { return x[1]; }).join(', ') + '.' }));
    }
    card.appendChild(h('p', { class: 'muted small', text: 'Дані з сайту Aion 2' + (c.t ? ', оновлено ' + new Date(c.t).toLocaleString('uk-UA', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '') + '.' }));
    return card;
  }

  function renderOwnCharacter(p) {
    var box = $('characterBox');
    box.textContent = '';
    if (p.character) { box.appendChild(characterCard(p.character, p.game)); return; }
    box.appendChild(h('div', { class: 'panel char-empty' },
      h('p', null, 'Персонажа ще не прив’язано. ', Cloud && Cloud.enabled
        ? 'Введіть нік у «Мій прогрес» і натисніть «Підтягнути БМ»: тут з’являться спорядження, дошки Даеваніона, вміння та підказки за гайдом.'
        : 'Прив’язка персонажа працює після налаштування Supabase (див. README).')));
  }

  function openCharacterDialog(c, game, name) {
    var dlg = $('charDialog'), body = $('charDialogBody');
    body.textContent = '';
    body.appendChild(c ? characterCard(c, game) : h('p', { class: 'muted', text: (name || 'Учасник') + ' ще не прив’язав персонажа.' }));
    if (!dlg.open) { if (dlg.showModal) dlg.showModal(); else dlg.setAttribute('open', ''); }
  }

  /* Тихе оновлення даних прив'язаного персонажа, якщо вони старші за 6 годин */
  function autoRefreshCharacter() {
    var p = active();
    if (!p || !p.character || autoRefreshed[p.id] || !(Cloud && Cloud.ready)) return;
    if (p.game && Date.now() - p.character.t < AUTO_REFRESH_MS) return;
    autoRefreshed[p.id] = true;
    var c = p.character;
    Cloud.lookup({ characterId: c.cid, serverId: c.sid, region: c.rg }).then(function (data) {
      if (data && data.character && state.profiles[p.id]) applyCharacter(data.character, data.game, state.profiles[p.id]);
    }).catch(function () { /* наступного разу */ });
  }

  /* ---------- Таймери: Розлом, ресети, польові боси ---------- */
  var BELL_SVG = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.9 1.9 0 0 0 3.4 0"/></svg>';
  var cloudKills = null;      /* { bossId: { t, by } } після входу; інакше відмітки в state.kills */
  var killsUnsub = null;
  var notified = {};

  function prefGet(k, d) {
    try { var v = JSON.parse(localStorage.getItem('aion2-pref-' + k)); return v == null ? d : v; } catch (e) { return d; }
  }
  function prefSet(k, v) { try { localStorage.setItem('aion2-pref-' + k, JSON.stringify(v)); } catch (e) { /* ігноруємо */ } }

  var bossZone = prefGet('bossZone', G.bossZones[0].id);
  var bossSort = prefGet('bossSort', 'time');
  var bossBells = prefGet('bossBells', []);
  var notifyLead = prefGet('notifyLead', 5);          /* за скільки хвилин попереджати */
  var notifyAtSpawn = prefGet('notifyAtSpawn', true); /* ще й у момент появи / відкриття */
  var notifyRift = prefGet('notifyRift', false);
  var notifySound = prefGet('notifySound', true);     /* власний звуковий сигнал сторінки */
  var notifyVolume = prefGet('notifyVolume', 70);     /* 0–100 */
  var audioCtx = null;

  /* Браузер дозволяє звук лише після дії користувача на сторінці: «розблоковуємо» звук першим кліком або клавішею */
  function unlockAudio() {
    try {
      if (!audioCtx) {
        var AC = window.AudioContext || window.webkitAudioContext;
        if (!AC) return;
        audioCtx = new AC();
      }
      if (audioCtx.state === 'suspended') audioCtx.resume().then(syncSoundState, syncSoundState);
    } catch (e) { /* без звуку */ }
    syncSoundState();
  }

  function syncSoundState() {
    var el = $('soundState');
    if (!el) return;
    if (!notifySound) { el.textContent = ''; return; }
    var ready = audioCtx && audioCtx.state === 'running';
    el.textContent = ready ? 'Звук готовий.' : 'Щоб звук спрацював, клацніть будь-де на сторінці один раз після відкриття.';
    el.classList.toggle('warn', !ready);
  }

  /* Короткий триразовий сигнал, згенерований у браузері (без аудіофайлів) */
  function chime() {
    if (!notifySound) return;
    unlockAudio();
    if (!audioCtx || audioCtx.state !== 'running') return;
    var t = audioCtx.currentTime, peak = Math.max(0.0002, 0.5 * clamp(toInt(notifyVolume), 0, 100) / 100);
    [[880, 0], [1175, 0.18], [1568, 0.36]].forEach(function (n) {
      var o = audioCtx.createOscillator(), g = audioCtx.createGain();
      o.type = 'sine';
      o.frequency.value = n[0];
      g.gain.setValueAtTime(0.0001, t + n[1]);
      g.gain.exponentialRampToValueAtTime(peak, t + n[1] + 0.02);
      g.gain.exponentialRampToValueAtTime(0.0001, t + n[1] + 0.5);
      o.connect(g);
      g.connect(audioCtx.destination);
      o.start(t + n[1]);
      o.stop(t + n[1] + 0.55);
    });
  }
  var LEAD_PRESETS = [1, 2, 3, 5, 10, 15, 20, 30, 60];

  function leadMs() { return clamp(toInt(notifyLead), 1, 180) * 60000; }

  function clock(t) { return new Date(t).toLocaleTimeString('uk-UA', { hour: '2-digit', minute: '2-digit' }); }
  function cycleText(min) {
    if (min < 60) return min + ' хв';
    return Math.floor(min / 60) + ' год' + (min % 60 ? ' ' + (min % 60) + ' хв' : '');
  }

  function riftInfo(now) {
    var P = G.rift.everyHours * 3600000;
    var anchor = G.rift.anchorUtcHour * 3600000;
    var last = anchor + Math.floor((now - anchor) / P) * P;
    return { last: last, next: last + P, open: now < last + G.rift.portalMin * 60000, closes: last + G.rift.portalMin * 60000, P: P };
  }

  function renderTimerCards() {
    var now = Date.now();
    var r = riftInfo(now);
    $('tcRift').classList.toggle('open', r.open);
    $('tcRiftValue').textContent = r.open ? 'відкритий ще ' + timeLeft(r.closes - now) : 'через ' + timeLeft(r.next - now);
    $('tcRiftSub').textContent = 'Далі: ' + [r.next, r.next + r.P, r.next + 2 * r.P].map(clock).join(', ');
    var day = nextReset('day', now), week = nextReset('week', now);
    $('tcDayValue').textContent = 'через ' + timeLeft(day - now);
    $('tcDaySub').textContent = 'о ' + clock(day) + ' за вашим часом';
    $('tcWeekValue').textContent = 'через ' + timeLeft(week - now);
    $('tcWeekSub').textContent = new Date(week).toLocaleDateString('uk-UA', { weekday: 'long' }) + ' о ' + clock(week);
  }

  function killsMap() {
    if (Cloud && Cloud.user && cloudKills) return cloudKills;
    return state.kills || (state.kills = {});
  }

  function bossState(b, now) {
    var k = killsMap()[b.id];
    if (!k) return { s: 'unknown', order: 3 };
    var at = k.t + b.min * 60000, end = at + G.bossWindowMin * 60000;
    if (now < at) return { s: 'dead', at: at, end: end, k: k, order: 1 };
    if (now < end) return { s: 'window', at: at, end: end, k: k, order: 0 };
    return { s: 'up', at: at, end: end, k: k, order: 2 };
  }

  function renderBosses(force) {
    var list = $('bossList');
    if (!force && list.contains(document.activeElement)) return; /* не збиваємо фокус клавіатури */
    var now = Date.now();
    var items = G.bosses.filter(function (b) { return b.zone === bossZone; }).map(function (b, i) {
      return { b: b, st: bossState(b, now), i: i };
    });
    if (bossSort === 'time') {
      items.sort(function (x, y) {
        return x.st.order - y.st.order || (x.st.at || 0) - (y.st.at || 0) || x.i - y.i;
      });
    }
    list.textContent = '';
    items.forEach(function (it) { list.appendChild(bossRow(it.b, it.st, now)); });
    $('bossTabs').querySelectorAll('.tab').forEach(function (t) { t.setAttribute('aria-selected', t.dataset.zone === bossZone ? 'true' : 'false'); });
    $('bossSortTime').setAttribute('aria-pressed', bossSort === 'time' ? 'true' : 'false');
    $('bossSortMap').setAttribute('aria-pressed', bossSort === 'map' ? 'true' : 'false');
  }

  function bossRow(b, st, now) {
    var bell = h('button', {
      class: 'bell', type: 'button', 'aria-pressed': bossBells.indexOf(b.id) >= 0 ? 'true' : 'false',
      title: 'Сповіщати про цього боса', 'aria-label': 'Сповіщати про ' + b.name,
      onclick: function () { toggleBell(b.id); },
    });
    bell.innerHTML = BELL_SVG;

    var main = h('div', { class: 'boss-main' },
      h('b', { text: b.name }),
      h('span', { class: 'meta', text: b.area + ' · рів. ' + b.lv + ' · відродження ' + cycleText(b.min) }));

    var big, small;
    if (st.s === 'unknown') { big = 'немає відмітки'; small = ''; }
    else if (st.s === 'dead') { big = 'через ' + timeLeft(st.at - now); small = 'о ' + clock(st.at); }
    else if (st.s === 'window') { big = 'може з’явитись'; small = 'вікно до ' + clock(st.end); }
    else { big = 'має бути живий'; small = 'з ' + clock(st.at); }
    if (st.k) small += (small ? ' · ' : '') + 'вбито о ' + clock(st.k.t) + (st.k.by ? ' (' + st.k.by + ')' : '');
    var status = h('div', { class: 'boss-st', 'aria-live': 'off' }, h('b', { text: big }), small ? h('span', { text: small }) : null);

    var act = h('div', { class: 'boss-act' },
      h('button', { class: 'btn', type: 'button', text: 'Вбито', 'aria-label': 'Вбито: ' + b.name, onclick: function () { setKill(b, Date.now()); } }),
      h('button', { class: 'btn btn-ghost', type: 'button', text: 'Раніше…', 'aria-label': 'Вказати час вбивства: ' + b.name, onclick: function () { askKillTime(b); } }),
      st.k ? h('button', { class: 'btn btn-ghost', type: 'button', text: '✕', title: 'Скасувати відмітку', 'aria-label': 'Скасувати відмітку: ' + b.name, onclick: function () { clearKill(b); } }) : null);

    return h('li', { class: 'boss is-' + st.s }, bell, main, status, act);
  }

  function askKillTime(b) {
    var v = window.prompt('Скільки хвилин тому вбили «' + b.name + '»?', '5');
    if (v == null) return;
    var min = parseInt(v, 10);
    if (isNaN(min) || min < 0 || min > 1440) { toast('Вкажіть число хвилин від 0 до 1440.'); return; }
    setKill(b, Date.now() - min * 60000);
  }

  function setKill(b, t) {
    if (Cloud && Cloud.user && cloudKills) {
      cloudKills[b.id] = { t: t, by: Cloud.user.name };
      renderBosses(true);
      Cloud.markKill(b.id, t).catch(function (err) { toast('Не вдалося зберегти відмітку: ' + err.message); loadKills(); });
    } else {
      killsMap()[b.id] = { t: t };
      saveState();
      renderBosses(true);
    }
  }

  function clearKill(b) {
    if (Cloud && Cloud.user && cloudKills) {
      delete cloudKills[b.id];
      renderBosses(true);
      Cloud.clearKill(b.id).catch(function (err) { toast('Не вдалося скасувати: ' + err.message); loadKills(); });
    } else {
      delete killsMap()[b.id];
      saveState();
      renderBosses(true);
    }
  }

  function loadKills() {
    if (!(Cloud && Cloud.user)) return;
    Cloud.fetchKills().then(function (rows) {
      var m = {};
      (rows || []).forEach(function (r) {
        var t = Date.parse(r.killed_at);
        if (t) m[r.boss_id] = { t: t, by: r.by_name || '' };
      });
      cloudKills = m;
      $('bossSync').textContent = 'Спільні відмітки 1 HP: їх бачать і змінюють усі, хто увійшов через Discord. Оновлюються автоматично.';
      renderBosses(true);
    }).catch(function (err) {
      $('bossSync').textContent = 'Не вдалося завантажити спільні відмітки (' + err.message + '). Чи виконано оновлений schema.sql?';
    });
  }

  function startKillsSync() {
    loadKills();
    if (killsUnsub) killsUnsub();
    try { killsUnsub = Cloud.onKillsChange(loadKills); } catch (e) { killsUnsub = null; }
  }

  function stopKillsSync() {
    if (killsUnsub) killsUnsub();
    killsUnsub = null;
    cloudKills = null;
    syncBossSyncNote();
    renderBosses(true);
  }

  function syncBossSyncNote() {
    if (Cloud && Cloud.user) return;
    $('bossSync').textContent = Cloud && Cloud.enabled
      ? 'Зараз відмітки зберігаються лише в цьому браузері. Увійдіть через Discord, щоб бачити спільні відмітки всього складу 1 HP.'
      : 'Відмітки зберігаються в цьому браузері.';
  }

  function toggleBell(id) {
    var i = bossBells.indexOf(id);
    if (i >= 0) bossBells.splice(i, 1); else bossBells.push(id);
    prefSet('bossBells', bossBells);
    if (i < 0 && notifyPermission() !== 'granted') toast('Щоб отримувати сповіщення, натисніть «Увімкнути сповіщення».');
    renderBosses(true);
  }

  function notifyPermission() { return 'Notification' in window ? Notification.permission : 'unsupported'; }

  function syncNotifyButton() {
    var p = notifyPermission(), btn = $('bossNotify');
    btn.textContent = p === 'granted' ? 'Сповіщення увімкнено' : p === 'denied' ? 'Сповіщення заблоковані' : p === 'unsupported' ? 'Сповіщення недоступні' : 'Увімкнути сповіщення';
    btn.disabled = p !== 'default';
  }

  function notify(title, body) {
    toast(title + ': ' + body);
    chime();
    if (notifyPermission() !== 'granted') return;
    try { new Notification(title, { body: body, icon: 'favicon.svg', tag: title }); } catch (e) { /* деякі браузери вимагають service worker */ }
  }

  function checkNotify() {
    var now = Date.now(), lead = leadMs();
    G.bosses.forEach(function (b) {
      if (bossBells.indexOf(b.id) < 0) return;
      var st = bossState(b, now);
      if (!st.k) return;
      var key = b.id + ':' + st.k.t;
      if (st.s === 'dead' && st.at - now <= lead && !notified[key + ':soon']) {
        notified[key + ':soon'] = true;
        notify(b.name, 'відродиться через ' + timeLeft(st.at - now) + ' (' + b.area + ')');
      } else if (st.s === 'window' && notifyAtSpawn && !notified[key + ':up']) {
        notified[key + ':up'] = true;
        notify(b.name, 'може з’явитись зараз (' + b.area + ')');
      }
    });
    if (notifyRift) {
      var r = riftInfo(now);
      if (r.open && notifyAtSpawn && !notified['rift:' + r.last + ':open']) {
        notified['rift:' + r.last + ':open'] = true;
        notify('Розлом відкрито', 'портал відкритий ще ' + timeLeft(r.closes - now));
      } else if (!r.open && r.next - now <= lead && !notified['rift:' + r.next + ':soon']) {
        notified['rift:' + r.next + ':soon'] = true;
        notify('Розлом', 'відкриється через ' + timeLeft(r.next - now) + ', о ' + clock(r.next));
      }
    }
  }

  function initNotifySettings() {
    var sel = $('notifyLead'), custom = $('notifyCustom');
    LEAD_PRESETS.forEach(function (m) { sel.appendChild(h('option', { value: String(m), text: cycleText(m) })); });
    sel.appendChild(h('option', { value: 'custom', text: 'Свій час…' }));
    var isPreset = LEAD_PRESETS.indexOf(toInt(notifyLead)) >= 0;
    sel.value = isPreset ? String(toInt(notifyLead)) : 'custom';
    custom.value = String(toInt(notifyLead));
    $('notifyCustomWrap').hidden = isPreset;
    sel.addEventListener('change', function () {
      var c = sel.value === 'custom';
      $('notifyCustomWrap').hidden = !c;
      if (c) { custom.focus(); return; }
      notifyLead = toInt(sel.value);
      prefSet('notifyLead', notifyLead);
      notified = {};
    });
    custom.addEventListener('change', function () {
      notifyLead = clamp(toInt(custom.value) || 5, 1, 180);
      custom.value = String(notifyLead);
      prefSet('notifyLead', notifyLead);
      notified = {};
    });
    $('notifyAtSpawn').checked = !!notifyAtSpawn;
    $('notifyAtSpawn').addEventListener('change', function (e) { notifyAtSpawn = e.target.checked; prefSet('notifyAtSpawn', notifyAtSpawn); });
    $('notifyRift').checked = !!notifyRift;
    $('notifyRift').addEventListener('change', function (e) {
      notifyRift = e.target.checked;
      prefSet('notifyRift', notifyRift);
      if (notifyRift && notifyPermission() === 'default') askPermission();
    });
    $('notifySound').checked = !!notifySound;
    $('notifySound').addEventListener('change', function (e) {
      notifySound = e.target.checked;
      prefSet('notifySound', notifySound);
      $('notifyVolumeWrap').hidden = !notifySound;
      if (notifySound) chime();
      syncSoundState();
    });
    $('notifyVolumeWrap').hidden = !notifySound;
    $('notifyVolume').value = String(clamp(toInt(notifyVolume), 0, 100));
    $('notifyVolume').addEventListener('change', function (e) {
      notifyVolume = clamp(toInt(e.target.value), 0, 100);
      prefSet('notifyVolume', notifyVolume);
      chime();
    });
    ['pointerdown', 'keydown'].forEach(function (ev) { document.addEventListener(ev, unlockAudio, true); });
    syncSoundState();
    $('notifyTest').addEventListener('click', function () {
      if (notifyPermission() === 'default') { askPermission(function () { notify('1 HP', 'Сповіщення працюють'); }); return; }
      notify('1 HP', 'Сповіщення працюють');
      if (notifyPermission() !== 'granted') toast('Браузер не показує системні сповіщення: вони вимкнені або заблоковані для сайту.');
    });
  }

  function askPermission(then) {
    if (notifyPermission() !== 'default') return;
    Notification.requestPermission().then(function () { syncNotifyButton(); if (then) then(); });
  }

  function initTimers() {
    var tabs = $('bossTabs');
    G.bossZones.forEach(function (z) {
      tabs.appendChild(h('button', {
        class: 'tab', type: 'button', role: 'tab', 'data-zone': z.id, text: z.name + ' · ' + z.faction,
        onclick: function () { bossZone = z.id; prefSet('bossZone', z.id); renderBosses(true); },
      }));
    });
    $('bossSortTime').addEventListener('click', function () { bossSort = 'time'; prefSet('bossSort', 'time'); renderBosses(true); });
    $('bossSortMap').addEventListener('click', function () { bossSort = 'map'; prefSet('bossSort', 'map'); renderBosses(true); });
    $('bossNotify').addEventListener('click', function () {
      askPermission(function () {
        if (notifyPermission() === 'granted' && !bossBells.length && !notifyRift) toast('Тепер увімкніть дзвіночок біля потрібних босів або сповіщення про Розлом.');
      });
    });
    initNotifySettings();
    syncNotifyButton();
    syncBossSyncNote();
    renderTimerCards();
    renderBosses(true);
    setInterval(function () { renderTimerCards(); renderBosses(false); checkNotify(); }, 15000);
  }

  /* ---------- Глосарій і перемикач англійських назв ---------- */
  function setShowEn(on) {
    document.body.classList.toggle('hide-en', !on);
    $('btnEn').setAttribute('aria-pressed', on ? 'true' : 'false');
    $('btnEn').textContent = on ? 'Англійські назви: показано' : 'Англійські назви: сховано';
  }

  function initGlossary() {
    var box = $('glossaryList');
    G.glossary.forEach(function (g) {
      var dl = h('dl', { class: 'gl-list' });
      g.items.forEach(function (it) {
        dl.appendChild(h('div', { class: 'gl-row', 'data-q': (it[0] + ' ' + it[1]).toLowerCase() },
          h('dt', { text: it[0] }), h('dd', { lang: 'en', text: it[1] })));
      });
      box.appendChild(h('section', { class: 'gl-group' }, h('h3', { text: g.group }), dl));
    });
    $('glossarySearch').addEventListener('input', function (e) {
      var q = e.target.value.trim().toLowerCase(), shown = 0;
      box.querySelectorAll('.gl-row').forEach(function (r) {
        var on = !q || r.getAttribute('data-q').indexOf(q) >= 0;
        r.hidden = !on;
        if (on) shown++;
      });
      box.querySelectorAll('.gl-group').forEach(function (sec) { sec.hidden = !sec.querySelector('.gl-row:not([hidden])'); });
      $('glossaryEmpty').hidden = shown > 0;
    });
    var showEn = prefGet('showEn', true);
    setShowEn(showEn);
    $('btnEn').addEventListener('click', function () { showEn = !showEn; prefSet('showEn', showEn); setShowEn(showEn); });
  }

  /* ---------- Інтерактивна карта ---------- */
  var mapZone = G.map.zones[0];
  var mapLoaded = false;

  function mapUrl() { return G.map.base + mapZone.slug; }

  function renderMapLinks() {
    $('mapOpen').href = mapUrl();
    $('routeGuide').href = G.map.routeGuide;
  }

  function loadMap() {
    if (mapLoaded) return;
    mapLoaded = true;
    $('mapPlaceholder').hidden = true;
    $('mapFrame').appendChild(h('iframe', {
      id: 'mapIframe', src: mapUrl(), title: 'Інтерактивна карта Aion 2: ' + mapZone.name,
      allow: 'fullscreen', sandbox: 'allow-scripts allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-forms allow-downloads',
    }));
  }

  function initMap() {
    var tabs = $('zoneTabs');
    G.map.zones.forEach(function (z) {
      var b = h('button', { class: 'tab', type: 'button', role: 'tab', 'aria-selected': z === mapZone ? 'true' : 'false', text: z.name });
      b.addEventListener('click', function () {
        mapZone = z;
        tabs.querySelectorAll('.tab').forEach(function (t) { t.setAttribute('aria-selected', t === b ? 'true' : 'false'); });
        renderMapLinks();
        var frame = $('mapIframe');
        if (frame) { frame.src = mapUrl(); frame.title = 'Інтерактивна карта Aion 2: ' + z.name; } else { loadMap(); }
      });
      tabs.appendChild(b);
    });
    renderMapLinks();
    $('mapLoad').addEventListener('click', loadMap);
    $('mapFull').addEventListener('click', function () {
      var f = $('mapFrame');
      if (f.requestFullscreen) f.requestFullscreen().catch(function () { toast('Повноекранний режим недоступний'); });
      else toast('Повноекранний режим недоступний у цьому браузері');
    });
    if ('IntersectionObserver' in window) {
      var io = new IntersectionObserver(function (entries) {
        if (entries.some(function (e) { return e.isIntersecting; })) { loadMap(); io.disconnect(); }
      }, { rootMargin: '400px' });
      io.observe($('mapFrame'));
    }
  }

  /* ---------- Старт ---------- */
  buildStatic();
  renderResetInfo();
  setInterval(function () { renderResetInfo(); syncUI(); }, 60000);
  ensureProfile();
  bindProfileControls();
  initLookup();
  initCharacterDialog();
  initTimers();
  initGlossary();
  initMap();
  syncUI();
  handleHash();
  initCloud();
})();
