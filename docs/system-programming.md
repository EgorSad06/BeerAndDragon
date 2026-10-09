# Системное программирование: направления, первый модуль, контракт

## 1. Выбранные направления системной сложности

### Направление A — управление памятью в реальном времени (выбрано первым)
**Проблема в проекте.** Игра должна держать стабильные 60+ FPS (NFR-08), а в C#/Unity любая аллокация в куче
рано или поздно вызывает сборку мусора — заметный «фриз» кадра. Самое горячее место — дробовик:
выстрел = 12 дробин → до 12 `Instantiate` декалей-дырок + 12 отложенных `Destroy`
(`Weapon.cs`, метод `SpawnBulletHole`). При частой стрельбе это сотни созданий/уничтожений объектов в минуту.

**Что делаем.** Пулы объектов с жёстким лимитом: объекты создаются заранее, в бою только переиспользуются,
память после прогрева не выделяется. Метрика — байты, выделенные в управляемой куче за цикл Get/Release (цель: 0).

### Направление B — параллельные вычисления (Job System)
**Проблема.** Те же 12 дробин — это 12 последовательных `Physics.RaycastNonAlloc` в главном потоке (`Weapon.cs`, `FireSinglePellet`); меч и скейт
(проверка земли, поиск перил) добавляют свои запросы. С появлением врагов число запросов вырастет кратно.

**Что делаем.** Пакетные физические запросы `RaycastCommand.ScheduleBatch` через Unity Job System:
все лучи выстрела считаются параллельно в рабочих потоках, главный поток только забирает результаты.
Здесь системная сложность — синхронизация (кто и когда владеет `NativeArray`), отсутствие гонок
и детерминированный порядок результатов. Метрика — время кадра на выстрел (Profiler, режим `-diag`).

| | A — память | B — параллелизм |
|---|---|---|
| Ресурс | управляемая куча, GC | ядра CPU |
| Риск без решения | фризы кадра от GC | рост времени кадра с числом запросов |
| Инструмент Unity | собственный пул, Profiler (GC Alloc) | Job System, `NativeArray`, `RaycastCommand` |
| Модули | **SP-1** пул объектов → SP-1b декали через пул | SP-2 пакетная стрельба |

## 2. Первый модуль — SP-1 `BoundedPool<T>`
**Файл:** [`Assets/Game/Scripts/Core/BoundedPool.cs`](../Assets/Game/Scripts/Core/BoundedPool.cs)
**Тесты:** [`Assets/Game/Tests/Editor/BoundedPoolTests.cs`](../Assets/Game/Tests/Editor/BoundedPoolTests.cs) (10 тестов)
**Засеянные дефекты:** `pool-recycles-newest`, `pool-double-release` в [`ci/mutations.json`](../ci/mutations.json)

Модуль — чистый C# без зависимостей от сцены, поэтому тестируется в EditMode без запуска игры
и переиспользуется для любых объектов (декали, вспышки выстрела, гильзы, частицы).

### 2.1 Контракт
```csharp
public sealed class BoundedPool<T> where T : class
{
    public enum OverflowPolicy { ReuseOldest, Fail }

    BoundedPool(Func<T> create, int capacity, OverflowPolicy overflow = ReuseOldest,
                Action<T> onGet = null, Action<T> onRelease = null);

    void Prewarm(int count);   // создать заранее (на загрузке уровня)
    T    Get();                 // выдать объект
    void Release(T item);       // вернуть объект
    bool IsActive(T item);

    int Capacity, CountActive, CountInactive, CountAll, CreatedTotal, Recycled;
}
```

| # | Пункт контракта | Тип | Тест |
|---|---|---|---|
| C1 | `capacity > 0`, `create != null`, иначе `ArgumentOutOfRangeException` / `ArgumentNullException` | предусловие | `InvalidArguments_Throw` |
| C2 | `create()` не возвращает `null`, иначе `InvalidOperationException` | предусловие | `InvalidArguments_Throw` |
| C3 | **Инвариант:** `CountAll ≤ Capacity` всегда; `CountAll = CountActive + CountInactive` | инвариант | `Overflow_ReuseOldest_NeverExceedsCapacity` |
| C4 | `Get()` сначала отдаёт возвращённый объект, новый создаёт только если свободных нет и `CountAll < Capacity` | постусловие | `Get_CreatesUpToCapacity_ThenReusesReleased` |
| C5 | Лимит исчерпан, `ReuseOldest` → забирается объект, выданный **раньше всех** (по порядку выдачи), для него вызывается `onRelease`, затем `onGet` | постусловие | `Overflow_ReuseOldest_TakesOldestByIssueOrder` |
| C6 | Лимит исчерпан, `Fail` → `Get()` возвращает `null`, состояние не меняется | постусловие | `Overflow_Fail_ReturnsNull` |
| C7 | `Release` объекта, который уже возвращён, → `InvalidOperationException` (ERR-08) | ошибка | `Release_Twice_Throws` |
| C8 | `Release` чужого объекта → `InvalidOperationException` | ошибка | `Release_ForeignObject_Throws` |
| C9 | `onGet` вызывается при каждой выдаче, `onRelease` — при каждом возврате (включая принудительный в C5) | постусловие | `Callbacks_AreCalledOnGetAndRelease` |
| C10 | `Prewarm(n)` создаёт `min(n, Capacity)` неактивных объектов; после него `Get` не создаёт новых | постусловие | `Prewarm_CreatesInactiveObjects_LimitedByCapacity` |
| C11 | **Ресурсы:** после прогрева `Get`/`Release`/переполнение выделяют **0 байт** в управляемой куче (NFR-07) | нефункц. | `SteadyState_GetRelease_DoesNotAllocate` (констрейнт `Is.Not.AllocatingGCMemory()`) |
| C12 | Сложность: `Get`/`Release` — O(1); переполнение с `ReuseOldest` — O(Capacity) | нефункц. | — (обосновано в коде) |
| C13 | Не потокобезопасен: вызывать только из главного потока Unity | ограничение | — |

### 2.2 Решения реализации
- Все коллекции (`Stack`, `Dictionary`, `HashSet`) создаются сразу ёмкостью `capacity` → не растут → не аллоцируют (C11).
- Порядок выдачи — монотонный счётчик `long` в `Dictionary<T,long>`; поиск самого старого — линейный проход
  `foreach` по словарю (struct-энумератор, без аллокаций). Для лимитов в десятки–сотни объектов это быстрее
  и проще, чем поддерживать связный список (который аллоцирует узлы).
- Ошибки использования (C7, C8) — исключения, а не тихое игнорирование: двойной возврат в игре означает,
  что один объект показан в двух местах, и такую ошибку дешевле поймать сразу.

### 2.3 Состояние реализации
| Шаг | Статус |
|---|---|
| Контракт и реализация `BoundedPool<T>` | ✅ готово |
| 10 EditMode-тестов на все пункты контракта, включая отсутствие аллокаций | ✅ готово (локально проверены в .NET 8: 0 байт за 100 циклов) |
| Засеянные дефекты для mutation testing | ✅ 2 мутанта |
| SP-1b: `Weapon.SpawnBulletHole` через пул (`Instantiate/Destroy` → `Get/Release`, `onGet` — `SetActive(true)` + позиция, `onRelease` — `SetActive(false)`) | 🔨 следующий шаг |
| Замер «до/после» в Profiler (GC Alloc на выстрел) | 📋 после SP-1b |
| SP-2: `RaycastCommand` для дроби | 📋 направление B |
