using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>
    /// Превью раскладки (проходы 1–2 генерации) в Texture2D: 1 пиксель = 1 м, цвет — раздел или тип плитки.
    /// Чанки не генерируются, поэтому карту можно строить для любой области мгновенно (World Generator в лобби).
    /// </summary>
    public static class LayoutPreview
    {
        private static readonly Color32 Gallery = new(205, 200, 185, 255);
        private static readonly Color32 Wall = new(40, 40, 44, 255);
        private static readonly Color32 Facade = new(70, 75, 85, 255);
        private static readonly Color32 Glass = new(140, 190, 210, 255);
        private static readonly Color32 Door = new(250, 250, 250, 255);
        private static readonly Color32 Core = new(110, 110, 115, 255);
        private static readonly Color32 Stairs = new(240, 200, 60, 255);
        private static readonly Color32 Elevator = new(60, 200, 240, 255);
        private static readonly Color32 Void = new(0, 0, 0, 255);
        private static readonly Color32 Railing = new(150, 210, 230, 255);
        private static readonly Color32 Pillar = new(90, 90, 90, 255);
        private static readonly Color32 Parking = new(85, 85, 90, 255);
        private static readonly Color32 Anomaly = new(200, 40, 200, 255);

        /// <summary>Нарисовать уровень level в квадрате radiusCells макроячеек вокруг centerCell (texture пересоздаётся при смене размера).</summary>
        public static Texture2D Render(uint seed, float anomalyFrequency, SectionTable sections, int2 centerCell, int radiusCells, int level, Texture2D texture = null)
        {
            int cells = radiusCells * 2 + 1;
            int size = cells * MallLayout.CellSize;
            if (texture == null || texture.width != size)
            {
                if (texture != null)
                    Object.Destroy(texture);
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "LayoutPreview" };
            }
            var pixels = new Color32[size * size];
            var plan = new MallLevelPlan(Allocator.Temp);
            try
            {
                for (int cz = 0; cz < cells; cz++)
                for (int cx = 0; cx < cells; cx++)
                {
                    int2 cell = centerCell + new int2(cx - radiusCells, cz - radiusCells);
                    var info = MallPlanner.GetCellInfo(seed, anomalyFrequency, cell);
                    MallPlanner.BuildLevel(seed, info, level, sections.Infos, ref plan);
                    float2 origin = MallLayout.CellOrigin(cell);
                    for (int z = 0; z < MallLayout.CellSize; z++)
                    for (int x = 0; x < MallLayout.CellSize; x++)
                    {
                        Color32 color = TileColor(plan, sections, x, z, level);
                        if (InAnomaly(info, level, origin + new float2(x + 0.5f, z + 0.5f)))
                            color = Color32.Lerp(color, Anomaly, 0.6f);
                        color = BiomeTint(color, info.Biome);
                        pixels[(cx * MallLayout.CellSize + x) + (cz * MallLayout.CellSize + z) * size] = color;
                    }
                }
            }
            finally
            {
                plan.Dispose();
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        /// <summary>
        /// Нарисовать уровень level в квадрате size×size метров с углом min (мировые x, z) — для карты загрузки, где
        /// область кратна колоннам, а не макроячейкам. Пиксели — в pixels (строка = z), текстура не нужна.
        /// </summary>
        public static void RenderArea(uint seed, float anomalyFrequency, SectionTable sections, int2 min, int size, int level, Color32[] pixels)
        {
            var plan = new MallLevelPlan(Allocator.Temp);
            try
            {
                int2 firstCell = MallLayout.CellOf(min.x, min.y);
                int2 lastCell = MallLayout.CellOf(min.x + size - 1, min.y + size - 1);
                for (int cz = firstCell.y; cz <= lastCell.y; cz++)
                for (int cx = firstCell.x; cx <= lastCell.x; cx++)
                {
                    int2 cell = new(cx, cz);
                    var info = MallPlanner.GetCellInfo(seed, anomalyFrequency, cell);
                    MallPlanner.BuildLevel(seed, info, level, sections.Infos, ref plan);
                    int2 origin = MallLayout.CellOrigin(cell);
                    for (int z = 0; z < MallLayout.CellSize; z++)
                    for (int x = 0; x < MallLayout.CellSize; x++)
                    {
                        int px = origin.x + x - min.x, pz = origin.y + z - min.y;
                        if (px < 0 || pz < 0 || px >= size || pz >= size)
                            continue;
                        Color32 color = TileColor(plan, sections, x, z, level);
                        if (InAnomaly(info, level, new float2(origin.x + x + 0.5f, origin.y + z + 0.5f)))
                            color = Color32.Lerp(color, Anomaly, 0.6f);
                        pixels[px + pz * size] = BiomeTint(color, info.Biome);
                    }
                }
            }
            finally
            {
                plan.Dispose();
            }
        }

        private static Color32 TileColor(in MallLevelPlan plan, SectionTable sections, int x, int z, int level)
        {
            Tile tile = plan.TileAt(x, z);
            byte roomId = plan.RoomIds[MallLevelPlan.Index(x, z)];
            switch (tile)
            {
                case Tile.Wall: return Wall;
                case Tile.Door:
                case Tile.FacadeDoor:
                case Tile.CoreDoor: return Door;
                case Tile.FacadeWall:
                case Tile.Shutter: return Facade;
                case Tile.FacadeGlass: return Glass;
                case Tile.CoreWall: return Core;
                case Tile.Pillar: return Pillar;
                case Tile.StairLaneA:
                case Tile.StairLaneB:
                case Tile.StairMid: return Stairs;
                case Tile.ElevatorVoid: return Elevator;
                case Tile.AtriumVoid:
                case Tile.RampHole: return MallLayout.RetailFloor(level) == 0 ? Gallery : Void;
                case Tile.Railing:
                case Tile.Barrier: return Railing;
            }
            if (roomId == 0)
                return MallLayout.Kind(level) == LevelKind.Parking ? Parking : Gallery;
            var room = plan.Rooms[roomId];
            if (room.Has(RoomFlags.Stairwell))
                return Stairs;
            Color color = sections.Defs[room.Section].MapColor;
            if (room.Has(RoomFlags.Dark))
                color *= 0.55f;
            color.a = 1f;
            return color;
        }

        private static readonly Color32 FieldTint = new(170, 120, 220, 255);
        private static readonly Color32 ForestTint = new(90, 110, 85, 255);

        /// <summary>Биом на карте: пустые разделы блёклые, поле аномалий сиреневое, место под лес — серо-зелёное.</summary>
        private static Color32 BiomeTint(Color32 color, MallBiome biome)
        {
            switch (biome)
            {
                case MallBiome.Empty:
                {
                    byte gray = (byte)((color.r + color.g + color.b) / 3);
                    var flat = new Color32(gray, gray, gray, 255);
                    return Color32.Lerp(color, flat, 0.65f);
                }
                case MallBiome.AnomalyField:
                    return Color32.Lerp(color, FieldTint, 0.35f);
                case MallBiome.Forest:
                    return Color32.Lerp(color, ForestTint, 0.5f);
                default:
                    return color;
            }
        }

        private static bool InAnomaly(in MallCellInfo info, int level, float2 world) =>
            PropPlacer.InAnomaly(info, level, world, 0f) || MallPlanner.InInverted(info, world) || MallPlanner.InFlooded(info, world);
    }
}
