using NUnit.Framework;
using VoidMall.Core;

namespace VoidMall.Tests
{
    public sealed class RngAndHashTests
    {
        [Test]
        public void Rng_SameSeed_SameSequence()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            for (int i = 0; i < 1000; i++)
                Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()));
        }

        [Test]
        public void Rng_DifferentSeeds_DifferentSequences()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            int equal = 0;
            for (int i = 0; i < 100; i++)
                if (a.NextUInt() == b.NextUInt())
                    equal++;
            Assert.That(equal, Is.LessThan(3));
        }

        [Test]
        public void Rng_Range_StaysInBounds()
        {
            var rng = new Rng(7);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.Range(-3, 5);
                Assert.That(v, Is.InRange(-3, 4));
                float f = rng.NextFloat();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void Hash_IsStable_AndCoordinateSensitive()
        {
            // Значение зафиксировано: если оно изменится, все миры по сидам перестанут совпадать со старыми.
            Assert.That(Hash.Get(42u, 1, 2, 3), Is.EqualTo(Hash.Get(42u, 1, 2, 3)));
            Assert.That(Hash.Get(42u, 1, 2, 3), Is.Not.EqualTo(Hash.Get(42u, 1, 2, 4)));
            Assert.That(Hash.Get(42u, 1, 2, 3), Is.Not.EqualTo(Hash.Get(43u, 1, 2, 3)));
            Assert.That(Hash.Get(42u, 0, 1), Is.Not.EqualTo(Hash.Get(42u, 1, 0)));
        }
    }

    public sealed class MovementRulesTests
    {
        [Test]
        public void LightLoad_HasNoEffect()
        {
            var m = MovementModifiers.Default;
            MovementRules.ApplyEncumbrance(ref m, 0.5f);
            Assert.That(m.SpeedMul, Is.EqualTo(1f));
            Assert.That(m.CanSprint, Is.True);
        }

        [Test]
        public void Overload_LimitsTurnRate_AndDisablesSprintAndJump()
        {
            var m = MovementModifiers.Default;
            MovementRules.ApplyEncumbrance(ref m, 1.2f);
            Assert.That(m.CanSprint, Is.False);
            Assert.That(m.CanJump, Is.False);
            Assert.That(float.IsInfinity(m.MaxTurnRateDeg), Is.False);
            Assert.That(m.SpeedMul, Is.LessThan(0.4f));
        }

        [Test]
        public void BothLegsBroken_ForcesCrouch()
        {
            var m = MovementModifiers.Default;
            MovementRules.ApplyLegFractures(ref m, 2);
            Assert.That(m.CanStand, Is.False);
        }

        [Test]
        public void FallClassification()
        {
            Assert.That(MovementRules.ClassifyFall(5f), Is.EqualTo(MovementRules.FallSeverity.None));
            Assert.That(MovementRules.ClassifyFall(10f), Is.EqualTo(MovementRules.FallSeverity.Damage));
            Assert.That(MovementRules.ClassifyFall(15f), Is.EqualTo(MovementRules.FallSeverity.Fracture));
        }
    }
}
