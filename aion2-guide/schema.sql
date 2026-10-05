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
