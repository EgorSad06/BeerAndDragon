using System;
using NUnit.Framework;

namespace BeerAndDragon.Tests
{
    // Проверки магазина и запаса (МДК.03.02 ПЗ-2). Проект проверок, источники ожидаемых результатов и чек-лист --
    // docs/mdk0302/test-plan.md; номер теста (T01...) совпадает с номером строки в таблице проекта.
    // Параметры по умолчанию -- дробовик из сцены: магазин 2, запас 16, предел 40, перезарядка 0,9 с.
    // Время -- параметр now (заглушка системного времени): тесты не ждут и не зависят от Time.time.
    public class AmmoMagazineTests
    {
        private const float Reload = 0.9f;

        private static AmmoMagazine Shotgun(int reserve = 16, int maxReserve = 40, bool infinite = false, bool oneByOne = false)
            => new AmmoMagazine(2, reserve, maxReserve, Reload, infinite, oneByOne);

        // Магазин с заданным числом патронов: выстрелы из полного магазина (состояние готовится через API, без рефлексии)
        private static AmmoMagazine WithMagazine(int magazine, int reserve, bool infinite = false, bool oneByOne = false)
        {
            AmmoMagazine m = Shotgun(reserve, 40, infinite, oneByOne);
            for (int i = m.Magazine; i > magazine; i--) Assert.AreEqual(FireResult.Fired, m.TryFire());
            return m;
        }

        // ---------- П1 (FR-WPN-01): выстрел ----------

        [Test]
        public void T01_Fire_WithAmmo_TakesOneRound()
        {
            AmmoMagazine m = Shotgun();
            Assert.AreEqual(FireResult.Fired, m.TryFire());
            Assert.AreEqual(1, m.Magazine);
            Assert.AreEqual(16, m.Reserve, "выстрел не трогает запас");
        }

        [Test]
        public void T02_Fire_LastRound_EmptiesMagazine()
        {
            AmmoMagazine m = WithMagazine(1, 16);
            Assert.AreEqual(FireResult.Fired, m.TryFire());
            Assert.AreEqual(0, m.Magazine);
        }

        // ---------- П2 (FR-WPN-03.1): нет патронов ----------

        [Test]
        public void T03_Fire_EmptyMagazine_NoShotCountersUnchanged()
        {
            AmmoMagazine m = WithMagazine(0, 0);
            Assert.AreEqual(FireResult.Empty, m.TryFire());
            Assert.AreEqual(0, m.Magazine);
            Assert.AreEqual(0, m.Reserve);
        }

        [Test]
        public void T04_Reload_NoAmmoAtAll_DoesNotStart()
        {
            AmmoMagazine m = WithMagazine(0, 0);
            Assert.IsFalse(m.TryStartReload(0f));
            Assert.IsFalse(m.IsReloading);
            Assert.AreEqual(0, m.Tick(10f));
            Assert.AreEqual(0, m.Magazine);
        }

        // ---------- П3 (FR-WPN-03): начинается ли перезарядка -- таблица решений ----------
        // Условия: магазин полон | запас > 0 | запас бесконечный; правило «уже идёт перезарядка» -- T10

        [TestCase(0, 16, false, true,  TestName = "T05_Reload_Decision_D1_PartialMagazine_WithReserve_Starts")]
        [TestCase(0, 0,  false, false, TestName = "T06_Reload_Decision_D2_NoReserve_Rejected")]
        [TestCase(0, 0,  true,  true,  TestName = "T07_Reload_Decision_D3_InfiniteReserve_Starts")]
        [TestCase(2, 16, false, false, TestName = "T08_Reload_Decision_D4_FullMagazine_Rejected")]
        [TestCase(1, 1,  false, true,  TestName = "T09_Reload_Decision_D5_OneMissing_OneInReserve_Starts")]
        public void Reload_DecisionTable(int magazine, int reserve, bool infinite, bool expectedStart)
        {
            AmmoMagazine m = WithMagazine(magazine, reserve, infinite);
            Assert.AreEqual(expectedStart, m.TryStartReload(0f));
            Assert.AreEqual(expectedStart, m.IsReloading);
        }

