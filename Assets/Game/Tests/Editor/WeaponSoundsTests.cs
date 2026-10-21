using NUnit.Framework;
using UnityEngine;

namespace BeerAndDragon.Tests
{
    // Сгенерированные звуки дробовика: громкие, без клиппинга, затухают; в перезарядке слышны все четыре события
    public class WeaponSoundsTests
    {
        private static float Peak(float[] d, int from, int to)
        {
            float p = 0f;
            for (int i = Mathf.Max(0, from); i < Mathf.Min(d.Length, to); i++) p = Mathf.Max(p, Mathf.Abs(d[i]));
            return p;
        }

        private static int At(float seconds) => Mathf.RoundToInt(seconds * WeaponSounds.SampleRate);

        [Test]
        public void Shot_LoudAtStart_DecaysToSilence()
        {
            float[] d = WeaponSounds.SynthShot(100);
            Assert.AreEqual(At(WeaponSounds.ShotLength), d.Length);
            Assert.AreEqual(0.9f, Peak(d, 0, d.Length), 0.001f, "нормировка к 0,9 -- без клиппинга");
            Assert.Greater(Peak(d, 0, At(0.05f)), 0.5f, "в первые 50 мс -- громкий хлопок");
            Assert.Less(Peak(d, At(0.55f), d.Length), 0.1f, "к концу звук затих");
            Assert.Less(Mathf.Abs(d[0]), 0.05f, "без щелчка в первом отсчёте");
        }

        [Test]
        public void Shot_Variants_Differ_SameSeed_Same()
        {
            Assert.AreNotEqual(WeaponSounds.SynthShot(100), WeaponSounds.SynthShot(101));
            Assert.AreEqual(WeaponSounds.SynthShot(100), WeaponSounds.SynthShot(100), "звук воспроизводим");
        }

        [Test]
        public void Reload_EventsAudible_QuietBetween()
        {
            float[] d = WeaponSounds.SynthReload();
            Assert.AreEqual(At(WeaponSounds.ReloadLength), d.Length);
            Assert.LessOrEqual(Peak(d, 0, d.Length), 0.9001f);
            foreach (float e in WeaponSounds.ReloadEvents)
                Assert.Greater(Peak(d, At(e), At(e + 0.03f)), 0.2f, $"событие в {e} с слышно");
            Assert.Less(Peak(d, At(0.2f), At(0.27f)), 0.05f, "между щелчком и первым патроном -- тишина");
        }

        [Test]
        public void Clips_HaveExpectedFormat()
        {
            AudioClip clip = WeaponSounds.ToClip("t", WeaponSounds.SynthShot(1));
            Assert.AreEqual(1, clip.channels);
            Assert.AreEqual(WeaponSounds.SampleRate, clip.frequency);
            Assert.AreEqual(WeaponSounds.ShotLength, clip.length, 0.001f);
            Object.DestroyImmediate(clip);
        }
    }
}
