# Логика доступности ключевых предметов

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

## Принятые решения

- В логике только **Key Item**. Skill Unlock и Merchant Unlock не перемещаются; их лишние копии, которые мог положить «полностью случайный» пул, заменяются заполнителем.
- Бои, где враги роняют прогрессионные предметы (разблокировки торговцев, навыки-художника Маэль с Paintress), **не рандомизируются**: дроп принадлежит типу врага. Настройка «Keep fights whose enemies drop progression items».
- Ключевые предметы **не кладутся** в:
  - пролог и эпилог (NG+-награды Boulangerie туда же);
  - **Lumière, Акт III** — пока не подтверждено, что туда можно вернуться после финала;
  - дроп с врагов, общие лут-таблицы, Endless Tower, мини-игры Gestral Beach;
  - **закрытый** ассортимент торговцев. Открытый разрешён.
- Монолит и Old Lumière возвратные и допускают ключевые предметы.
- Каждый ключевой предмет кладётся один раз и в отдельную проверку.

## Ключевые предметы

| Предмет | Код | Статус | Где в оригинале | Для чего |
|---|---|---|---|---|
| Resin | `Quest_Resin` | перемещается | сундук, Spring Meadows | White Jar (Spring Meadows) |
| Intact Mine | `Quest_Mine` | перемещается | сундук, Flying Waters | White Demineur (Flying Waters) |
| Wood Boards | `Quest_WoodBoards` | перемещается | сундук, Esoteric Ruins | White Portier (Esoteric Ruins) |
| Mushroom | `Quest_Mushroom` | перемещается | сундук, Esquie's Nest | Karatom (Gestral Village) |
| Rock Crystal | `Quest_HexgaRock` | перемещается | сундук, Stone Wave Cliffs + дроп White Rocher | White Hexga (Stone Wave Cliffs) |
| Weird Pictos | `Quest_WeirdPictos` | перемещается | награда Тома, пролог | торговец в Gestral Village |
| Bourgeon Skin | `Quest_BourgeonSkin` | перемещается | дроп Bourgeon | маленький Буржон → сундук в The Small Bourgeon |
| Eternal Ice | `Quest_EternalIce` | перемещается | дроп Gargant (Frozen Hearts) | Grandis на Monoco's Station → закрытый ассортимент |
| Colour / Shape / Light of the Beast | `Quest_CleaWorkshop_Part1..3` | перемещаются | Painting Workshop (пути независимы) | статуя, открывающая бой с Lampmaster |
| Old Key | `Quest_OldKey` | перемещается | Колетт, пролог | после пролога |
| A journal from Gustave's apprentices | `Quest_ApprenticesJournal` | перемещается | ученики, пролог | после пролога |
| Festival Token | `FestivalToken` | на месте | пролог | жетоны фестиваля, только пролог |
| A uniform for Richard's son | `Quest_UniformForSon` | на месте | Ричард, пролог | Жюль, только пролог |
| Lost Gestral, Molten Heart, Chromatic Ink, Underground Key, Manor Family Canvas, A flower for Sophie | — | на месте | выдаются тем, что рандомайзер не редактирует | — |

Wooden Stick — шуточный предмет без применения, поэтому не считается прогрессом и рандомизируется как обычный.

## Акты

- Акт I заканчивается гибелью Гюстава на Stone Wave Cliffs: бой `SC_MirrorRenoir_GustaveEnd` идёт сразу после `SC_LampMaster`.
- Forgotten Battlefield и Monoco's Station отнесены к Акту II: их обязательные бои идут позже.
- Old Lumière — конец Акта II.

Акт влияет только на подписи в спойлер-логе и в таблице, на достижимость он не влияет: логика гарантирует, что предмет можно получить, но не то, что он попадётся раньше места применения.

## Сюжетные бои и туториалы

Не рандомизируются (настройка «Keep scripted story battles and tutorials»):

- энкаунтеры, которые игра сама помечает как сюжетные (`IsNarrativeBattle`): `SC_MirrorRenoir_GustaveEnd` (Гюстав против Ренуара), финальная дуэль `FinalBossVerso`/`FinalBossMaelle`, обучающий бой с Маэль, туториалы прыжка, Сиэль и Моноко;
- туториальные бои по имени боя или врага: всё с `Tuto` (включая варианты `NoTuto`) — контратака на градиент, градиентная атака, щиты Eveque, летающий Volester, прыжок; первые бои `SM_First*`; тренировочный манекен `PunchingBall`.

Остальные бои с Ренуаром рандомизируются: `OL_MirrorRenoir_FirstFight` (Old Lumière) и `MM_MirrorRenoir` (Монолит).

## Проверить в игре

1. Ключевой предмет из сундука или от торговца засчитывается квестом.
2. Торговец после дуэли открывает ассортимент (подтверждает, что защита боёв с дропом нужна).
3. Масштабирование уровней.
4. Рандомизированные бои с Ренуаром `OL_MirrorRenoir_FirstFight` и `MM_MirrorRenoir` проходятся и не ломают сюжет.
5. Мод сам копируется в `~mods` при генерации.
6. Можно ли вернуться в **Lumière Акт III** после финала. Если да, это ещё 45 допустимых проверок: в `progression_logic.json` у региона `"Lumiere - Act 3"` поставить `"missable": false`.

## Регионы с низкой уверенностью в акте

Влияет только на подписи: Yellow Harvest, Stone Quarry, Gestral Beach, Falling Leaves, Crimson Forest, Esoteric Ruins, Flying Cemetery, Sinister Cave, Dark Shores, The Manor, Sunless Cliffs, Endless Night Sanctuary, The Chosen Path, арены, The Carousel, Flying Casino, White Sands, Fixed-Camera Levels, Rock Trailing, Sky Island, Painting Workshop, Sacred River, Crushing Cavern, Red Woods, The Fountain.
