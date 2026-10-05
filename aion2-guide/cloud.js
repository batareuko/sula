/* Вхід через Discord та спільний склад 1 HP (Supabase).
   Якщо config.js порожній — Cloud.enabled === false і решта сайту працює локально. */
(function () {
  'use strict';

  var cfg = window.GUIDE_CONFIG || {};
  var enabled = !!(cfg.supabaseUrl && cfg.supabaseAnonKey);
  var LIB = 'https://cdn.jsdelivr.net/npm/@supabase/supabase-js@2.45.4/dist/umd/supabase.min.js';
  var client = null;
  var user = null;
  var listeners = [];

  function emit() {
    listeners.forEach(function (fn) { fn(user); });
  }

  function loadLib() {
    return new Promise(function (resolve, reject) {
      if (window.supabase) return resolve();
      var s = document.createElement('script');
      s.src = LIB;
      s.onload = resolve;
      s.onerror = function () { reject(new Error('Не вдалося завантажити supabase-js')); };
      document.head.appendChild(s);
    });
  }

  function toUser(u) {
    if (!u) return null;
    var m = u.user_metadata || {};
    var claims = m.custom_claims || {};
    var name = claims.global_name || m.full_name || m.name || m.user_name || 'Discord-user';
    return { id: u.id, name: String(name).slice(0, 40), avatar: m.avatar_url || '' };
  }

  function check(res) {
    if (res.error) throw res.error;
    return res.data;
  }

  var Cloud = {
    enabled: enabled,

    get user() { return user; },

    get ready() { return !!client; },

    onChange: function (fn) { listeners.push(fn); },

    init: function () {
      if (!enabled) return Promise.resolve(null);
      return loadLib().then(function () {
        client = window.supabase.createClient(cfg.supabaseUrl, cfg.supabaseAnonKey, {
          auth: { flowType: 'pkce', persistSession: true, detectSessionInUrl: true },
        });
        client.auth.onAuthStateChange(function (_event, session) {
          var next = toUser(session && session.user);
          var changed = (next && next.id) !== (user && user.id);
          user = next;
          if (changed) emit();
        });
        return client.auth.getSession().then(function (res) {
          user = toUser(res.data && res.data.session && res.data.session.user);
          return user;
        });
      });
    },

    signIn: function () {
      return client.auth.signInWithOAuth({
        provider: 'discord',
        options: { redirectTo: location.origin + location.pathname },
      }).then(check);
    },

    signOut: function () {
      return client.auth.signOut().then(check);
    },

    fetchMine: function () {
      return client.from('members').select('*').eq('user_id', user.id).maybeSingle().then(check);
    },

    saveMine: function (row) {
      return client.from('members').upsert({
        user_id: user.id,
        name: user.name,
        avatar_url: user.avatar || null,
        cp: row.cp,
        checks: row.checks,
        character: row.character || null,
        updated_at: new Date().toISOString(),
      }).then(check);
    },

    fetchAll: function () {
      return client.from('members')
        .select('user_id,name,avatar_url,cp,checks,character,updated_at')
        .order('updated_at', { ascending: false })
        .then(check);
    },

    /* Пошук персонажа через Edge Function aion-lookup. Помилки мають поле .code:
       bad_name | not_found | rate_limited | upstream | network */
    lookup: function (body) {
      var fail = function (code) { return Object.assign(new Error(code), { code: code }); };
      return client.functions.invoke('aion-lookup', { body: body }).then(function (res) {
        if (!res.error) return res.data;
        var ctx = res.error.context;
        if (ctx && typeof ctx.json === 'function') {
          return ctx.json().then(function (j) { throw fail((j && j.error) || 'upstream'); },
            function () { throw fail('upstream'); });
        }
        throw fail('network');
      });
    },

    deleteMine: function () {
      return client.from('members').delete().eq('user_id', user.id).then(check);
    },
  };

  window.Cloud = Cloud;
})();
