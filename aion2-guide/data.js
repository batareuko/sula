/* Дані гайда «Новачок 45 рівня — дорожня карта спорядження» (Aion 2 Global).
   Тексти взято з інфографіки; тут їх можна редагувати без зміни логіки. */

window.GUIDE = {
  rules: [
    'Не витрачайте ресурси на спорядження до 45 рівня. До цього моменту все замінюється.',
    'Одразу витрачайте [Кристали Даеваніона|Daevanion Crystals] та [Камені Мудрості|Wisdom Stones].',
    'Покращуйте вшир, а потім углиб: спершу все до +5, потім ключові речі до +10.',
    '[Енергія Оділе|Odyle Energy] обмежена: не допускайте переповнення й не марнуйте її.',
  ],

  start: {
    title: 'Менше 1000 бойової міці на 45 рівні?',
    goal: 'Мета: дійти до 1000+, а далі йти за дорожньою картою.',
    items: [
      { id: 'start-1', text: 'Завершіть [регіональні|Regional Quests] / [побічні квести|Side Quests].' },
      { id: 'start-2', text: 'Пройдіть [Запечатані підземелля|Sealed Dungeons] рідного регіону.' },
      { id: 'start-3', text: 'Витратьте [очки Даеваніона|Daevanion points] та [Камені Мудрості|Wisdom Stones].' },
      { id: 'start-4', text: 'Зачищайте [Оплоти|Strongholds] заради [сувоїв Благородного Пояса|Noble Belt Enhance Scrolls].' },
      { id: 'start-5', text: 'Екіпіруйте 2 [Руни Зіткнення|Clash Runes] й заповніть усі порожні слоти спорядження.' },
      { id: 'start-6', text: 'Збирайте пера [Моноліту|Monolith] / [Емпірейських слідів|Empyrean Traces] для [Амулета Одкровення|Revelation Amulet].' },
    ],
  },

  /* from — нижня межа бойової міці (БМ), з якої гравець вважається на цьому етапі */
  stages: [
    {
      id: 's1', num: '01', from: 1000, range: ['1000', '1269'], title: 'База 45 рівня',
      badge: 'Постійне підсилення', badgeHot: false,
      items: [
        'Основне спорядження 45 рівня доведе вас до діапазону 600+.',
        '[Запечатані підземелля|Sealed Dungeons] дають значний приріст [очок Даеваніона|Daevanion points].',
        'Пера / [Моноліт|Monolith] = прогрес [Амулета Одкровення|Revelation Amulet].',
        '[Оплоти|Strongholds] = прогрес [Благородного Пояса|Noble Belt].',
        '[Пера Безодні|Abyss Empyrean Traces] дають додатковий прогрес на [дошці|Daevanion Board].',
        'Екіпіруйте 2 [Руни Зіткнення|Clash Runes]; на початку покращуйте їх помірно.',
      ],
    },
    {
      id: 's2', num: '02', from: 1269, range: ['1269', '1420'], title: 'Цикл вхідних [експедицій|Expeditions]',
      badge: 'Ціль ≈1400', badgeHot: true,
      items: [
        'Крафтіть або замініть слабкі аксесуари.',
        'Крафтіть зброю, лише якщо вона відстає.',
        'Пройдіть [Дослідження|Exploration] [Драупніра|Draupnir] ×3 заради гарантованої скрині з бронею.',
        'Починайте низькорівневі підземелля [Підкорення|Conquest], до яких маєте доступ.',
        'Продовжуйте [Щоденні квести|Daily Quests], [Кошмар|Nightmare] і [Щоденне підземелля|Daily Dungeon].',
      ],
    },
    {
      id: 's3', num: '03', from: 1420, range: ['1400', '1600'], title: 'Відкриття середньої гри',
      badge: 'Розблок. на 1600', badgeHot: true,
      items: [
        '[Дослідження|Exploration] [Вакрона|Vakron Sky Island] ×3 дає ще один гарантований крок по броні.',
        'Рівномірно розподіляйте покращення між [легендарними предметами|Legendary gear].',
        '[Каньйон Урюгю|Urugugu Canyon] та [Підкорення|Conquest] Вакрона входять у ваш цикл.',
        'Тримайте в русі дошки пояса, амулета, рун і [Даеваніона|Daevanion Boards].',
        'На 1600 відкривається [Трансценденція|Transcendence], етап 1.',
      ],
    },
    {
      id: 's4', num: '04', from: 1600, range: ['1600', '2100'], title: 'Розвиток [Трансценденції|Transcendence]',
      badge: 'Ера Аркани', badgeHot: true,
      items: [
        'Фармте [Трансценденцію|Transcendence] етап 1, потім етап 2 в міру росту.',
        '[Аркана|Arcana] стає одним із найбільших джерел бойової міці.',
        'Використовуйте покращення крафтового спорядження та проміжні віхи, де потрібно.',
        '1900 — поширена контрольна точка перед наступним великим ривком.',
        'На 2100 відкриваються [Підкорення|Conquest]: [Вогненний храм|Fire Temple] і [Лігво Лютого Рогу|Ferocious Horn Den].',
      ],
    },
    {
      id: 's5', num: '05', from: 2100, range: ['2100', '2500'], title: 'Просунутий ривок',
      badge: 'Консолідація сили', badgeHot: true,
      items: [
        'Фармте [Підкорення|Conquest] T3 і сильніші рівні нагород.',
        'Працюйте над вищими етапами [Трансценденції|Transcendence] та сильнішими наборами [Аркани|Arcana].',
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
        'Використовуйте вищі рівні [Випробування Вознесіння|Ascension Trial], коли це реально.',
        'Ціль — поширений поріг входу в [рейд Лудри|Ludra raid]: ≈2800 бойової міці.',
      ],
    },
  ],

  systems: [
    {
      title: '[Благородний Пояс|Noble Belt]',
      text: 'Покращується сувоями з [Оплотів|Strongholds]. Шлях [морфу|Substance Morph] на +10:',
      tiers: [
        { color: 'green', label: 'Базовий / Зелений' },
        { color: 'blue', label: 'Рідкісний / Синій' },
        { color: 'gold', label: 'Унікальний / Золотий' },
      ],
    },
    { title: '[Амулет Одкровення|Revelation Amulet]', text: 'Покращується прогресом [Моноліту|Monolith] / пер.' },
    { title: '[Амулет Лютого Бою|Fierce Battle Amulet]', text: 'Пізніший PvP-амулет із [Магазину Безодні|Abyss Shop].' },
    {
      title: '[Руни Зіткнення|Clash Runes]',
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
      'Підземелля безкоштовні для проходження; отримання нагород коштує [Енергії Оділе|Odyle Energy].',
      'Більшість нагород ери 45 рівня зазвичай коштують 40 Оділе.',
      'Ніколи не тримайте енергію на максимумі. Витрачайте її на найкращий доступний контент.',
      '[Дослідження|Exploration] = цільовий / гарантований прогрес.',
      '[Підкорення|Conquest] = повторюваний цикл спорядження + [кіна|Kinah].',
    ],
  },

  /* period: 'day' | 'week'; max: 1 = звичайний чекбокс, >1 = лічильник */
  priorities: [
    { id: 'p-daily-quests', name: '[Щоденні квести|Daily Quests]', period: 'day', max: 1, hint: 'щодня' },
    { id: 'p-nightmare', name: '[Кошмар|Nightmare]', period: 'day', max: 2, hint: '2 проходження / день' },
    { id: 'p-daily-dungeon', name: '[Щоденне підземелля|Daily Dungeon] за [Камені Покращення|Enhance Stones]', period: 'week', max: 7, hint: '7 / тиждень' },
    { id: 'p-ascension', name: '[Випробування Вознесіння|Ascension Trial]', period: 'week', max: 3, hint: '3 / тиждень' },
  ],
  /* Тижневі входи Global (скидаються в середу). Джерела: Metabot, Aion 2 Timers (дані клієнта Global).
     Кількість спроб рейду Лудри джерела називають по-різному, тому її тут немає. */
  weekly: [
    { id: 'p-ascension', name: '[Випробування Вознесіння|Ascension Trial]', max: 3 },
    { id: 'p-daily-dungeon', name: '[Щоденне підземелля|Daily Dungeon]', max: 7, hint: '14 з підпискою' },
    { id: 'w-subjugation', name: 'Квитки [Приборкання|Subjugation]', max: 3 },
    { id: 'w-shugo', name: 'Ключі [Фестивалю Шуго|Shugo Festival]', max: 7, hint: '14 з підпискою' },
    { id: 'w-ex-krao', name: '[Дослідження|Exploration]: [Печера Крау|Krao Cave]', max: 7 },
    { id: 'w-ex-draupnir', name: '[Дослідження|Exploration]: [Драупнір|Draupnir]', max: 7 },
    { id: 'w-ex-urugugu', name: '[Дослідження|Exploration]: [Каньйон Урюгю|Urugugu Canyon]', max: 7 },
    { id: 'w-ex-vakron', name: '[Дослідження|Exploration]: [Вакрон|Vakron Sky Island]', max: 7 },
    { id: 'w-ex-fire', name: '[Дослідження|Exploration]: [Вогненний храм|Fire Temple]', max: 7 },
    { id: 'w-ex-horn', name: '[Дослідження|Exploration]: [Лігво Лютого Рогу|Ferocious Horn Den]', max: 7 },
  ],

  /* Запаси, що відновлюються з часом (Global). Джерело: Aion 2 Timers (клієнт Global), Metabot.
     every — години між поповненнями; daily — поповнення в щоденний ресет. Час тіків невідомий, тож це оцінка. */
  stocks: [
    { id: 'odyle', name: '[Енергія Оділе|Odyle Energy]', per: 15, every: 3, caps: [560, 840], capNote: 'з підпискою', claim: 40 },
    { id: 'conquest', name: 'Нагороди [Підкорення|Conquest]', per: 1, every: 8, caps: [21] },
    { id: 'transcendence', name: '[Трансценденція|Transcendence]', per: 1, every: 12, caps: [14] },
    { id: 'nightmare', name: 'Спроби [Кошмару|Nightmare]', per: 2, daily: true, caps: [14] },
  ],

  /* Білди класів для порівняння з персонажем (картка «Персонаж»).
     active.steps: [рівень вміння, опції спеціалізації на цьому рівні] — опції API не віддає, їх відмічають вручну.
     passives: групи в порядку пріоритету (ліве важливіше). aka — як вміння називається в API, якщо інакше. */
  builds: [
    {
      id: 'cleric-silence-13798', cls: 'Cleric', name: 'The Silence of the Infernal Monster',
      url: 'https://questlog.gg/aion-2/en/character-builder/TheSilenceOfTheInfernalMonster?build-id=13798',
      active: [
        { skill: 'Condemnation', steps: [[12, '2 4'], [20, '2 4 5']] },
        { skill: 'Divine Aura', steps: [[12, '3 4'], [16, '2 5'], [20, '3 4 5']] },
        { skill: 'Bolt', steps: [[16, '3 5'], [20, '3 4 5']] },
        { skill: 'Judgement Thunder', aka: ['Judgment Thunder'], steps: [[12, '1 2']] },
        { skill: 'Debilitating Mark', steps: [[12, '2 4']] },
        { skill: 'Earth Retribution', aka: ["Earth's Retribution"], steps: [[12, '2 4']] },
        { skill: 'Radiant Recovery', steps: [[12, '2 3']] },
        { skill: 'Chain of Torment', steps: [[12, '2 4'], [16, '4 5']] },
        { skill: 'Light of Regeneration', steps: [[12, '2 4']] },
        { skill: 'Healing Light', steps: [[12, '1 3']] },
        { skill: 'Lightning Strike', aka: ['Lightning Strike Scattershot'], steps: [[12, '3 4']] },
      ],
      passives: [
        ["Empyrean Lord's Grace", "Earth's Grace"],
        ['Healing Enhancement', 'Radiant Benediction', 'Warm Benediction', 'Immortal Veil'],
      ],
      stigmas: [
        { skill: 'Earth Punishment', target: 20, note: 'до 20 першою' },
        { skill: 'Prayer of Amplification' },
        { skill: 'Light of Protection' },
        { skill: 'Noble Aura' },
      ],
      stigmaNotes: [
        'Поміняйте місцями слоти Earth Punishment і Prayer of Amplification.',
        'Хочете Res / Yustiel (Summon Resurrection / Yustiel\'s Power) — замініть Noble Aura.',
        'Якщо в групі є Chanter — замініть Light of Protection на Res / Yustiel.',
      ],
      board: [
        'Активні вміння до 12',
        'Помаранчеві атакувальні вузли: Combat Speed, CDR, Damage Boost, Crit Damage Boost, Multi Hit Chance',
        'Пасивки: Empyrean Lord\'s Grace, Earth\'s Grace',
        'Атакувальні вузли: Attack, Crit',
        'Захисні: Damage Tolerance, Crit Damage Tolerance, HP, Defense',
        'Решту заповнюйте будь-чим, крім MP +50',
      ],
    },
  ],

  /* Збір у рідному регіоні (Вертерон для Елійців, Альтгард для Асмодіан): кількість — з інтерактивної карти та гайдів */
  collect: [
    { id: 'strongholds', name: '[Оплоти|Strongholds]', max: 15, reward: 'по 2 [сувої Благородного Пояса|Noble Belt Enhance Scrolls], разом 30' },
    { id: 'sealed', name: '[Запечатані підземелля|Sealed Dungeons]', max: 61, reward: '[Кристали Даеваніона|Daevanion Crystals] і [Камені Мудрості|Wisdom Stones]' },
  ],

  /* Активності для збору групи */
  groupActivities: [
    'Krao Cave', 'Draupnir', 'Urugugu Canyon', 'Vakron Sky Island', 'Fire Temple', 'Ferocious Horn Den',
    'Transcendence', 'Ascension Trial', 'Nightmare', 'Ludra raid', 'Spacetime Rift', 'Field Boss', 'Abyss',
  ],
  groupRoles: [
    { id: 'tank', name: 'Танк' }, { id: 'heal', name: 'Хіл' }, { id: 'dd', name: 'ДД' }, { id: 'support', name: 'Підтримка' },
  ],

  /* Ресет Global-серверів: щодня о 07:00 UTC (16:00 за часом гри, UTC+9), щотижня — у середу.
     weeklyDay: 0 = неділя … 3 = середа. */
  reset: { utcHour: 7, weeklyDay: 3 },
  prioritiesNote: 'Пороги [випробувань|Ascension Trial]: 1000 / 1500 / 2000 / 2500. Якщо плануєте підняти бойову міць, збережіть щотижневі входи на пізніші дні тижня.',

  thresholds: [
    { cp: 700, text: '[Підкорення|Conquest]: [Печера Крау|Krao Cave] + [Драупнір|Draupnir]' },
    { cp: 1000, text: '[Випробування Вознесіння|Ascension Trial] (Легке)' },
    { cp: 1400, text: '[Каньйон Урюгю|Urugugu Canyon] + доступ до [Вакрона|Vakron Sky Island]' },
    { cp: 1500, text: 'Випробування Вознесіння (Нормальне)' },
    { cp: 1600, text: '[Трансценденція|Transcendence], етап 1' },
    { cp: 1900, text: 'Поширена контрольна точка / ривок до етапу 2' },
    { cp: 2000, text: 'Випробування Вознесіння (Складне)' },
    { cp: 2100, text: '[Вогненний храм|Fire Temple] + [Лігво Лютого Рогу|Ferocious Horn Den]' },
    { cp: 2500, text: 'Випробування Вознесіння (Виклик)' },
    { cp: 2800, text: '[Рейд Лудри|Ludra raid]', final: true },
  ],

  /* Глосарій: українська назва → як це називається в англійському клієнті Global.
     Назви звірено з англомовними гайдами та базами для Global (див. README). */
  glossary: [
    { group: 'Ресурси й валюта', items: [
      ['Бойова міць (у гайді) / рівень предметів', 'Item Level / Gear Score'],
      ['Енергія Оділе', 'Odyle Energy'],
      ['Кристали Даеваніона', 'Daevanion Crystals'],
      ['Очки / дошки Даеваніона', 'Daevanion points / Daevanion Boards'],
      ['Камені Мудрості', 'Wisdom Stones'],
      ['Камені Покращення', 'Enhance Stones'],
      ['Кіна', 'Kinah'],
      ['Очки Безодні', 'Abyss Points'],
    ] },
    { group: 'Спорядження', items: [
      ['Благородний Пояс', 'Noble Belt'],
      ['Сувої Благородного Пояса', 'Noble Belt Enhance Scrolls'],
      ['Амулет Одкровення (PvE)', 'Revelation Amulet'],
      ['Амулет Лютого Бою (PvP)', 'Fierce Battle Amulet'],
      ['Руни Зіткнення', 'Clash Runes'],
      ['Морф (перетворення в наступну рідкість)', 'Substance Morph'],
      ['Легендарні предмети', 'Legendary gear'],
      ['Аркана', 'Arcana'],
    ] },
    { group: 'Світ і активності', items: [
      ['Регіональні квести', 'Regional Quests'],
      ['Побічні квести', 'Side Quests'],
      ['Запечатані підземелля', 'Sealed Dungeons'],
      ['Оплоти', 'Strongholds'],
      ['Моноліт', 'Monolith'],
      ['Емпірейські сліди (пера)', 'Empyrean Traces'],
      ['Пера Безодні', 'Abyss Empyrean Traces'],
      ['Розлом', 'Spacetime Rift'],
      ['Польові боси', 'Field Bosses'],
      ['Магазин Безодні', 'Abyss Shop'],
    ] },
    { group: 'Підземелля', items: [
      ['Експедиції (підземелля на 5 гравців)', 'Expeditions'],
      ['Дослідження (легший режим)', 'Exploration'],
      ['Підкорення (звичайний режим)', 'Conquest'],
      ['Печера Крау', 'Krao Cave'],
      ['Драупнір', 'Draupnir'],
      ['Каньйон Урюгю', 'Urugugu Canyon'],
      ['Вакрон', 'Vakron Sky Island'],
      ['Вогненний храм', 'Fire Temple'],
      ['Лігво Лютого Рогу', 'Ferocious Horn Den'],
      ['Щоденне підземелля', 'Daily Dungeon'],
      ['Кошмар', 'Nightmare'],
      ['Випробування Вознесіння', 'Ascension Trial'],
      ['Трансценденція', 'Transcendence'],
      ['Рейд Лудри', 'Ludra raid'],
    ] },
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
