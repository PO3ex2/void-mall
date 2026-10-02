using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoidMall.World;

namespace VoidMall.Tests
{
    public sealed class EmbeddedEscapeTests
    {
        private const ushort Block = 1;
        private const ushort Smooth = 2;
        private const float Height = 1.8f;
        private const float Radius = 0.35f;

        private sealed class FakeWorld : IVoxelAccess
        {
            public readonly Dictionary<int3, Cell> Cells = new();

            public bool IsLoaded(int3 cell) => true;
            public Cell GetCell(int3 cell) => Cells.TryGetValue(cell, out var value) ? value : Cell.Air;

            public MaterialInfo GetInfo(ushort material) => new()
            {
                Kind = material == Smooth ? MaterialKind.Smooth : MaterialKind.Block,
                Flags = MaterialFlags.Solid | MaterialFlags.Opaque,
            };

            public void FillBox(int3 min, int3 max, Cell cell)
            {
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                for (int x = min.x; x <= max.x; x++)
                    Cells[new int3(x, y, z)] = cell;
            }
        }

        [Test]
        public void StandingOnFloor_IsNotEmbedded()
        {
            var world = new FakeWorld();
            world.FillBox(new int3(-5, 0, -5), new int3(5, 17, 5), new Cell(Block));
            Assert.That(EmbeddedEscape.IsEmbedded(world, new float3(0.5f, 18.02f, 0.5f), Height, Radius), Is.False);
        }

        [Test]
        public void StandingOnSmoothSurface_IsNotEmbedded()
        {
            var world = new FakeWorld();
            world.FillBox(new int3(-5, 0, -5), new int3(5, 16, 5), new Cell(Smooth, 255));
            // Верхний слой массива частично плотный — поверхность чуть выше центра клеток.
            world.FillBox(new int3(-5, 17, -5), new int3(5, 17, 5), new Cell(Smooth, 180));
            Assert.That(EmbeddedEscape.IsEmbedded(world, new float3(0.5f, 17.95f, 0.5f), Height, Radius), Is.False);
        }

        [Test]
        public void BuriedInFloor_EscapesUpOntoSurface()
        {
            var world = new FakeWorld();
            world.FillBox(new int3(-5, 0, -5), new int3(5, 17, 5), new Cell(Block));
            var feet = new float3(0.5f, 14f, 0.5f);
            Assert.That(EmbeddedEscape.TryFindEscape(world, feet, Height, Radius, out float3 free), Is.True);
            Assert.That(free.y, Is.InRange(18f, 18.6f), "Выталкивает наверх, на поверхность пола.");
            Assert.That(free.xz, Is.EqualTo(feet.xz));
        }

        [Test]
        public void EnclosedInSmoothBlob_EscapesToNearestFreeSpot()
        {
            var world = new FakeWorld();
            world.FillBox(new int3(-5, 0, -5), new int3(5, 9, 5), new Cell(Block));
            world.FillBox(new int3(-2, 10, -2), new int3(2, 14, 2), new Cell(Smooth, 255));
            var feet = new float3(0.5f, 10.2f, 0.5f);
            Assert.That(EmbeddedEscape.TryFindEscape(world, feet, Height, Radius, out float3 free), Is.True);
            Assert.That(EmbeddedEscape.IsFree(world, free, Height, Radius), Is.True);
            // Вбок до края кома (~3 м) ближе, чем наверх (~4.8 м): выходим рядом, а не на макушку.
            Assert.That(math.distance(free, feet), Is.LessThan(4f));
        }

        [Test]
        public void BlockedRoomAroundPlayer_ExitsIntoRoomNotOntoRoof()
        {
            var world = new FakeWorld();
            world.FillBox(new int3(-10, 0, -10), new int3(10, 17, 10), new Cell(Block));   // пол
            world.FillBox(new int3(-10, 23, -10), new int3(10, 23, 10), new Cell(Block));  // потолок
            world.FillBox(new int3(-2, 18, -2), new int3(2, 21, 2), new Cell(Block));     // залили всё вокруг игрока
            Assert.That(EmbeddedEscape.TryFindEscape(world, new float3(0.5f, 18.02f, 0.5f), Height, Radius, out float3 free), Is.True);
            Assert.That(free.y, Is.LessThan(23f), "Остаёмся в комнате под потолком.");
        }

        [Test]
        public void SealedFromAbove_EscapesSideways()
        {
            var world = new FakeWorld();
            world.FillBox(new int3(-10, 0, -10), new int3(10, 9, 10), new Cell(Block));
            // Колонна-«пробка» до потолка мира: вверх не выбраться, рядом — свободно.
            world.FillBox(new int3(0, 10, 0), new int3(0, WorldConstants.WorldHeight - 1, 0), new Cell(Block));
            Assert.That(EmbeddedEscape.TryFindEscape(world, new float3(0.5f, 10f, 0.5f), Height, Radius, out float3 free), Is.True);
            Assert.That(EmbeddedEscape.IsFree(world, free, Height, Radius), Is.True);
            Assert.That(EmbeddedEscape.IsEmbedded(world, free, Height, Radius), Is.False);
            Assert.That(free.y, Is.LessThan(12f), "Вверх через пробку до потолка мира не лезем — выходим рядом.");
        }
    }
}
