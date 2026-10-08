-- Схема для входу через Discord + спільного складу 1 HP (Supabase → SQL Editor → Run).
-- Кожен учасник бачить усіх, але змінює лише власний рядок.

create table if not exists public.members (
  user_id    uuid primary key references auth.users (id) on delete cascade,
  name       text not null check (char_length(name) between 1 and 40),
  avatar_url text,
  cp         integer not null default 0 check (cp between 0 and 9999),
  checks     text not null default '' check (checks ~ '^[01]*$' and char_length(checks) <= 512),
  updated_at timestamptz not null default now()
);

-- прив'язаний персонаж Aion 2 (нік, сервер, клас); додається й до вже створеної таблиці
alter table public.members
  add column if not exists character jsonb
  check (character is null or pg_column_size(character) < 1000);

-- Повний знімок персонажа з гри: стати, дошки Даеваніона, спорядження, вміння тощо (кілька КБ)
alter table public.members
  add column if not exists game jsonb
  check (game is null or pg_column_size(game) < 32768);

-- Тижневі входи, щоденні лічильники й збір у регіоні (для складу 1 HP), невеликий об'єкт
alter table public.members
  add column if not exists extra jsonb
  check (extra is null or pg_column_size(extra) < 4096);

alter table public.members enable row level security;

drop policy if exists "members readable by signed-in users" on public.members;
create policy "members readable by signed-in users"
  on public.members for select to authenticated using (true);

drop policy if exists "members insert own row" on public.members;
create policy "members insert own row"
  on public.members for insert to authenticated with check (auth.uid() = user_id);

drop policy if exists "members update own row" on public.members;
create policy "members update own row"
  on public.members for update to authenticated
  using (auth.uid() = user_id) with check (auth.uid() = user_id);

drop policy if exists "members delete own row" on public.members;
create policy "members delete own row"
  on public.members for delete to authenticated using (auth.uid() = user_id);

-- Ролі лідера немає. Якщо ви раніше виконували версію схеми з таблицею leaders, ці рядки її приберуть.
drop policy if exists "leaders delete any member" on public.members;
drop table if exists public.leaders;

-- Спільні таймери польових босів: одна остання відмітка вбивства на кожного боса.
-- Будь-хто, хто увійшов, може поставити або скасувати відмітку (це спільний трекер складу).
create table if not exists public.boss_kills (
  boss_id   text primary key check (boss_id ~ '^[0-9]{1,12}$'),
  killed_at timestamptz not null,
  by_name   text check (char_length(by_name) <= 40),
  by_user   uuid default auth.uid() references auth.users (id) on delete set null
);

alter table public.boss_kills enable row level security;

drop policy if exists "boss kills readable" on public.boss_kills;
create policy "boss kills readable"
  on public.boss_kills for select to authenticated using (true);

drop policy if exists "boss kills insert" on public.boss_kills;
create policy "boss kills insert"
  on public.boss_kills for insert to authenticated with check (by_user = auth.uid());

drop policy if exists "boss kills update" on public.boss_kills;
create policy "boss kills update"
  on public.boss_kills for update to authenticated using (true) with check (by_user = auth.uid());

drop policy if exists "boss kills delete" on public.boss_kills;
create policy "boss kills delete"
  on public.boss_kills for delete to authenticated using (true);

-- Оновлення в реальному часі (повторний запуск не дає помилки)
do $$ begin
  alter publication supabase_realtime add table public.boss_kills;
exception when duplicate_object then null;
end $$;

-- Історія рівня предметів для рейтингу 1 HP: один запис на учасника на день
create table if not exists public.il_history (
  user_id uuid not null references auth.users (id) on delete cascade,
  day     date not null,
  il      integer not null check (il between 0 and 99999),
  primary key (user_id, day)
);

alter table public.il_history enable row level security;

drop policy if exists "il history readable" on public.il_history;
create policy "il history readable"
  on public.il_history for select to authenticated using (true);

drop policy if exists "il history insert own" on public.il_history;
create policy "il history insert own"
  on public.il_history for insert to authenticated with check (auth.uid() = user_id);

drop policy if exists "il history update own" on public.il_history;
create policy "il history update own"
  on public.il_history for update to authenticated using (auth.uid() = user_id) with check (auth.uid() = user_id);

