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
