# Модуль ObjectPool — пул объектов с жёстким лимитом

| | |
|---|---|
| Автор | EgorSad06 (<ФИО>, <группа>) |
| Код | [`IObjectPool.cs`](../../Assets/Game/Scripts/Core/IObjectPool.cs) (контракт), [`BoundedPool.cs`](../../Assets/Game/Scripts/Core/BoundedPool.cs), [`LinkedPool.cs`](../../Assets/Game/Scripts/Core/LinkedPool.cs) (реализации), [`ObjectPoolFactory.cs`](../../Assets/Game/Scripts/Core/ObjectPoolFactory.cs) (точка сборки) |
| Потребитель | [`BulletHoles`](../../Assets/Game/Scripts/GunScript/BulletHoles.cs) ← `Weapon` |
| Документация API | XML-комментарии C# → [object-pool-api.md](object-pool-api.md) (генерируется из XML компилятора) |
| Место на карте | [module-map.md](../architecture/module-map.md): `ObjectPool`, `BulletHoles` |
| Тесты | [`ObjectPoolContractTests`](../../Assets/Game/Tests/Editor/ObjectPoolContractTests.cs) (контракт × 2 реализации), [`BulletHolesTests`](../../Assets/Game/Tests/Editor/BulletHolesTests.cs) (потребитель × 2), [`BoundedPoolTests`](../../Assets/Game/Tests/Editor/BoundedPoolTests.cs) (память) |

## 1. Ответственность
**Цепочка:** требования FR-WPN-05 («выстрел в стену → декаль, старые исчезают при превышении лимита»),
NFR-07 (нет выделений памяти в горячем пути) и NFR-08 (≥ 60 FPS)
→ ответственность: выдавать объекты для частых коротких событий без создания и уничтожения на каждое событие,
никогда не держать больше лимита и при нехватке отдавать самый давно выданный объект
→ публичная операция `IObjectPool<T>.Get()` / `Release(item)`
→ потребитель `BulletHoles.Place()`; его вызывает `Weapon` на каждое попадание дроби.

**Вне границы модуля:**

| Обязанность | Где |
|---|---|
| создание и внешний вид объекта (префаб, `Instantiate`) | фабрика `create`, её передаёт точка сборки (`Weapon.SetupBulletHoles`) |
| что значит «объект выдан / убран» (`SetActive`) | обратные вызовы `onGet` / `onRelease` |
| где стоит декаль, сколько живёт | потребитель `BulletHoles` |
| выбор реализации | точка сборки `ObjectPoolFactory` по параметру `decalPool` конфигурации |
| попадание луча, слои, поверхность | `Weapon` |

**Причины изменения модуля:** другая политика переполнения, другая структура данных, требования к памяти и времени.
Причины изменения потребителя (вид дырок, время жизни) модуль не затрагивают.

## 2. Контракт `IObjectPool<T>`
| Часть | Решение |
|---|---|
| **Назначение** | Выдать объект (`Get`), вернуть (`Release`), создать заранее (`Prewarm`), узнать состояние (`IsActive`, счётчики `Capacity`, `CountActive`, `CountInactive`, `CountAll`, `CreatedTotal`, `Recycled`) |
| **Вход** | Конструктор: `create` — фабрика, не `null` и не возвращает `null`; `capacity` > 0; `overflow` — `ReuseOldest` или `Fail`; `onGet`, `onRelease` — необязательны. `Release(item)`: объект из `Get` этого пула, ещё не возвращённый. `Prewarm(count)`: любое целое, ≤ 0 ничего не делает |
| **Результат** | `Get`: свободный объект (последний возвращённый) → новый, если `CountAll < Capacity` → при исчерпанном лимите: `ReuseOldest` — самый давно выданный (для него вызывается `onRelease`, затем `onGet`), `Fail` — `null`. Для выданного объекта всегда вызывается `onGet` |
| **Гарантии (инварианты)** | `CountActive + CountInactive == CountAll ≤ Capacity` всегда; новых объектов сверх `Capacity` не создаётся никогда; `Recycled` растёт ровно на 1 за каждое переиспользование |
| **Ошибки и порядок проверки** | Конструктор: `create == null` → `ArgumentNullException` (первой), затем `capacity ≤ 0` → `ArgumentOutOfRangeException`. `Get`: фабрика вернула `null` → `InvalidOperationException`. `Release`: `null` → `ArgumentNullException` → объект не из этого пула → `InvalidOperationException` → уже возвращён → `InvalidOperationException`. При ошибке состояние пула не меняется. `Get` при `Fail` и исчерпанном лимите — **не ошибка**, а `null` |
| **Владение** | Пул владеет всеми созданными объектами. Выданный объект потребитель использует до `Release`; после `Release` и после принудительного переиспользования ссылку держать нельзя. Вход (`item`) не копируется — пул хранит ту же ссылку |
| **Побочные эффекты** | Только вызовы `create`, `onGet`, `onRelease`. Нет ввода-вывода, обращений к движку, журнала, статического состояния |
| **Потоки** | Не потокобезопасен; только главный поток Unity |

**Свойства реализаций (не входят в общий контракт):**

| | `BoundedPool<T>` | `LinkedPool<T>` |
|---|---|---|
| Хранение порядка выдачи | словарь «объект → номер выдачи» | двусвязный список + словарь «объект → узел» |
| `Get` при переполнении | O(Capacity): линейный поиск самого старого | O(1): первый узел списка |
| Память после прогрева | 0 байт на `Get`/`Release` (тест `SteadyState_GetRelease_DoesNotAllocate`) | узел `LinkedListNode<T>` на каждый `Get` (тест `LinkedPool_AllocatesOnGet_DocumentedCost`) |

## 3. Интеграция
- **Потребитель** `BulletHoles` получает `IObjectPool<GameObject>` через конструктор и вызывает только `Get`, `Release`,
  `Capacity`, `CountActive`. Конкретные классы пула ему неизвестны — в его файле нет ни `BoundedPool`, ни `LinkedPool`,
  ни `ObjectPoolFactory`.
- **Точка сборки** — `Weapon.SetupBulletHoles()`: `ObjectPoolFactory.Create(GameConfig.DecalPoolKind, …)` с фабрикой
  `Instantiate(bulletHolePrefab)`, `onGet = SetActive(true)`, `onRelease = SetActive(false)`, затем `Prewarm(maxDecals)`.
- Реализацию выбирает параметр `"decalPool": "bounded" | "linked"` в `game-config.json` (каркас ПЗ-1: неверное
  значение → `[ERROR][Config]`, код 2).

## 4. Подмена реализации
| | `BoundedPool` | `LinkedPool` |
|---|---|---|
| Как подключить | `"decalPool": "bounded"` (по умолчанию) | `"decalPool": "linked"` (`ci/pz02/linked-config.json`) |
| Потребитель | `BulletHoles` — один и тот же файл | |
| Сценарий в тестах | `BulletHolesTests` × 2 (фикстура с параметром `PoolKind`) | |
| Сценарий в игре | `-config <конфиг> -decalProbe 1000 -ciSmoke 1` → строка `[INFO][Probe]` | |

**Границы вывода.** Подмена подтверждает, что потребитель работает с любой реализацией контракта и что обе реализации
одинаково проходят набор тестов контракта. Она не подтверждает производительность: время и память сравниваются
отдельно (модуль-кандидат, [ADR-002](../adr/ADR-002-decal-pool.md)), в последующих работах.
Прогон `-decalProbe` ставит декали в фиксированные точки без лучей и физики — попадание пуль он не проверяет.