-- Збір групи: оголошення та учасники
create table if not exists public.groups (
  id           uuid primary key default gen_random_uuid(),
  activity     text not null check (char_length(activity) between 1 and 40),
  starts_at    timestamptz not null,
  size         integer not null default 5 check (size between 2 and 20),
  note         text check (char_length(note) <= 160),
  created_by   uuid not null default auth.uid() references auth.users (id) on delete cascade,
  created_name text check (char_length(created_name) <= 40),
  created_at   timestamptz not null default now()
);

create table if not exists public.group_members (
  group_id  uuid not null references public.groups (id) on delete cascade,
  user_id   uuid not null default auth.uid() references auth.users (id) on delete cascade,
  name      text check (char_length(name) <= 40),
  role      text not null check (role in ('tank', 'heal', 'dd', 'support')),
  joined_at timestamptz not null default now(),
  primary key (group_id, user_id)
);

alter table public.groups enable row level security;
alter table public.group_members enable row level security;

drop policy if exists "groups readable" on public.groups;
create policy "groups readable" on public.groups for select to authenticated using (true);
drop policy if exists "groups insert own" on public.groups;
create policy "groups insert own" on public.groups for insert to authenticated with check (created_by = auth.uid());
drop policy if exists "groups delete own" on public.groups;
create policy "groups delete own" on public.groups for delete to authenticated using (created_by = auth.uid());

drop policy if exists "group members readable" on public.group_members;
create policy "group members readable" on public.group_members for select to authenticated using (true);
drop policy if exists "group members join self" on public.group_members;
create policy "group members join self" on public.group_members for insert to authenticated with check (user_id = auth.uid());
drop policy if exists "group members leave self" on public.group_members;
create policy "group members leave self" on public.group_members for delete to authenticated using (user_id = auth.uid());

do $$ begin
  alter publication supabase_realtime add table public.groups;
exception when duplicate_object then null;
end $$;
do $$ begin
  alter publication supabase_realtime add table public.group_members;
exception when duplicate_object then null;
end $$;

-- Журнал надісланих у Discord сповіщень (щоб не дублювати). Доступ лише для функції discord-alerts (service role).
create table if not exists public.alert_log (
  key     text primary key,
  sent_at timestamptz not null default now()
);
alter table public.alert_log enable row level security;

-- ===================================================================================================
-- Рейтинг DPS 1 HP (оверлей). Кожен учасник надсилає лише СВОЇ результати боїв з босами.
-- Оверлей входить ключем, який учасник створює на сайті; у базі зберігається тільки SHA-256 ключа.
-- Записи додає лише функція dps-upload (service role); читати можуть усі, хто увійшов, видаляти — свої.
-- ===================================================================================================
create table if not exists public.overlay_keys (
  user_id    uuid primary key references auth.users(id) on delete cascade,
  key_hash   text not null unique check (key_hash ~ '^[0-9a-f]{64}$'),
  created_at timestamptz not null default now()
);
alter table public.overlay_keys enable row level security;
drop policy if exists "overlay key own" on public.overlay_keys;
create policy "overlay key own" on public.overlay_keys for all to authenticated
  using (user_id = auth.uid()) with check (user_id = auth.uid());

create table if not exists public.dps_records (
  id          bigint generated always as identity primary key,
  user_id     uuid not null references auth.users(id) on delete cascade,
  character   text not null check (char_length(character) between 1 and 40),
  class       text not null default '' check (char_length(class) <= 20),
  server_id   int not null default 0,
  boss_code   int not null check (boss_code > 0),
  boss_name   text not null check (char_length(boss_name) between 1 and 80),
  zone        text check (char_length(zone) <= 80),
  dps         double precision not null check (dps > 0 and dps < 1e9),
  damage      bigint not null check (damage > 0),
  duration_ms int not null check (duration_ms between 5000 and 7200000),
  party_size  int not null check (party_size between 1 and 50),
  place       int not null check (place between 1 and 50),
  fought_at   timestamptz not null,
  created_at  timestamptz not null default now(),
  unique (user_id, boss_code, fought_at)
);
create index if not exists dps_records_boss on public.dps_records (boss_code, dps desc);
alter table public.dps_records enable row level security;
drop policy if exists "dps readable" on public.dps_records;
create policy "dps readable" on public.dps_records for select to authenticated using (true);
drop policy if exists "dps delete own" on public.dps_records;
create policy "dps delete own" on public.dps_records for delete to authenticated using (user_id = auth.uid());
