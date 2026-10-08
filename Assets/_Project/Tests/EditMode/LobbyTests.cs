using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using VoidMall.Game;

namespace VoidMall.Tests
{
    /// <summary>Этап 10: геометрия портала, ASCII-заставка, список клавиш для переназначения.</summary>
    public sealed class LobbyTests
    {
        private static readonly Vector3 PortalPos = new(10f, 0f, 5f);
        private static readonly Quaternion PortalRot = Quaternion.Euler(0f, 90f, 0f); // проход — вдоль +X

        [Test]
        public void Crossed_OnlyForwardThroughOpening()
        {
            // Проём 2×3: вперёд через середину — да; назад — нет; мимо проёма — нет; не дошёл — нет.
            Assert.IsTrue(PortalMath.Crossed(new Vector3(9.9f, 1f, 5f), new Vector3(10.1f, 1f, 5f), PortalPos, PortalRot, 2f, 3f));
            Assert.IsFalse(PortalMath.Crossed(new Vector3(10.1f, 1f, 5f), new Vector3(9.9f, 1f, 5f), PortalPos, PortalRot, 2f, 3f));
            Assert.IsFalse(PortalMath.Crossed(new Vector3(9.9f, 1f, 7f), new Vector3(10.1f, 1f, 7f), PortalPos, PortalRot, 2f, 3f));
            Assert.IsFalse(PortalMath.Crossed(new Vector3(9.9f, 3.5f, 5f), new Vector3(10.1f, 3.5f, 5f), PortalPos, PortalRot, 2f, 3f));
            Assert.IsFalse(PortalMath.Crossed(new Vector3(9.5f, 1f, 5f), new Vector3(9.9f, 1f, 5f), PortalPos, PortalRot, 2f, 3f));
        }

        [Test]
        public void Map_KeepsPoseRelativeToPortal()
        {
            var toPos = new Vector3(-3f, 1000f, 2f);
            var toRot = Quaternion.Euler(0f, 180f, 0f);
            var point = new Vector3(9f, 1.6f, 5.5f); // метр до проёма, полметра вбок
            var mapped = PortalMath.MapPoint(point, PortalPos, PortalRot, toPos, toRot);
            var before = PortalMath.Local(point, PortalPos, PortalRot);
            var after = PortalMath.Local(mapped, toPos, toRot);
            Assert.That(Vector3.Distance(before, after), Is.LessThan(1e-4f));

            // Смотрел в проход — смотрит в проход выхода.
            var look = PortalMath.MapRotation(PortalRot, PortalRot, toRot);
            Assert.That(Quaternion.Angle(look, toRot), Is.LessThan(0.01f));
            var direction = PortalMath.MapDirection(PortalRot * Vector3.forward, PortalRot, toRot);
            Assert.That(Vector3.Distance(direction, toRot * Vector3.forward), Is.LessThan(1e-4f));

            // Туда и обратно — та же точка.
            var back = PortalMath.MapPoint(mapped, toPos, toRot, PortalPos, PortalRot);
            Assert.That(Vector3.Distance(back, point), Is.LessThan(1e-4f));
        }

        [Test]
        public void Ascii_MapsBrightnessAndFlipsRows()
        {
            // 4×4: верхняя половина (в Unity — строки с большим y) белая, нижняя прозрачная.
            var pixels = new Color32[16];
            for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                pixels[x + y * 4] = y >= 2 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            var lines = AsciiArt.FromPixels(pixels, 4, 4, 4);
            Assert.AreEqual(2, lines.Length, "символ вдвое выше своей ширины — строк вдвое меньше");
            Assert.AreEqual("@@@@", lines[0]);
            Assert.AreEqual(string.Empty, lines[1], "пустые строки обрезаются");
        }

        [Test]
        public void KeyRows_SkipCompositeAndOtherDevices()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            try
            {
                var map = asset.AddActionMap("Player");
                var move = map.AddAction("Move");
                move.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w", "Keyboard&Mouse")
                    .With("Down", "<Keyboard>/s", "Keyboard&Mouse");
                var jump = map.AddAction("Jump");
                jump.AddBinding("<Keyboard>/space", groups: "Keyboard&Mouse");
                jump.AddBinding("<Gamepad>/buttonSouth", groups: "Gamepad");
                asset.AddActionMap("Debug").AddAction("Noclip").AddBinding("<Keyboard>/v", groups: "Keyboard&Mouse");

                var rows = KeyBindings.Rows(asset, "Keyboard&Mouse");
                var captions = rows.ConvertAll(r => r.caption);
                CollectionAssert.AreEqual(new[] { "Движение: вперёд", "Движение: назад", "Прыжок" }, captions);
                Assert.AreEqual(1, rows[0].index, "индекс привязки — в списке действия (0 — сама составная)");
                Assert.AreEqual(0, rows[2].index);
                Assert.AreEqual("Space", KeyBindings.Display(jump, 0), "имя клавиши — по месту на клавиатуре, без устройства");
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
