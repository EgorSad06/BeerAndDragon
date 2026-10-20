using System;

/// <summary>Что делает пул, когда все <see cref="IObjectPool{T}.Capacity"/> объектов уже выданы.</summary>
public enum PoolOverflow
{
    /// <summary>Забрать объект, выданный раньше всех: для него вызывается onRelease, затем он выдаётся заново.</summary>
    ReuseOldest,
    /// <summary>Не выдавать ничего: <see cref="IObjectPool{T}.Get"/> возвращает <c>null</c>.</summary>
    Fail,
}

/// <summary>
/// Пул переиспользуемых объектов с жёстким лимитом (модуль SP-1, контракт — docs/modules/object-pool.md).
/// Решает задачу: выдавать объекты (декали, вспышки, гильзы) без создания и уничтожения на каждое событие
/// и никогда не держать больше <see cref="Capacity"/> объектов.
/// </summary>
/// <remarks>
/// <para><b>Владение.</b> Пул владеет всеми созданными им объектами. Выданный объект потребитель использует,
/// пока не вернёт его через <see cref="Release"/>; после возврата, а также после принудительного
/// переиспользования (<see cref="PoolOverflow.ReuseOldest"/>) ссылку держать нельзя — объект снова принадлежит пулу.</para>
/// <para><b>Побочные эффекты.</b> Только вызовы фабрики <c>create</c> и обратных вызовов <c>onGet</c> / <c>onRelease</c>,
/// переданных при создании пула. Сам пул не выполняет ввода-вывода, не обращается к движку и не пишет журнал.</para>
/// <para><b>Потоки.</b> Не потокобезопасен: вызывать только из главного потока Unity.</para>
/// <para><b>Реализации.</b> <see cref="BoundedPool{T}"/> — без выделений памяти после прогрева, переиспользование O(Capacity);
/// <see cref="LinkedPool{T}"/> — переиспользование O(1), но каждая выдача выделяет узел списка.
/// Наблюдаемое поведение (результаты и ошибки) у них одинаковое; различаются только время и память.
/// Реализацию выбирает точка сборки <see cref="ObjectPoolFactory"/>.</para>
/// </remarks>
/// <typeparam name="T">Тип объектов пула; ссылочный.</typeparam>
/// <example>
/// <code>
/// IObjectPool&lt;GameObject&gt; pool = ObjectPoolFactory.Create(PoolKind.Bounded,
///     () =&gt; Object.Instantiate(prefab), capacity: 60,
///     onGet: go =&gt; go.SetActive(true), onRelease: go =&gt; go.SetActive(false));
/// GameObject hole = pool.Get();   // лимит исчерпан -- вернётся самый старый объект
/// pool.Release(hole);            // повторный Release(hole) -- InvalidOperationException
/// </code>
/// </example>
public interface IObjectPool<T> where T : class
{
    /// <summary>Наибольшее число объектов, которое пул может создать; задаётся при создании, &gt; 0.</summary>
    int Capacity { get; }

    /// <summary>Выдано и не возвращено. Всегда <c>CountActive + CountInactive == CountAll &lt;= Capacity</c>.</summary>
    int CountActive { get; }

    /// <summary>Создано и лежит в пуле свободным.</summary>
    int CountInactive { get; }

    /// <summary>Всего создано пулом и ещё не потеряно; никогда не превышает <see cref="Capacity"/>.</summary>
    int CountAll { get; }

    /// <summary>Сколько раз за жизнь пула вызывалась фабрика <c>create</c>.</summary>
    int CreatedTotal { get; }

    /// <summary>Сколько раз сработало переиспользование самого старого объекта (<see cref="PoolOverflow.ReuseOldest"/>).</summary>
    int Recycled { get; }

    /// <summary>
    /// Выдать объект.
    /// Порядок: свободный объект (последний возвращённый) → новый объект, если <c>CountAll &lt; Capacity</c> →
    /// при исчерпанном лимите по политике: <see cref="PoolOverflow.ReuseOldest"/> — самый давно выданный объект,
    /// <see cref="PoolOverflow.Fail"/> — <c>null</c> без изменения состояния.
    /// Для выданного объекта всегда вызывается <c>onGet</c>.
    /// </summary>
    /// <returns>Объект, принадлежащий пулу, или <c>null</c> (только при <see cref="PoolOverflow.Fail"/>).</returns>
    /// <exception cref="InvalidOperationException">Фабрика <c>create</c> вернула <c>null</c>.</exception>
    T Get();

    /// <summary>
    /// Вернуть выданный объект в пул; для него вызывается <c>onRelease</c>.
    /// Ошибки проверяются в порядке: <c>null</c> → объект не из этого пула → объект уже возвращён.
    /// При ошибке состояние пула не меняется.
    /// </summary>
    /// <param name="item">Объект, полученный из <see cref="Get"/> этого пула и ещё не возвращённый.</param>
    /// <exception cref="ArgumentNullException"><paramref name="item"/> равен <c>null</c>.</exception>
    /// <exception cref="InvalidOperationException">Объект создан не этим пулом, или уже возвращён.</exception>
    void Release(T item);

    /// <summary>Создать заранее <c>min(count, Capacity)</c> объектов (всего, с учётом уже созданных); они остаются свободными.</summary>
    /// <param name="count">Желаемое число объектов; отрицательное и нулевое ничего не делает.</param>
    void Prewarm(int count);

    /// <summary><c>true</c>, если объект сейчас выдан этим пулом; для <c>null</c> и чужих объектов — <c>false</c>.</summary>
    bool IsActive(T item);
}
