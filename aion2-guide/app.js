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
  function periodKey(period) {
    var d = new Date();
    if (period === 'week') d.setDate(d.getDate() - ((d.getDay() + 6) % 7)); /* понеділок */
    return period + ':' + d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate());
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

  /* ---------- Побудова статичних блоків ---------- */
  function checkbox(id, text, extraAttrs) {
    var input = h('input', Object.assign({ type: 'checkbox', 'data-id': id }, extraAttrs || {}));
    input.addEventListener('change', function () {
      var p = active();
      if (input.checked) p.checks[id] = true; else delete p.checks[id];
      touch(p);
    });
    return h('label', { class: 'check' }, input, h('span', { class: 'box' }), text ? h('span', { class: 'txt', text: text }) : null);
  }

  function buildStatic() {
    G.rules.forEach(function (t) { $('rules').appendChild(h('li', { text: t })); });

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
        h('div', { class: 'stage-head' }, h('h3', { text: s.title }), h('span', { class: 'badge' + (s.badgeHot ? ' hot' : ''), text: s.badge })),
        list);
      s.el = h('article', { class: 'stage', id: 'stage-' + s.id }, side, body);
      $('stages').appendChild(s.el);
    });

    G.systems.forEach(function (sys) {
      var el = h('div', { class: 'sys' }, h('h3', { text: sys.title }), h('p', { text: sys.text }));
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
    G.energy.bullets.forEach(function (t) { $('energyList').appendChild(h('li', { text: t })); });

    G.priorities.forEach(function (it) { $('prioList').appendChild(buildPriority(it)); });
    $('prioNote').textContent = G.prioritiesNote;

    G.thresholds.forEach(function (t) {
      t.el = h('li', { class: t.final ? 'final' : null }, h('b', { text: String(t.cp) }), h('small', { text: t.text }));
      $('timeline').appendChild(t.el);
    });

    buildMapMatrix();
  }

  function buildPriority(it) {
    var name = h('span', { class: 'nm' }, it.name, h('span', { class: 'hint', text: it.hint }));
    var ctl;
    if (it.max === 1) {
      var input = h('input', { type: 'checkbox', 'data-prio': it.id });
      input.addEventListener('change', function () { setCounter(active(), it, input.checked ? 1 : 0); });
      ctl = h('label', { class: 'check' }, input, h('span', { class: 'box' }));
    } else {
      var out = h('output', { 'data-prio': it.id });
      ctl = h('div', { class: 'counter' },
        h('button', { type: 'button', 'aria-label': 'Менше: ' + it.name, text: '−', onclick: function () { setCounter(active(), it, counterValue(active(), it) - 1); } }),
        out,
        h('button', { type: 'button', 'aria-label': 'Більше: ' + it.name, text: '+', onclick: function () { setCounter(active(), it, counterValue(active(), it) + 1); } }));
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
        ' (ще ' + (next.cp - p.cp) + '): ' + next.text + '.');
    } else {
      st.textContent = 'Усі пороги досягнуто — ви готові до рейду Лудри.';
    }
  }

  /* ---------- Склад 1 HP ---------- */
  function sortRows(rows) {
    return rows.slice().sort(function (a, b) {
      if (sortKey === 'name') return a.name.localeCompare(b.name, 'uk');
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
    if (opts.updated) head.appendChild(h('th', { text: 'Оновлено' }));
    head.appendChild(h('th'));
    table.appendChild(h('thead', null, head));
    var body = h('tbody');
    if (!rows.length) body.appendChild(h('tr', null, h('td', { class: 'empty', colspan: opts.updated ? 6 : 5, text: opts.empty })));
    sortRows(rows).forEach(function (r) {
      var nick = h('div', { class: 'nick' });
      if (r.avatar && /^https:\/\//.test(r.avatar)) nick.appendChild(h('img', { src: r.avatar, alt: '', width: 24, height: 24, loading: 'lazy', referrerpolicy: 'no-referrer' }));
      nick.appendChild(document.createTextNode(r.name));
      var tr = h('tr', { class: r.isActive ? 'active' : null },
        h('td', null, nick),
        h('td', { class: 'num', text: r.cp > 0 ? String(r.cp) : '—' }),
        h('td', { text: stageLabel(r.cp) }),
        barCell(r.pct));
      if (opts.updated) tr.appendChild(h('td', { class: 'muted small', text: r.updated ? new Date(r.updated).toLocaleDateString('uk-UA') : '' }));
      tr.appendChild(h('td', { class: 'acts' }, opts.actions ? opts.actions(r) : null));
      body.appendChild(tr);
    });
    table.appendChild(body);
  }

  function renderTeam() {
    var local = localProfiles().map(function (p) {
      return { id: p.id, name: p.name, cp: p.cp, pct: roadPct(p.checks), isActive: p.id === state.active, profile: p };
    });
    renderTable($('localTable'), local, {
      empty: 'Профілів немає.',
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
        return { name: m.name, avatar: m.avatar_url, cp: m.cp, pct: bitsRoadPct(m.checks), updated: m.updated_at, isActive: m.user_id === Cloud.user.id };
      });
      renderTable($('cloudTable'), rows, { empty: 'Поки що нікого немає.', updated: true });
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
    return Cloud.saveMine({ cp: p.cp, checks: toBits(p.checks) }).then(function () {
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
      } else if (prev && !prev.cloud && countDone(prev.checks, ALL_IDS) + prev.cp > 0 &&
        window.confirm('Перенести прогрес профілю «' + prev.name + '» у ваш акаунт Discord?')) {
        p.cp = prev.cp;
        p.checks = Object.assign({}, prev.checks);
      }
      state.active = 'cloud';
      saveState();
      syncUI();
      return cloudPush();
    }).then(function () {
      loadRoster();
      clearInterval(cloudTimer);
      cloudTimer = setInterval(function () { if (!document.hidden) loadRoster(); }, 60000);
    }).catch(function (err) { setSync('Помилка: ' + err.message); });
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
      Cloud.onChange(onCloudUser);
      if (user) onCloudUser(user); else onCloudUser(null);
    }).catch(function (err) {
      setSync('Вхід через Discord недоступний: ' + err.message);
    });
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
  ensureProfile();
  bindProfileControls();
  initMap();
  syncUI();
  handleHash();
  initCloud();
})();
