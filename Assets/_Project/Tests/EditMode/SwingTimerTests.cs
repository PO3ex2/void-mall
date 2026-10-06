using NUnit.Framework;
using VoidMall.Items;

namespace VoidMall.Tests
{
    /// <summary>Перезарядка удара: частые клики не бьют чаще, чем удержание кнопки.</summary>
    public sealed class SwingTimerTests
    {
        private const float Interval = 0.5f, Dt = 0.02f;

        private static int Strikes(System.Func<int, bool> pressedAtFrame, int frames)
        {
            var timer = new SwingTimer();
            int strikes = 0;
            for (int i = 0; i < frames; i++)
                if (timer.Tick(pressedAtFrame(i), Dt, Interval))
                    strikes++;
            return strikes;
        }

        [Test]
        public void Spamming_IsNotFasterThanHolding()
        {
            const int frames = 250; // 5 с
            int held = Strikes(_ => true, frames);
            int spammed = Strikes(i => i % 4 < 2, frames); // нажатие 2 кадра, отпускание 2 кадра
            Assert.AreEqual(10, held, "удержание: удар раз в интервал");
            Assert.LessOrEqual(spammed, held, "клики не должны бить чаще удержания");
        }

        [Test]
        public void FirstPress_StrikesImmediately()
        {
            var timer = new SwingTimer();
            Assert.IsTrue(timer.Tick(true, Dt, Interval));
            Assert.IsFalse(timer.Tick(true, Dt, Interval));
        }

        [Test]
        public void AfterPause_StrikesImmediately()
        {
            var timer = new SwingTimer();
            timer.Tick(true, Dt, Interval);
            for (int i = 0; i < 30; i++)
                timer.Tick(false, Dt, Interval);
            Assert.IsTrue(timer.Tick(true, Dt, Interval), "перезарядка прошла, пока кнопка была отпущена");
        }
    }
}
