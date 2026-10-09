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

  /* Тижневі ліміти з пачноутів (не входи, тому не рахуються в «Використано X з N тижневих входів») */
  weeklyLimits: [
    { id: 'w-wing-morph', name: '[Морф Запечатаних Крил|Sealed Wings → Enhance Stones]', max: 20, hint: '20 / тиждень на сервер, з 7.10' },
    { id: 'w-res-stone', name: '[Камінь Воскресіння|Resurrection Spiritstone] за кінари', max: 10, hint: '10 / тиждень по 25 000, до 16.12' },
  ],

  /* Пачноути Global: переклад, тлумачення і для кого це важливо.
     who: all — усім; founder — покупцям Founder's Pack; stones — хто фармить камені заточки;
     craft — ремесло; raid — рейди й PvP; asmodians / elyos — лише ця раса; qol — зручність.
     kind: plus — на користь гравцям, minus — обмеження, fix — виправлення.
     act — що зробити; counter — id лічильника в «Тижневих лімітах». Новий пачноут — новий об'єкт зверху. */
  patches: [
    {
      id: '6ac5e6e8d97eae18cc40e34e',
      date: '2026-10-07',
      title: 'Пачноут 7 жовтня',
      en: '[Notice] Patch Notes | Oct. 6 (PDT) / Oct. 7 (CEST)',
      maint: 'Техобслуговування 7 жовтня з 06:30 UTC (09:30 за Києвом), 3 год 30 хв; азійські сервери — 5 год 30 хв.',
      tldr: [
        'Founder\'s Pack тепер на всіх персонажах і серверах + безкоштовний магазин для покупців пакета.',
        'Морф Запечатаних Крил у камені заточки: ліміт 20 на тиждень.',
        'Більше Raw Leather у Вертероні, Альтгарді й Хаотичній Нижній Решанті.',
        'Камінь Воскресіння за 25 000 кінар, 10 на тиждень, до 16 грудня.',
        'Асмодіанам знову зараховується тижнева місія «Daeva of Glorious Deeds».',
      ],
      groups: [
        {
          title: 'Покращення',
          items: [
            {
              en: 'Founder\'s Pack purchasers will be able to use Skins, Titles, and other items on other characters and servers.',
              ua: 'Покупці [Пакета засновника|Founder\'s Pack] зможуть користуватися скінами, титулами та іншими предметами з пакета на інших персонажах і серверах.',
              why: 'Раніше бонуси пакета були прив\'язані до одного персонажа. Тепер їх отримують і твінки, і персонажі на інших серверах. Титули в Aion 2 (категорії Attack, Defense, Etc) дають бонуси до характеристик, тож для твінків це ще й невелике посилення.',
              who: ['founder'], kind: 'plus', act: 'Зайдіть кожним персонажем і перевірте скіни та титули.',
            },
            {
              en: 'Founder\'s Pack purchasers will be able to use the dedicated Shop to purchase items they do not yet own for free, based on their Founder\'s Pack tier.',
              ua: 'Покупці пакета отримають окремий магазин, де безкоштовно «купують» предмети свого рівня пакета, яких у них ще немає.',
              why: 'Так видають вміст пакета на кожного персонажа: заходите в магазин і забираєте те, чого бракує.',
              who: ['founder'], kind: 'plus', act: 'Відкрийте магазин пакета на кожному персонажі й заберіть предмети.',
            },
            {
              en: 'The free Founder\'s Pack-dedicated Shop will be available for all characters on all servers until a closure notice is issued.',
              ua: 'Безкоштовний магазин пакета працюватиме для всіх персонажів на всіх серверах, доки не оголосять про його закриття.',
              why: 'Термін не обмежений, але магазин можуть закрити з попередженням. Новим твінкам краще забрати все одразу.',
              who: ['founder'], kind: 'plus',
            },
            {
              en: 'Items you already own from your Founder\'s Pack will not be purchased again; if you end up with duplicate items that remain in your inventory, we plan to add a feature allowing you to discard them at a later date.',
              ua: 'Предмети з пакета, які у вас уже є, повторно не купуються. Якщо в інвентарі залишаться дублікати, пізніше додадуть можливість їх викинути.',
              why: 'Дублікати поки займатимуть місце в інвентарі. Не продавайте й не руйнуйте їх навмання, дочекайтеся офіційної функції.',
              who: ['founder'], kind: 'fix',
            },
            {
              en: 'Skins obtained in duplicate through the Founder\'s Pack-dedicated Shop will not affect the purchase limit for paid Skins, and a fix to prevent duplicate entries will be planned for a future update.',
              ua: 'Скіни-дублікати з магазину пакета не зменшують ліміт покупки платних скінів. Виправлення, щоб дублікати не з\'являлися, заплановане на наступні оновлення.',
              why: 'Якщо отримали скін двічі, ліміт на платні скіни від цього не постраждає.',
              who: ['founder'], kind: 'fix',
            },
            {
              en: 'If you are unable to complete a purchase while buying the Founder\'s Pack, please close the client and try again.',
              ua: 'Якщо покупка пакета не завершується, закрийте клієнт гри і спробуйте ще раз.',
              why: 'Відома проблема з оплатою: перезапуск клієнта її обходить.',
              who: ['founder'], kind: 'fix',
            },
            {
              en: 'A weekly limit of 20 Morphs per server will be added to the Substance Morph formula for morphing Sealed Wings into Enhance Stones.',
              ua: 'Рецепт [морфу|Substance Morph] «[Запечатані Крила|Sealed Wings] → [Камені заточки|Enhance Stones]» отримує ліміт: 20 морфів на тиждень на сервер.',
              why: 'Це був популярний спосіб швидко добути камені заточки. Тепер дохід обмежений: максимум 20 морфів між тижневими ресетами (середа, 07:00 UTC). Камені заточки стануть дефіцитнішими, і їх ціна на ринку, ймовірно, зросте. Ще більше причин точити лише жовте спорядження.',
              who: ['stones'], kind: 'minus', act: 'Робіть 20 морфів щотижня до ресету й відмічайте їх у «Тижневих лімітах».', counter: 'w-wing-morph',
            },
            {
              en: 'The drop rate for "Raw Leather," obtainable from monsters in the "Chaotic Lower Reshanta," will be increased.',
              ua: 'Збільшено шанс випадіння «[Сирої шкіри|Raw Leather]» з монстрів у [Хаотичній Нижній Решанті|Chaotic Lower Reshanta].',
              why: 'Raw Leather — матеріал для ремесла. Фарм у Решанті тепер вигідніший.',
              who: ['craft'], kind: 'plus',
            },
            {
              en: 'The drop rate for "Raw Leather," obtainable from monsters in "Verteron" and "Altgard," will be increased.',
              ua: 'Збільшено шанс випадіння Raw Leather з монстрів у [Вертероні|Verteron] та [Альтгарді|Altgard].',
              why: 'Шкіру тепер легше фармити в рідному регіоні, поруч з Оплотами та Запечатаними підземеллями. Ціна на ринку, найімовірніше, впаде, тож купувати її стане дешевше, а продавати — менш вигідно.',
              who: ['craft', 'all'], kind: 'plus',
            },
            {
              en: 'New Kina items will be added to the Wind Breeze Merchants. Item: Resurrection Spiritstone (Season 1) – Limited to 10 purchases per server per week. Price: 25,000 Kina. Sales period: after the maintenance on October 6, 2026 (PDT) through before the maintenance on December 15, 2026 (PST).',
              ua: 'У [Торговців Вітерця|Wind Breeze Merchants] нові товари за кінари: [Камінь Воскресіння (Сезон 1)|Resurrection Spiritstone (Season 1)], 10 штук на тиждень на сервер, 25 000 кінар за штуку. Де: Меню → Магазин → Wind Breeze Merchants → Special. Продаж з техобслуговування 7 жовтня до техобслуговування 16 грудня (за європейським часом).',
              why: 'Судячи з назви, це камінь для воскресіння на місці смерті без забігу від точки відродження. Найкорисніший у рейдах і на польових босах. «Season 1» означає, що він для першого сезону. Повний тижневий ліміт коштує 250 000 кінар. Хто ходить у рейди, хай купує щотижня, поки продаж не закінчився.',
              who: ['raid', 'all'], kind: 'plus', act: 'Купуйте до 10 каменів щотижня до 16 грудня.', counter: 'w-res-stone',
            },
          ],
        },
        {
          title: 'Виправлення',
          items: [
            {
              en: 'The issue where the maximum number of Map Pins that can be placed on the map is incorrectly displayed will be fixed. You will be able to place up to 30 Map Pins.',
              ua: 'Виправлено неправильне відображення максимальної кількості міток на карті. Тепер можна поставити до 30 міток.',
              why: 'Зручно для маршрутів фарму: позначте Оплоти, Запечатані підземелля й точки босів.',
              who: ['qol', 'all'], kind: 'fix',
            },
            {
              en: '[Asmodian] Challenge Season Missions > The issue where the "Daeva of Glorious Deeds" mission in the Weekly Missions cannot be completed properly will be fixed.',
              ua: '[Асмодіани] Місії сезону випробувань: тижнева місія «[Даева славних звершень|Daeva of Glorious Deeds]» знову зараховується.',
              why: 'Асмодіани втрачали нагороду сезону за цю місію. Цього тижня її можна виконати, тож закрийте до ресету.',
              who: ['asmodians'], kind: 'fix', act: 'Виконайте тижневу місію «Daeva of Glorious Deeds» до середи.',
            },
            {
              en: 'The issue where the "Movement Controls" option appears multiple times under Settings > Key Settings > General will be fixed.',
              ua: 'Виправлено повтор пункту «Movement Controls» у Налаштування → Клавіші → Загальні.',
              why: 'Лише інтерфейс налаштувань.',
              who: ['qol'], kind: 'fix',
            },
            {
              en: 'An issue where certain Emote commands do not function properly in chat will be fixed.',
              ua: 'Виправлено деякі команди емоцій у чаті, які не спрацьовували.',
              why: 'Косметика, на силу персонажа не впливає.',
              who: ['qol'], kind: 'fix',
            },
          ],
        },
      ],
    },
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
  /* Пріоритетні польові боси: 48–51 рівень. З кожного падає свій іменний Unique-сет (рукавиці, чоботи, плащ, сережки,
     кільце по ~12,9 %, шолом і наплічники 10,3 %, поножі 8,6 %, нагрудник 6,4 %) — у середньому ~1 Unique-предмет
     за вбивство; з Gartua ще Legend-артворк (25 %). З босів 45 рівня Unique (Wisdom / Fantasy) падають з шансом 0,1–0,2 %.
     Дані: metabot.gg (шанси на одне вбивство), 2026-10-08. Ці ж коди — в supabase/functions/discord-alerts і в
     overlay/data/priority_bosses.json. */
  /* DPS-метр 1 HP: історія версій (нове — зверху) і що в роботі. Дати й посилання на завантаження сайт бере з GitHub. */
  meter: {
    repo: 'batareuko/sula',
    changes: [
      { v: '1.4.4', items: ['Урон групи рахується повністю: удари союзника, якого оверлей ще не знав як члена групи, більше не губляться — він з\'являється з усім уроном за бій.'] },
      { v: '1.4.3', items: ['Фільтр групи працює одразу після оновлення чи перезапуску посеред гри: оверлей пам\'ятає ваш нік, а до того показує лише вашу групу.'] },
      { v: '1.4.2', items: ['Гір скор членів групи — зі списку групи, без огляду.', 'Група, бойова міць і ГС більше не губляться після перезапуску чи оновлення оверлея.'] },
      { v: '1.4.1', items: ['Гір скор оглянутих у грі гравців — біля ніка в кожному бою, поруч із бойовою міццю.'] },
      { v: '1.4.0', items: ['Розбір смерті: хто вбив, сервер, гір скор, за скільки секунд і якими скілами; пети — разом із власником.', 'Розбір видно 20 с після смерті, далі — знову ваш бій.'] },
      { v: '1.3.2', items: ['Таймери Вертерона: точний час з ігрового списку для всіх 24 босів.', 'Після входу в гру — нагадування відкрити список босів, якщо час застарів.'] },
      { v: '1.3.1', items: ['Район боса на карті, цикли відродження з гайда, кнопка відкрити зону на карті.', 'Дзвіночки на ★ пріоритетних босах — самі; старий час таймер називає старим.'] },
      { v: '1.3.0', items: ['Забіги на манекені: номер, порівняння з рекордом і попереднім забігом.', 'Сторінка для стріму в OBS (localhost:7799).'] },
      { v: '1.2.7', items: ['Рядок таймерів босів і нагадувань (Розлом, ресет) — завжди на картці.', 'Втрати пакетів поруч із пінгом.'] },
      { v: '1.2.6', items: ['Бої, повз які ви проходите, більше не показуються і не зберігаються.', 'Динамічне вікно: один рядок поза боєм, рядок на кожного в бою.', 'Бойова міць біля ніків.'] },
      { v: '1.2.5', items: ['Суворіший фільтр групи зі статусом на кнопці ГРУПА.', 'Тонші компактні рядки.'] },
      { v: '1.2.4', items: ['Новий сучасний стиль, легший і плоский.'] },
      { v: '1.2.3', items: ['Таймери босів окремо для кожного сервера, з часом із гри.', 'Втрати ↑ до сервера і фризи в рядку мережі.'] },
      { v: '1.2.2', items: ['У відкритому світі — лише ви і ваша група.'] },
      { v: '1.2.1', items: ['Самооновлення копії, розпакованої з zip.'] },
      { v: '1.2.0', items: ['Старт разом з грою і вихід після неї; портрети монстрів; пам\'ять місця вікон; компактний вигляд.'] },
    ],
    next: [
      'PvP-метр для Безодні: ваш урон по гравцях окремим боєм.',
      'Бойова міць чужих гравців в огляді — шукаємо в пакетах.',
      'Таймери босів спільні для гільдії: хто відкрив список — оновлює всім.',
    ],
  },

  bossPriority: {
    ids: ['2101120', '2101122', '2101131', '2101074', '2400855', '2400854', '2400853', '2400800'],
    loot: 'іменний Unique-сет, ~1 предмет за вбивство',
    note: 'Пріоритетні (★) — боси 48–51 рівня: з кожного падає свій Unique-сет, у середньому ~1 предмет за вбивство. Дзвіночки для них увімкнені самі; у Discord 1 HP про них сповіщає бот.',
  },
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