        [Test]
        public void T10_Reload_Decision_D6_AlreadyReloading_IgnoredTimerNotRestarted()
        {
            AmmoMagazine m = WithMagazine(0, 16);
            Assert.IsTrue(m.TryStartReload(0f));
            Assert.IsFalse(m.TryStartReload(0.5f), "повторное R во время перезарядки игнорируется (ОП-2)");
            m.Tick(Reload);
            Assert.AreEqual(2, m.Magazine, "таймер не перезапустился: патроны на месте через 0,9 с от первого R");
        }

        // ---------- П4 (FR-WPN-03): перенос патронов из запаса -- классы и границы ----------

        [TestCase(0, 10, 2, 8,  TestName = "T11_Reload_Transfer_ReserveCoversNeed")]
        [TestCase(1, 16, 2, 15, TestName = "T12_Reload_Transfer_PartialMagazine")]
        [TestCase(0, 1,  1, 0,  TestName = "T13_Reload_Transfer_ReserveLessThanNeed")]
        [TestCase(0, 2,  2, 0,  TestName = "T14_Reload_Transfer_ReserveExactlyNeed")]
        public void Reload_Transfer(int magazine, int reserve, int expectedMagazine, int expectedReserve)
        {
            AmmoMagazine m = WithMagazine(magazine, reserve);
            Assert.IsTrue(m.TryStartReload(0f));
            m.Tick(Reload);
            Assert.AreEqual(expectedMagazine, m.Magazine, "магазин = min(2, магазин + запас)");
            Assert.AreEqual(expectedReserve, m.Reserve, "запас уменьшается на перенесённое");
            Assert.IsFalse(m.IsReloading);
        }

        [Test]
        public void T15_Reload_TimeBoundary_JustBeforeAndExactly()
        {
            AmmoMagazine m = WithMagazine(0, 10);
            m.TryStartReload(5f);
            Assert.AreEqual(0, m.Tick(5f + Reload - 0.001f), "до 0,9 с патроны не переносятся");
            Assert.AreEqual(0, m.Magazine);
            Assert.IsTrue(m.IsReloading);
            Assert.AreEqual(2, m.Tick(5f + Reload), "ровно через 0,9 с -- перенос (граница включена)");
            Assert.AreEqual(2, m.Magazine);
        }

        [Test]
        public void T16_Reload_InfiniteReserve_FillsWithoutSpending()
        {
            AmmoMagazine m = WithMagazine(0, 0, infinite: true);
            m.TryStartReload(0f);
            m.Tick(Reload);
            Assert.AreEqual(2, m.Magazine);
            Assert.AreEqual(0, m.Reserve);
        }

        // ---------- Переходы состояний: Готово -> Пусто -> Перезарядка -> Готово ----------

        [Test]
        public void T17_State_FireDuringReload_Blocked()
        {
            AmmoMagazine m = WithMagazine(1, 16);
            m.TryStartReload(0f);
            Assert.AreEqual(FireResult.Blocked, m.TryFire());
            Assert.AreEqual(1, m.Magazine, "во время перезарядки выстрелов нет");
        }

        [Test]
        public void T18_State_SituationC1_FromSpecExample()
        {
            // FR-WPN-03, пример и ситуация С1: магазин 1, запас 10, ЛКМ -> 0/10 -> через 0,9 с 2/8
            AmmoMagazine m = WithMagazine(1, 10);
            Assert.AreEqual(FireResult.Fired, m.TryFire());
            Assert.IsTrue(m.TryStartReload(3f), "автоперезарядка после опустошения");
            Assert.AreEqual(0, m.Magazine);
            Assert.AreEqual(10, m.Reserve);
            m.Tick(3f + Reload);
            Assert.AreEqual(2, m.Magazine);
            Assert.AreEqual(8, m.Reserve);
            Assert.AreEqual(FireResult.Fired, m.TryFire(), "после перезарядки снова Готово");
        }

