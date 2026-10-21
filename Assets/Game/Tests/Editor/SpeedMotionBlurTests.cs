using NUnit.Framework;

namespace BeerAndDragon.Tests
{
    // Сила моушен блюра от скорости: порог 9 м/с (бег 8 -- без размытия), максимум 0,6 с 25 м/с
    public class SpeedMotionBlurTests
    {
        private static float At(float speed) => SpeedMotionBlur.IntensityForSpeed(speed, 9f, 25f, 0.6f);

        [TestCase(0f, 0f)]
        [TestCase(8f, 0f)]       // бег -- чётко
        [TestCase(9f, 0f)]       // граница: ещё без размытия
        [TestCase(17f, 0.3f)]    // середина
        [TestCase(25f, 0.6f)]    // граница: максимум
        [TestCase(40f, 0.6f)]    // быстрее -- не растёт
        public void Intensity_BySpeed(float speed, float expected)
        {
            Assert.AreEqual(expected, At(speed), 0.0001f);
        }

        [Test]
        public void Intensity_InvalidRange_StepAtStart()
        {
            Assert.AreEqual(0f, SpeedMotionBlur.IntensityForSpeed(5f, 10f, 10f, 0.6f));
            Assert.AreEqual(0.6f, SpeedMotionBlur.IntensityForSpeed(10f, 10f, 10f, 0.6f));
        }
    }
}
