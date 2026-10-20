using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Воспроизводимый прогон модуля-кандидата (ПЗ-2, ADR-002): аргумент запуска <c>-decalProbe N</c>.
/// Ставит N декалей через того же потребителя <see cref="BulletHoles"/>, что и выстрелы, в фиксированные точки
/// (сетка под уровнем, вне поля зрения), и пишет в журнал реализацию пула, счётчики, выделенную память и время.
/// Вход одинаковый при каждом запуске, поэтому результаты разных реализаций можно сравнивать.
/// </summary>
public static class BulletHoleProbe
{
    public const string Arg = "-decalProbe";

    /// <summary>Выполнить прогон, если задан <c>-decalProbe N</c>. Возвращает <c>true</c>, если прогон был.</summary>
    public static bool RunIfRequested(BulletHoles holes, string[] args)
    {
        string value = GameLog.ArgValue(args, Arg);
        if (value == null) return false;
        if (!int.TryParse(value, out int n) || n <= 0)
        {
            GameLog.Error("Probe", $"{Arg} {value} -- ожидается целое число > 0");
            return true;
        }
        Run(holes, n);
        return true;
    }

    public static void Run(BulletHoles holes, int n)
    {
        IObjectPool<GameObject> pool = holes.Pool;
        float now = Time.time;

        // Прогрев: первый проход по коду (JIT, инициализация типов) не измеряем
        for (int i = 0; i < pool.Capacity; i++) holes.Place(Point(i), Vector3.up, 0f, now);

        // Прирост используемой кучи Mono за прогон. GC.GetAllocatedBytesForCurrentThread в Mono плеера Unity
        // всегда 0, поэтому берём размер кучи; сборщик мусора во время прогона уменьшил бы его -- тогда "н/д".
        System.GC.Collect();
        long before = Profiler.GetMonoUsedSizeLong();
        int gcBefore = System.GC.CollectionCount(0);
        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) holes.Place(Point(i), Vector3.up, i * 37 % 360, now);
        sw.Stop();
        long after = Profiler.GetMonoUsedSizeLong();
        bool gcRan = System.GC.CollectionCount(0) != gcBefore || after < before;
        long allocated = before > 0 && !gcRan ? after - before : -1;

        GameLog.Info("Probe", $"decalProbe: pool={pool.GetType().Name.Replace("`1", "")} N={n} capacity={pool.Capacity} " +
                              $"created={pool.CreatedTotal} active={pool.CountActive} recycled={pool.Recycled} " +
                              $"heap+={(allocated >= 0 ? allocated + " Б" : "н/д")} time={sw.Elapsed.TotalMilliseconds:0.000} мс");
    }

    private static Vector3 Point(int i) => new Vector3(i % 50 * 0.2f, -500f, i / 50 % 50 * 0.2f);
}
