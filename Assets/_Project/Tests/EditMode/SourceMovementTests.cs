using NUnit.Framework;
using UnityEngine;
using VoidMall.Player;

namespace VoidMall.Tests
{
    public sealed class SourceMovementTests
    {
        private const float Dt = 1f / 60f;

        [Test]
        public void Accelerate_ReachesWishSpeed_AndDoesNotExceedIt()
        {
            var velocity = Vector3.zero;
            for (int i = 0; i < 600; i++)
                SourceMovement.Accelerate(ref velocity, Vector3.forward, 3.8f, 10f, Dt);
            Assert.That(velocity.z, Is.EqualTo(3.8f).Within(1e-4f));
        }

        [Test]
        public void Friction_StopsPlayer_InFiniteTime()
        {
            var velocity = new Vector3(0f, 0f, 6f);
            int ticks = 0;
            while (velocity.sqrMagnitude > 0f && ticks < 600)
            {
                SourceMovement.Friction(ref velocity, Dt, 5f, 1.9f);
                ticks++;
            }
            Assert.That(velocity, Is.EqualTo(Vector3.zero));
            Assert.That(ticks, Is.LessThan(120), "Stopping from sprint should take less than 2 seconds.");
        }

        [Test]
        public void AirAccelerate_CapsGainAlongWishDirection()
        {
            var velocity = Vector3.zero;
            for (int i = 0; i < 600; i++)
                SourceMovement.AirAccelerate(ref velocity, Vector3.forward, 6f, 10f, 0.6f, Dt);
            Assert.That(velocity.z, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void AirStrafe_PerpendicularWish_IncreasesSpeed()
        {
            // Классический air-strafe: желаемое направление перпендикулярно скорости — скорость растёт.
            var velocity = new Vector3(0f, 0f, 5f);
            float before = velocity.magnitude;
            SourceMovement.AirAccelerate(ref velocity, Vector3.right, 6f, 10f, 0.6f, Dt);
            Assert.That(velocity.magnitude, Is.GreaterThan(before));
        }

        [Test]
        public void ClipVelocity_RemovesComponentIntoWall()
        {
            var clipped = SourceMovement.ClipVelocity(new Vector3(3f, 0f, -2f), Vector3.forward);
            Assert.That(clipped.z, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(clipped.x, Is.EqualTo(3f).Within(1e-5f));
        }

        [Test]
        public void ClipVelocity_KeepsVelocityMovingAway()
        {
            var velocity = new Vector3(0f, 0f, 2f);
            Assert.That(SourceMovement.ClipVelocity(velocity, Vector3.forward), Is.EqualTo(velocity));
        }
    }
}
