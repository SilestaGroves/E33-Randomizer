# Логика доступности: черновик разметки

Цель: **весь контент гарантированно достижим**. Ключевой предмет никогда не попадает туда, откуда его нельзя получить, за предмет, который сам от него зависит, или в место, куда нельзя вернуться.

Файлы:

| Файл | Что это |
|---|---|
| [`E33Randomizer/Data/Logic/progression_logic.json`](../../E33Randomizer/Data/Logic/progression_logic.json) | Сами данные: регионы, ключевые предметы, правила для проверок |
| [`E33Randomizer/ProgressionLogicData.cs`](../../E33Randomizer/ProgressionLogicData.cs) | Загрузка и сопоставление проверки с правилом |
| [`checks_review.csv`](checks_review.csv) | Все 1247 проверок с выведенным регионом, актом и допустимостью. Открывается в Excel (разделитель `;`) |

Таблица перегенерируется командой:

```bash
E33_WRITE_LOGIC_REVIEW=1 dotnet test E33Randomizer.Tests --filter WriteReviewTable
```

## Как устроены данные

- **Проверка** — одна секция источника предметов: сундук, награда диалога, катсцена, ассортимент торговца, дроп врага. Её id: `<имя ассета>#<ключ секции>`, например `BP_Dialog_JarNeedLight#HealingTint_Shard`.
- **Правила** (`checkRules`) сопоставляют id с регионом по шаблону с `*`. Срабатывает первое подходящее правило. В правиле можно задать:
  - `requires` — какие предметы нужны, чтобы получить награду;
  - `eligible: false` — сюда нельзя класть ключевые предметы, с причиной;
  - `missable` и `act` — переопределения для одной проверки.
- **Регион** имеет акт (0 — пролог, 1–3 — акты, 4 — эпилог), признак `missable` (нельзя вернуться) и уровень уверенности разметки.
- **Ключевые предметы** (`progressionItems`) помечены `randomize: true`, если их можно перемещать.

Тесты гарантируют, что каждая проверка покрыта правилом и ни одна проверка в невозвратном месте не допускает ключевых предметов.

## Решения по умолчанию в черновике

Ключевые предметы **не кладутся** в:

1. **Пролог** (Lumière, Акт I) и **эпилог** — по вашему решению. Сюда же отнесены награды Boulangerie: они есть только в NG+.
2. **Монолит** и **Lumière, Акт III**. Я не уверен, что туда можно вернуться, поэтому по умолчанию считаю их невозвратными. Исключение — принудительные сюжетные катсцены там: их награду пропустить нельзя.
3. **Дроп с врагов.** Рандомизация врагов переносит самого врага, а у дропа есть шанс выпадения.
4. **Общие лут-таблицы катализаторов** — это не место на карте.
5. **Endless Tower** — очень поздние и тяжёлые бои.
6. **Торговцы.** Закрытый ассортимент ещё и требует разблокировки торговца.
7. **Мини-игры Gestral Beach** (гонка, волейбол, паркур) — награда зависит от навыка.

Пункты 2, 5 и 6 — мои решения по умолчанию, их легко поменять в JSON.

## Ключевые предметы

| Предмет | Код | Статус | Где в оригинале | Кому нужен | Уверенность |
|---|---|---|---|---|---|
| Resin | `Quest_Resin` | перемещается | сундук, Spring Meadows | White Jar (Spring Meadows) | высокая |
| Intact Mine | `Quest_Mine` | перемещается | сундук, Flying Waters | White Demineur (Flying Waters) | высокая |
| Wood Boards | `Quest_WoodBoards` | перемещается | сундук, Esoteric Ruins | White Portier (Esoteric Ruins) | высокая |
| Mushroom | `Quest_Mushroom` | перемещается | сундук, Esquie's Nest | Karatom (Gestral Village) | высокая |
| Rock Crystal | `Quest_HexgaRock` | перемещается | сундук, Stone Wave Cliffs + дроп White Rocher | White Hexga (Stone Wave Cliffs) | высокая |
| Weird Pictos | `Quest_WeirdPictos` | перемещается | награда Тома, пролог | торговец в Gestral Village | высокая |
| Bourgeon Skin | `Quest_BourgeonSkin` | перемещается | дроп Bourgeon (×3 врага) | неизвестно (вне данных рандомайзера) | средняя |
| Eternal Ice | `Quest_EternalIce` | перемещается | дроп Gargant (Frozen Hearts) | неизвестно | средняя |
| Colour / Shape / Light of the Beast | `Quest_CleaWorkshop_Part1..3` | перемещаются | Painting Workshop | неизвестно | низкая |
| Wooden Stick | `Quest_WoodenStick` | перемещается | «Excalibur», Gestral Village | неизвестно | низкая |
| Old Key | `Quest_OldKey` | перемещается | Колетт, пролог | **неизвестно** | низкая |
| A journal from Gustave's apprentices | `Quest_ApprenticesJournal` | перемещается | ученики, пролог | **неизвестно** | низкая |
| Festival Token | `FestivalToken` | на месте | пролог | жетоны фестиваля, только пролог | высокая |
| A uniform for Richard's son | `Quest_UniformForSon` | на месте | Ричард, пролог | Жюль, только пролог | высокая |
| Lost Gestral, Molten Heart, Chromatic Ink, Underground Key, Manor Family Canvas, A flower for Sophie | — | на месте | выдаются тем, что рандомайзер не редактирует | — | высокая |

«Кому нужен» определён по ассетам: диалог, который проверяет наличие предмета, содержит его кодовое имя.

## Вопросы для проверки

1. **Old Key** и **журнал учеников**: где они используются? Если в прологе, их нужно оставить на месте.
2. **Painting Workshop**: нужен ли для пути 2 предмет из пути 1, а для пути 3 — из пути 2? Где используются три предмета «...of the Beast»?
3. Где используются **Bourgeon Skin**, **Eternal Ice** и **Wooden Stick**?
4. Требуют ли предмет **White Chalier** (Flying Cemetery), **White Troubadour** (Stone Quarry) и **«Excalibur»** (Gestral Village)?
5. Можно ли вернуться в **Монолит** после Акта II и в **Lumière Акт III** после финала? Если да, это ещё 88 допустимых проверок.
6. Можно ли вернуться в **Old Lumière** после Акта I? Сейчас считается, что да.
7. Регионы с низкой уверенностью (акт определён на глаз; это влияет только на порядок, не на достижимость): Yellow Harvest, Stone Quarry, Gestral Beach, Falling Leaves, Crimson Forest, Esoteric Ruins, Flying Cemetery, Sinister Cave, Dark Shores, The Manor, Sunless Cliffs, Endless Night Sanctuary, The Chosen Path, арены, The Carousel, Flying Casino, White Sands, Fixed-Camera Levels, Rock Trailing, Sky Island, Painting Workshop, Sacred River, Crushing Cavern, Red Woods, The Fountain.
