using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VoidMall.Core;
using VoidMall.Player;

namespace VoidMall.Tests
{
    /// <summary>Мотор на настоящей физике: ступени, блоки, лазы, потолки, склоны.</summary>
    public sealed class PlayerMotorTests
    {
        private sealed class ScriptedInput : IPlayerInputSource
        {
            public PlayerInputFrame Frame;
            public PlayerButtons PressOnce;

            public PlayerInputFrame ConsumeFrame()
            {
                var frame = Frame;
                frame.Pressed |= PressOnce;
                PressOnce = PlayerButtons.None;
                return frame;
            }
        }

        private readonly List<Object> _spawned = new();
        private MovementSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<MovementSettings>();
            _spawned.Add(_settings);
            Box(new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 40f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _spawned)
                if (obj != null)
                    Object.Destroy(obj);
            _spawned.Clear();
        }

        [UnityTest]
        public IEnumerator SettlesOnFloor()
        {
            var (motor, _) = SpawnPlayer(new Vector3(0f, 0.5f, 0f));
            yield return Ticks(60);
            Assert.That(motor.State.IsGrounded, Is.True);
            Assert.That(motor.State.Position.y, Is.EqualTo(0f).Within(0.03f));
        }

        [UnityTest]
        public IEnumerator WalksUpHalfMeterStep()
        {
            Box(new Vector3(0f, 0.25f, 3f), new Vector3(4f, 0.5f, 2f));
            var (motor, input) = SpawnPlayer(Vector3.zero);
            input.Frame.Move = Vector2.up;
            yield return Ticks(60);
            Assert.That(motor.State.Position.y, Is.EqualTo(0.5f).Within(0.05f), "Должен подняться на ступеньку 0.5 м шагом.");
        }

        [UnityTest]
        public IEnumerator ClimbsStairs_Monotonically()
        {
            // Как на полигоне: 10 ступеней по 0.25 м, проступь 0.35 м, площадка 2.5 м.
            const float rise = 0.25f, tread = 0.35f;
            for (int i = 0; i < 10; i++)
            {
                float h = rise * (i + 1);
                Box(new Vector3(0f, h * 0.5f, 2f + tread * (i + 0.5f)), new Vector3(4f, h, tread));
            }
            Box(new Vector3(0f, 1.25f, 2f + tread * 10f + 2f), new Vector3(4f, 2.5f, 4f));

            var (motor, input) = SpawnPlayer(Vector3.zero);
            input.Frame.Move = Vector2.up;
            var trace = new System.Text.StringBuilder();
            float previousY = motor.State.Position.y;
            float worstDrop = 0f;
            for (int i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                float y = motor.State.Position.y;
                worstDrop = Mathf.Max(worstDrop, previousY - y);
                previousY = y;
                trace.Append($"[{i}] y={y:0.000} z={motor.State.Position.z:0.00} g={(motor.State.IsGrounded ? 1 : 0)}\n");
            }
            Assert.That(worstDrop, Is.LessThan(0.02f), "На подъёме по лестнице высота не должна проседать.\n" + trace);
            Assert.That(motor.State.Position.y, Is.EqualTo(2.5f).Within(0.05f), trace.ToString());
            // Лестница не должна заметно тормозить: за 2.5 с при беге 3.8 м/с проходим ≥ 7.5 м.
            Assert.That(motor.State.Position.z, Is.GreaterThan(7.5f), trace.ToString());
        }

