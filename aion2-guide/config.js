/* Налаштування входу через Discord (Supabase). Дивіться README.md.
   Поки поля порожні — сайт працює в локальному режимі (прогрес у браузері).
   Anon-ключ Supabase публічний за задумом: доступ захищає Row Level Security (schema.sql). */
window.GUIDE_CONFIG = {
  supabaseUrl: '',      // напр. 'https://abcdefgh.supabase.co'
  supabaseAnonKey: '',  // Project Settings → API → anon public key
};
