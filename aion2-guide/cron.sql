-- Розклад для сповіщень 1 HP у Discord: щохвилини викликає функцію discord-alerts.
-- Виконайте в Supabase → SQL Editor ПІСЛЯ розгортання функції discord-alerts і додавання секрету DISCORD_WEBHOOK_URL.
-- Ключ нижче — публічний anon-ключ із config.js (той самий, що на сайті).

create extension if not exists pg_cron;
create extension if not exists pg_net;

-- повторний запуск файлу замінює розклад, а не дублює його
do $$ begin
  perform cron.unschedule('1hp-discord-alerts');
exception when others then null;
end $$;

select cron.schedule(
  '1hp-discord-alerts',
  '* * * * *',
  $$
  select net.http_post(
    url := 'https://mbwulcbfjtdmcywiakmn.supabase.co/functions/v1/discord-alerts',
    headers := jsonb_build_object(
      'Content-Type', 'application/json',
      'Authorization', 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1id3VsY2JmanRkbWN5d2lha21uIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTExOTc4NDAsImV4cCI6MjEwNjc3Mzg0MH0.yX-vh6aOZV3V1FjYfJsFlEnj1ubvLGlTlmkUzH1WSQQ'
    ),
    body := '{}'::jsonb
  );
  $$
);

-- Вимкнути сповіщення:  select cron.unschedule('1hp-discord-alerts');
