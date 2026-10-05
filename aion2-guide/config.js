/* Налаштування входу через Discord та пошуку персонажа (Supabase). Дивіться README.md.
   Якщо поля порожні, сайт працює в локальному режимі (прогрес у браузері).
   Anon-ключ Supabase публічний за задумом: доступ захищає Row Level Security (schema.sql).
   Секретні ключі (service_role) і Client Secret з Discord сюди не вписуйте. */
window.GUIDE_CONFIG = {
  supabaseUrl: 'https://mbwulcbfjtdmcywiakmn.supabase.co',
  supabaseAnonKey: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Im1id3VsY2JmanRkbWN5d2lha21uIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTExOTc4NDAsImV4cCI6MjEwNjc3Mzg0MH0.yX-vh6aOZV3V1FjYfJsFlEnj1ubvLGlTlmkUzH1WSQQ',
};