        [UnityTest]
        public IEnumerator StandsOnFortyFiveDegreeSlope()
        {
            Vector3 surfacePoint = Ramp(45f, 7f, 2f);
            var (motor, _) = SpawnPlayer(surfacePoint + Vector3.up * 0.6f);
            yield return Ticks(60);
            int groundedTicks = 0;
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
                if (motor.State.IsGrounded)
                    groundedTicks++;
            }
            Assert.That(groundedTicks, Is.EqualTo(60), "Склон ровно 45° (= MaxSlopeAngle) должен стабильно держать игрока.");
        }

        [UnityTest]
        public IEnumerator BlockedByOneMeterBlock_WithoutJump()
        {
            Box(new Vector3(0f, 0.5f, 3f), new Vector3(4f, 1f, 2f));
            var (motor, input) = SpawnPlayer(Vector3.zero);
            input.Frame.Move = Vector2.up;
            yield return Ticks(90);
            Assert.That(motor.State.Position.y, Is.LessThan(0.1f));
            Assert.That(motor.State.Position.z, Is.LessThan(2f - _settings.Radius + 0.05f));
        }

        [UnityTest]
        public IEnumerator JumpsOntoOneMeterBlock()
        {
            Box(new Vector3(0f, 0.5f, 3f), new Vector3(4f, 1f, 2f));
            var (motor, input) = SpawnPlayer(new Vector3(0f, 0f, -0.5f));
            input.Frame.Move = Vector2.up;
            yield return Ticks(15);
            input.PressOnce = PlayerButtons.Jump;
            yield return Ticks(40);
            // Блок длиной 2 м: отпускаем «вперёд», чтобы не сбежать с дальнего края.
            input.Frame.Move = Vector2.zero;
            yield return Ticks(40);
            Assert.That(motor.State.IsGrounded, Is.True);
            Assert.That(motor.State.Position.y, Is.EqualTo(1f).Within(0.05f), "Прыжок должен доставать до блока 1 м.");
        }

        [UnityTest]
        public IEnumerator CrawlsThroughOneBlockTunnel_OnlyWhenCrouched()
        {
            Box(new Vector3(0f, 1.25f, 4f), new Vector3(4f, 0.5f, 4f));

            var (standing, standingInput) = SpawnPlayer(new Vector3(-0.8f, 0f, 0f));
            standingInput.Frame.Move = Vector2.up;
            var (crouching, crouchingInput) = SpawnPlayer(new Vector3(0.8f, 0f, 0f));
            crouchingInput.Frame.Move = Vector2.up;
            crouchingInput.Frame.Held = PlayerButtons.Crouch;

            yield return Ticks(300);
            Assert.That(standing.State.Position.z, Is.LessThan(2f), "Стоя в лаз высотой 1 м не пролезть.");
            Assert.That(crouching.State.Position.z, Is.GreaterThan(6.5f), "Пригнувшись лаз проходится насквозь.");
        }

        [UnityTest]
        public IEnumerator CannotStandUpUnderLowCeiling()
        {
            Box(new Vector3(0f, 1.75f, 0f), new Vector3(4f, 0.5f, 4f));
            var (motor, input) = SpawnPlayer(Vector3.zero);
            input.Frame.Held = PlayerButtons.Crouch;
            yield return Ticks(10);
            input.Frame.Held = PlayerButtons.None;
            yield return Ticks(30);
            Assert.That(motor.State.IsCrouched, Is.True);
        }

        [UnityTest]
        public IEnumerator WalksUpThirtyDegreeSlope()
        {
            Ramp(30f, 7f, 2f);
            var (motor, input) = SpawnPlayer(new Vector3(0f, 0f, 1f));
            input.Frame.Move = Vector2.up;
            yield return Ticks(150);
            Assert.That(motor.State.Position.y, Is.GreaterThan(2.5f));
        }

        [UnityTest]
        public IEnumerator SlidesDownSixtyDegreeSlope()
        {
            Vector3 surfacePoint = Ramp(60f, 7f, 2f);
            var (motor, _) = SpawnPlayer(surfacePoint + Vector3.up * 0.6f);
            float startY = motor.State.Position.y;
            yield return Ticks(180);
            Assert.That(motor.State.Position.y, Is.LessThan(startY - 1.5f), "Со склона 60° игрок должен соскальзывать.");
        }

        [UnityTest]
        public IEnumerator LandingReportsImpactSpeed()
        {
            var (motor, _) = SpawnPlayer(new Vector3(0f, 6f, 0f));
            float landed = -1f;
            motor.Landed += speed => landed = speed;
            yield return Ticks(120);
            float expected = Mathf.Sqrt(2f * _settings.Gravity * 6f);
            Assert.That(landed, Is.EqualTo(expected).Within(0.6f));
        }

        // ── Утилиты ──

        private (PlayerMotor, ScriptedInput) SpawnPlayer(Vector3 feet)
        {
            var go = new GameObject("TestPlayer") { layer = GameLayers.Player };
            go.SetActive(false);
            go.transform.position = feet;
            go.AddComponent<PlayerLook>();
            var motor = go.AddComponent<PlayerMotor>();
            SetPrivate(motor, "_settings", _settings);
            SetPrivate(motor, "_isLocalPlayer", false);
            go.SetActive(true);

            var input = new ScriptedInput();
            motor.SetInputSource(input);
            _spawned.Add(go);
            Physics.SyncTransforms();
            return (motor, input);
        }

        private GameObject Box(Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.layer = GameLayers.World;
            go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
            go.transform.localScale = size;
            _spawned.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>Пандус, поднимающийся к +Z от z = startZ. Возвращает точку на поверхности посередине.</summary>
        private Vector3 Ramp(float angle, float length, float startZ)
        {
            const float thickness = 0.4f;
            float rad = angle * Mathf.Deg2Rad;
            var center = new Vector3(
                0f,
                Mathf.Sin(rad) * length * 0.5f - Mathf.Cos(rad) * thickness * 0.5f,
                startZ + Mathf.Cos(rad) * length * 0.5f + Mathf.Sin(rad) * thickness * 0.5f);
            Box(center, new Vector3(4f, thickness, length), Quaternion.Euler(-angle, 0f, 0f));
            return new Vector3(0f, Mathf.Sin(rad) * length * 0.5f, startZ + Mathf.Cos(rad) * length * 0.5f);
        }

        private static IEnumerator Ticks(int count)
        {
            for (int i = 0; i < count; i++)
                yield return new WaitForFixedUpdate();
        }

        private static void SetPrivate(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
}