        [Test]
        public void T19_State_CancelReload_NoTransferCanRestart()
        {
            // FR-WPN-07: смена оружия прерывает перезарядку
            AmmoMagazine m = WithMagazine(0, 16);
            m.TryStartReload(0f);
            m.CancelReload();
            Assert.IsFalse(m.IsReloading);
            Assert.AreEqual(0, m.Tick(10f), "прерванная перезарядка не переносит патроны");
            Assert.AreEqual(0, m.Magazine);
            Assert.AreEqual(16, m.Reserve);
            Assert.IsTrue(m.TryStartReload(10f), "после прерывания перезарядку можно начать снова");
        }

        // ---------- П5 (FR-WPN-04): подбор коробки -- классы и границы, предел 40 ----------

        [TestCase(16, 8, 8, 24,  TestName = "T20_AddAmmo_FitsCompletely")]
        [TestCase(36, 8, 4, 40,  TestName = "T21_AddAmmo_Partly_RemainderStaysInBox")]
        [TestCase(32, 8, 8, 40,  TestName = "T22_AddAmmo_Boundary_ExactlyToLimit")]
        [TestCase(33, 8, 7, 40,  TestName = "T23_AddAmmo_Boundary_OneOverLimit")]
        [TestCase(39, 1, 1, 40,  TestName = "T24_AddAmmo_Boundary_LastFreeSlot")]
        [TestCase(40, 8, 0, 40,  TestName = "T25_AddAmmo_FullReserve_TakesNothing")]
        [TestCase(50, 8, 0, 50,  TestName = "T26_AddAmmo_StartAboveLimit_TakesNothing")]
        [TestCase(0, int.MaxValue, 40, 40, TestName = "T27_AddAmmo_HugeBox_NoOverflow")]
        public void AddAmmo_Classes(int reserve, int amount, int expectedTaken, int expectedReserve)
        {
            AmmoMagazine m = Shotgun(reserve);
            Assert.AreEqual(expectedTaken, m.AddAmmo(amount), "взято патронов (остаток остаётся в коробке)");
            Assert.AreEqual(expectedReserve, m.Reserve);
        }

        // ---------- FR-WPN-04.1: ошибочные данные ----------

        [TestCase(0,  TestName = "T28_AddAmmo_NonPositive_ZeroBox")]
        [TestCase(-5, TestName = "T29_AddAmmo_NonPositive_NegativeBox")]
        public void AddAmmo_NonPositive_TakesNothing(int amount)
        {
            AmmoMagazine m = Shotgun(16);
            Assert.AreEqual(0, m.AddAmmo(amount));
            Assert.AreEqual(16, m.Reserve, "ошибочная коробка не уменьшает запас");
        }

        [Test]
        public void T30_AddAmmo_InfiniteReserve_TakesNothing()
        {
            AmmoMagazine m = Shotgun(0, infinite: true);
            Assert.AreEqual(0, m.AddAmmo(8));
        }

        [TestCase(0, 16, 40, 0.9f, "magazineSize", TestName = "T31_Ctor_MagazineZero_Error")]
        [TestCase(2, -1, 40, 0.9f, "reserve",      TestName = "T32_Ctor_NegativeReserve_Error")]
        [TestCase(2, 16, -1, 0.9f, "maxReserve",   TestName = "T33_Ctor_NegativeMaxReserve_Error")]
        [TestCase(2, 16, 40, -0.1f, "reloadTime",  TestName = "T34_Ctor_NegativeReloadTime_Error")]
        [TestCase(2, 16, 40, float.NaN, "reloadTime", TestName = "T35_Ctor_NaNReloadTime_Error")]
        public void Ctor_InvalidSettings_Throws(int size, int reserve, int maxReserve, float reload, string param)
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => new AmmoMagazine(size, reserve, maxReserve, reload));
            Assert.AreEqual(param, e.ParamName, "в сообщении журнала должно быть имя неверного параметра");
        }

        [Test]
        public void T36_Ctor_MinimalValidSettings_FullMagazine()
        {
            AmmoMagazine m = new AmmoMagazine(1, 0, 0, 0f);
            Assert.AreEqual(1, m.Magazine, "граница: магазин на 1 патрон, запас и время 0 допустимы");
            Assert.AreEqual(0, m.Reserve);
            Assert.IsFalse(m.IsReloading);
        }
    }
}
