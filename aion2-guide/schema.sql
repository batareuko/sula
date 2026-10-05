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

-- Лідери: можуть видаляти будь-які записи зі складу. Додаються лише вручну через SQL Editor:
--   insert into public.leaders (user_id) values ('<User UID з Authentication → Users>');
create table if not exists public.leaders (
  user_id uuid primary key references auth.users (id) on delete cascade
);

alter table public.leaders enable row level security;

drop policy if exists "leaders read own row" on public.leaders;
create policy "leaders read own row"
  on public.leaders for select to authenticated using (auth.uid() = user_id);

drop policy if exists "leaders delete any member" on public.members;
create policy "leaders delete any member"
  on public.members for delete to authenticated
  using (exists (select 1 from public.leaders l where l.user_id = auth.uid()));
