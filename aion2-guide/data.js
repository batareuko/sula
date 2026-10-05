/* Дані гайда «Новачок 45 рівня — дорожня карта спорядження» (Aion 2 Global).
   Тексти взято з інфографіки; тут їх можна редагувати без зміни логіки. */

window.GUIDE = {
  rules: [
    'Не витрачайте ресурси на спорядження до 45 рівня. До цього моменту все замінюється.',
    'Одразу витрачайте Кристали Даеваніона та Камені Мудрості.',
    'Покращуйте вшир, а потім углиб: спершу все до +5, потім ключові речі до +10.',
    'Енергія Оділе обмежена: не допускайте переповнення й не марнуйте її.',
  ],

  start: {
    title: 'Менше 1000 бойової міці на 45 рівні?',
    goal: 'Мета: дійти до 1000+, а далі йти за дорожньою картою.',
    items: [
      { id: 'start-1', text: 'Завершіть регіональні / побічні квести.' },
      { id: 'start-2', text: 'Пройдіть Запечатані підземелля рідного регіону.' },
      { id: 'start-3', text: 'Витратьте очки Даеваніона та Камені Мудрості.' },
      { id: 'start-4', text: 'Зачищайте Оплоти заради сувоїв Благородного Пояса.' },
      { id: 'start-5', text: 'Екіпіруйте 2 Руни Зіткнення й заповніть усі порожні слоти спорядження.' },
      { id: 'start-6', text: 'Збирайте пера Моноліту / Емпірейських слідів для Амулета Одкровення.' },
    ],
  },

  /* from — нижня межа бойової міці (БМ), з якої гравець вважається на цьому етапі */
  stages: [
    {
      id: 's1', num: '01', from: 1000, range: ['1000', '1269'], title: 'База 45 рівня',
      badge: 'Постійне підсилення', badgeHot: false,
      items: [
        'Основне спорядження 45 рівня доведе вас до діапазону 600+.',
        'Запечатані підземелля дають значний приріст очок Даеваніона.',
        'Пера / Моноліт = прогрес Амулета Одкровення.',
        'Оплоти = прогрес Благородного Пояса.',
        'Пера Безодні дають додатковий прогрес на дошці.',
        'Екіпіруйте 2 Руни Зіткнення; на початку покращуйте їх помірно.',
      ],
    },
    {
      id: 's2', num: '02', from: 1269, range: ['1269', '1420'], title: 'Цикл вхідних експедицій',
      badge: 'Ціль ≈1400', badgeHot: true,
      items: [
        'Крафтіть або замініть слабкі аксесуари.',
        'Крафтіть зброю, лише якщо вона відстає.',
        'Пройдіть Дослідження Драупніра ×3 заради гарантованої скрині з бронею.',
        'Починайте низькорівневі підземелля Підкорення, до яких маєте доступ.',
        'Продовжуйте Щоденні квести, Кошмар і Щоденне підземелля.',
      ],
    },
    {
      id: 's3', num: '03', from: 1420, range: ['1400', '1600'], title: 'Відкриття середньої гри',
      badge: 'Розблок. на 1600', badgeHot: true,
      items: [
        'Дослідження Вакрона ×3 дає ще один гарантований крок по броні.',
        'Рівномірно розподіляйте покращення між легендарними предметами.',
        'Каньйон Урюгю та Підкорення Вакрона входять у ваш цикл.',
        'Тримайте в русі дошки пояса, амулета, рун і Даеваніона.',
        'На 1600 відкривається Трансценденція, етап 1.',
      ],
    },
    {
      id: 's4', num: '04', from: 1600, range: ['1600', '2100'], title: 'Розвиток Трансценденції',
      badge: 'Ера Аркани', badgeHot: true,
      items: [
        'Фармте Трансценденцію етап 1, потім етап 2 в міру росту.',
        'Аркана стає одним із найбільших джерел бойової міці.',
        'Використовуйте покращення крафтового спорядження та проміжні віхи, де потрібно.',
        '1900 — поширена контрольна точка перед наступним великим ривком.',
        'На 2100 відкриваються Підкорення: Вогненний храм і Лігво Лютого Рогу.',
      ],
    },
    {
      id: 's5', num: '05', from: 2100, range: ['2100', '2500'], title: 'Просунутий ривок',
      badge: 'Консолідація сили', badgeHot: true,
      items: [
        'Фармте Підкорення T3 і сильніші рівні нагород.',
        'Працюйте над вищими етапами Трансценденції та сильнішими наборами Аркани.',
        'Доведіть більшість легендарного спорядження до стабільної бази покращення, перш ніж ризикувати.',
        'Покращуйте руни обережно, лише якщо маєте заміну.',
        'Закрийте слабкі слоти перед «косметичними» покращеннями.',
      ],
    },
    {
      id: 's6', num: '06', from: 2500, range: ['2500', '2800+'], title: 'Підготовка до рейду',
      badge: 'Підготовка до ендгейму', badgeHot: true,
      items: [
        'Відполіруйте постійні системи та ключові віхи покращення.',
        'Підсильте найкраще спорядження, Аркану та крафтові предмети.',
        'Використовуйте вищі рівні Випробування Вознесіння, коли це реально.',
        'Ціль — поширений поріг входу в рейд Лудри: ≈2800 бойової міці.',
      ],
    },
  ],

  systems: [
    {
      title: 'Благородний Пояс',
      text: 'Покращується сувоями з Оплотів. Шлях морфу на +10:',
      tiers: [
        { color: 'green', label: 'Базовий / Зелений' },
        { color: 'blue', label: 'Рідкісний / Синій' },
        { color: 'gold', label: 'Унікальний / Золотий' },
      ],
    },
    { title: 'Амулет Одкровення', text: 'Покращується прогресом Моноліту / пер.' },
    { title: 'Амулет Лютого Бою', text: 'Пізніший PvP-амулет із Магазину Безодні.' },
    {
      title: 'Руни Зіткнення',
      text: 'Екіпіруйте обидві. Безпечна ціль на початку — низьке покращення, бо руни можуть зламатися.',
    },
  ],

  energy: {
    stats: [
      { value: 40, label: 'за підземелля', hot: true },
      { value: 560, label: 'макс. запас' },
      { value: 840, label: 'з підпискою' },
    ],
    bullets: [
      'Підземелля безкоштовні для проходження; отримання нагород коштує Енергії Оділе.',
      'Більшість нагород ери 45 рівня зазвичай коштують 40 Оділе.',
      'Ніколи не тримайте енергію на максимумі. Витрачайте її на найкращий доступний контент.',
      'Дослідження = цільовий / гарантований прогрес.',
      'Підкорення = повторюваний цикл спорядження + кіна.',
    ],
  },

  /* period: 'day' | 'week'; max: 1 = звичайний чекбокс, >1 = лічильник */
  priorities: [
    { id: 'p-daily-quests', name: 'Щоденні квести', period: 'day', max: 1, hint: 'щодня' },
    { id: 'p-nightmare', name: 'Кошмар', period: 'day', max: 2, hint: '2 проходження / день' },
    { id: 'p-daily-dungeon', name: 'Щоденне підземелля за Камені Покращення', period: 'week', max: 7, hint: '7 / тиждень' },
    { id: 'p-ascension', name: 'Випробування Вознесіння', period: 'week', max: 3, hint: '3 / тиждень' },
  ],
  /* Ресет Global-серверів: щодня о 07:00 UTC (16:00 за часом гри, UTC+9), щотижня — у середу.
     weeklyDay: 0 = неділя … 3 = середа. */
  reset: { utcHour: 7, weeklyDay: 3 },
  prioritiesNote: 'Пороги випробувань: 1000 / 1500 / 2000 / 2500. Якщо плануєте підняти бойову міць, збережіть щотижневі входи на пізніші дні тижня.',

  thresholds: [
    { cp: 700, text: 'Підкорення: Печера Крау + Драупнір' },
    { cp: 1000, text: 'Випробування Вознесіння (Легке)' },
    { cp: 1400, text: 'Каньйон Урюгю + доступ до Вакрона' },
    { cp: 1500, text: 'Випробування Вознесіння (Нормальне)' },
    { cp: 1600, text: 'Трансценденція, етап 1' },
    { cp: 1900, text: 'Поширена контрольна точка / ривок до етапу 2' },
    { cp: 2000, text: 'Випробування Вознесіння (Складне)' },
    { cp: 2100, text: 'Вогненний храм + Лігво Лютого Рогу' },
    { cp: 2500, text: 'Випробування Вознесіння (Виклик)' },
    { cp: 2800, text: 'Рейд Лудри', final: true },
  ],

  /* Таймери. Час Global-серверів (за даними гравців; NCSOFT не публікував розклад для Global).
     Розлом (Spacetime Rift): кожні everyHours год від anchorUtcHour UTC, портал відкритий portalMin хв. */
  rift: { everyHours: 3, anchorUtcHour: 0, portalMin: 10 },

  /* Польові боси Global Season 1: відродження через `min` хвилин після вбивства (на Global цикл удвічі
     коротший, ніж в оголошенні NCSOFT для Кореї). Назви — з глобального клієнта (англ.).
     Гра повертає боса не хвилина в хвилину, тому показуємо вікно windowMin хв після кінця циклу. */
  bossWindowMin: 10,
  bossZones: [
    { id: 'verteron', name: 'Вертерон', faction: 'Елійці' },
    { id: 'altgard', name: 'Альтгард', faction: 'Асмодіани' },
  ],
  bosses: [
      { id: '2100050', zone: 'verteron', area: 'Cantas Valley', lv: 45, min: 30, name: 'Kernon of the West' },
      { id: '2100003', zone: 'verteron', area: 'Cantas Valley', lv: 45, min: 30, name: 'Neikel of the East' },
      { id: '2100040', zone: 'verteron', area: 'Elun River Swamp', lv: 45, min: 30, name: 'Rotten Kutar' },
      { id: '2100141', zone: 'verteron', area: 'Elun River Midstream', lv: 45, min: 60, name: 'Blooming Korin' },
      { id: '2100079', zone: 'verteron', area: 'Fortress Ruins', lv: 45, min: 90, name: 'Bodyguard Teegant' },
      { id: '2100076', zone: 'verteron', area: 'Fortress Ruins', lv: 45, min: 120, name: 'Kusan the Mad Gladiator' },
      { id: '2100077', zone: 'verteron', area: 'Fortress Ruins', lv: 45, min: 120, name: 'Ritualist Garshim' },
      { id: '2100178', zone: 'verteron', area: 'Tolbas Forest', lv: 45, min: 180, name: 'Bloodfang Pnyn' },
      { id: '2100177', zone: 'verteron', area: 'Tolbas Forest', lv: 45, min: 180, name: 'Furious Saursus' },
      { id: '2100988', zone: 'verteron', area: 'Aulau Village', lv: 45, min: 120, name: 'Scholar Aulla' },
      { id: '2100991', zone: 'verteron', area: 'Aulau Village', lv: 45, min: 120, name: 'Chaser Taulo' },
      { id: '2100989', zone: 'verteron', area: 'Aulau Village', lv: 45, min: 120, name: 'Forest Warrior Aullamu' },
      { id: '2100582', zone: 'verteron', area: 'Artamia Plateau', lv: 45, min: 180, name: 'Heretic Layla' },
      { id: '2100617', zone: 'verteron', area: 'Artamia Gorge', lv: 45, min: 120, name: 'Black Tentacle Lawa' },
      { id: '2100708', zone: 'verteron', area: 'Artamia Plateau', lv: 45, min: 120, name: 'Centurion Demiros' },
      { id: '2100718', zone: 'verteron', area: 'Artamia Plateau', lv: 45, min: 360, name: 'Divine Ansas' },
      { id: '2100876', zone: 'verteron', area: 'Drana Plantation', lv: 45, min: 180, name: 'Harvest Manager Moshav' },
      { id: '2100877', zone: 'verteron', area: 'Drana Plantation', lv: 45, min: 120, name: 'Sentinel K\'nash' },
      { id: '2101016', zone: 'verteron', area: 'Nahid Legion Fortress', lv: 45, min: 180, name: 'Researcher Setram' },
      { id: '2100661', zone: 'verteron', area: 'Garden of the Illusion God', lv: 45, min: 360, name: 'Phantasm Kasia' },
      { id: '2101120', zone: 'verteron', area: 'Artamia Plateau South', lv: 48, min: 180, name: 'Silent Dartan' },
      { id: '2101122', zone: 'verteron', area: 'Artamia Plateau East', lv: 48, min: 360, name: 'Soul Ruler Kashapa' },
      { id: '2101131', zone: 'verteron', area: 'Red Forest', lv: 48, min: 360, name: 'High Commander Lagta' },
      { id: '2101074', zone: 'verteron', area: 'Isle of Eternity', lv: 51, min: 360, name: 'Eternal Gartua' },
      { id: '2400017', zone: 'altgard', area: 'Dredgion Crash Site', lv: 45, min: 30, name: 'Melted Danar' },
      { id: '2400074', zone: 'altgard', area: 'Nameless Graveyard', lv: 45, min: 30, name: 'Black Warrior Aed' },
      { id: '2400140', zone: 'altgard', area: 'Sanctuary Watch Post', lv: 45, min: 30, name: 'Faithful Rajit' },
      { id: '2400141', zone: 'altgard', area: 'Sanctuary Watch Post', lv: 45, min: 60, name: 'Berserker Vargor' },
      { id: '2400223', zone: 'altgard', area: 'Moslan Forest', lv: 45, min: 90, name: 'Blood Warrior Lannar' },
      { id: '2400212', zone: 'altgard', area: 'Moslan Forest', lv: 45, min: 120, name: 'Predator Garsan' },
      { id: '2400274', zone: 'altgard', area: 'Urtumheim', lv: 45, min: 120, name: 'Deceiver Trid' },
      { id: '2400335', zone: 'altgard', area: 'Forest of Purification', lv: 45, min: 120, name: 'Blue Wave Kelpina' },
      { id: '2400358', zone: 'altgard', area: 'Dranactus', lv: 45, min: 180, name: 'Advisor Resana' },
      { id: '2400353', zone: 'altgard', area: 'Dranactus', lv: 45, min: 120, name: 'High Overseer Nutah' },
      { id: '2400419', zone: 'altgard', area: 'Basfelt Ruins', lv: 45, min: 180, name: 'Special Operations Leader Linx' },
      { id: '2400424', zone: 'altgard', area: 'Basfelt Ruins', lv: 45, min: 240, name: 'Desecrator Newbold' },
      { id: '2400425', zone: 'altgard', area: 'Basfelt Ruins', lv: 45, min: 240, name: 'Specter Archon Axios' },
      { id: '2400474', zone: 'altgard', area: 'Pafnite Burial Ground', lv: 45, min: 180, name: 'Addicted Hardirun' },
      { id: '2400504', zone: 'altgard', area: 'Gribade Canyon West', lv: 45, min: 240, name: 'Executioner Barthien' },
      { id: '2400593', zone: 'altgard', area: 'Gribade Canyon East', lv: 45, min: 360, name: 'Drakan Battalion Weapon Guruta' },
      { id: '2400607', zone: 'altgard', area: 'Black Claw Village', lv: 45, min: 180, name: 'Veteran Shujakan' },
      { id: '2400608', zone: 'altgard', area: 'Black Claw Village', lv: 45, min: 240, name: 'Visionary Karuka' },
      { id: '2400659', zone: 'altgard', area: 'Ragta Fortress', lv: 45, min: 360, name: 'Dark Shadow Vishwada' },
      { id: '2400709', zone: 'altgard', area: 'Impetusium Plaza', lv: 45, min: 360, name: 'Sharp Shylak' },
      { id: '2400855', zone: 'altgard', area: 'Forest of Purification', lv: 48, min: 360, name: 'Silent Dartan' },
      { id: '2400854', zone: 'altgard', area: 'Pafnite Burial Ground', lv: 48, min: 360, name: 'Soul Ruler Kashapa' },
      { id: '2400853', zone: 'altgard', area: 'Ragta Fortress', lv: 48, min: 720, name: 'High Commander Lagta' },
      { id: '2400800', zone: 'altgard', area: 'Isle of Immortality', lv: 51, min: 720, name: 'Immortal Gartua' },
  ],

  /* Інтерактивна карта: interactivemap.app (сторонній сервіс). Slug-и зон перевірено. */
  map: {
    base: 'https://interactivemap.app/aion2/maps/',
    routeGuide: 'https://interactivemap.app/aion2/maps/route-guide',
    zones: [
      { slug: 'verteron', name: 'Вертерон' },
      { slug: 'altgard', name: 'Альтгард' },
      { slug: 'elthen', name: 'Елтнен' },
      { slug: 'morheim', name: 'Морхайм' },
    ],
    /* Що шукати на карті — зв'язок пунктів дорожньої карти з категоріями фільтра карти */
    categories: [
      { id: 'sealed', title: 'Запечатані підземелля', cat: 'Sealed Dungeon', why: 'Етап старту / 01: очки Даеваніона.' },
      { id: 'stronghold', title: 'Оплоти', cat: 'Stronghold', why: 'Сувої для Благородного Поясу.' },
      { id: 'trace', title: 'Емпірейські сліди', cat: 'Empyrean Trace', why: 'Прогрес Амулета Одкровення.' },
      { id: 'regional', title: 'Регіональні квести', cat: 'Regional Quest', why: 'Старт: регіональні / побічні квести.' },
      { id: 'dungeon', title: 'Підземелля', cat: 'Dungeon', why: 'Цикл Підкорення, Дослідження.' },
      { id: 'bosses', title: 'Іменні боси', cat: 'Named Bosses', why: 'Нагороди для спорядження.' },
    ],
  },
};
