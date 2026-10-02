using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Добыт материал (заглушка до инвентаря: логируется в консоль).</summary>
    public struct MaterialMinedEvent
    {
        public ushort Material;

        /// <summary>Блоки — штуки, гладкие материалы — м³ (1 полностью плотная клетка = 1 м³).</summary>
        public float Amount;

        public Vector3 Position;
    }

    /// <summary>Применение кисти бура к полю плотности (только на авторитете).</summary>
    public static class SmoothBrush
    {
        /// <summary>
        /// Максимальная плотность, которую бур может нарастить в клетке с сущностью: поверхность заходит в клетку
        /// не дальше ~0.2 м, и мотор выталкивает игрока, а не оставляет его внутри замкнутой геометрии.
        /// </summary>
        private const int EntityCellCap = 64;

        private const int EntityMask = (1 << GameLayers.Player) | (1 << GameLayers.Props) | (1 << GameLayers.Enemy);

        private static bool OverlapsEntity(int3 cell) =>
            Physics.CheckBox(new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f), Vector3.one * 0.48f,
                Quaternion.identity, EntityMask, QueryTriggerInteraction.Ignore);

        /// <param name="becameEmpty">Клетки, ставшие пустыми (для проверки целостности).</param>
        /// <param name="mined">Сколько материала срезано, м³ по материалам.</param>
        public static void Apply(ChunkManager world, in SmoothBrushCommand command, List<int3> becameEmpty, Dictionary<ushort, float> mined)
        {
            float radius = math.clamp(command.Radius, 0.25f, 4f);
            float strength = math.clamp(command.Strength, 0.01f, 1f);
            int3 min = (int3)math.floor(command.Center - radius - 1f);
            int3 max = (int3)math.floor(command.Center + radius + 1f);
            var infos = world.Materials.Infos;

            using (world.BeginBatch())
            {
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var p = new int3(x, y, z);
                    if (!WorldConstants.IsInsideWorldHeight(y) || !world.IsLoaded(p))
                        continue;

                    Cell cell = world.GetCell(p);
                    bool isAir = cell.IsAir;
                    if (!isAir)
                    {
                        var info = infos[cell.Material];
                        if (info.Kind != MaterialKind.Smooth || (info.Flags & MaterialFlags.Unbreakable) != 0)
                            continue; // блоки и неразрушимое бур не трогает
                    }

                    byte oldDensity = isAir ? (byte)math.min((int)cell.Value, Density.Iso - 1) : cell.Value;
                    float field = Density.ToField(oldDensity);
                    float brush = Density.SphereField(new float3(p) + 0.5f, command.Center, radius);
                    float newField;
                    if (command.Add)
                    {
                        float target = math.max(field, brush);
                        if (target <= field)
                            continue;
                        newField = math.min(target, field + strength * 2f);
                    }
                    else
                    {
                        float target = math.min(field, -brush);
                        if (target >= field)
                            continue;
                        // Твёрдые материалы срезаются медленнее: прочность 1000 — базовая скорость.
                        float hardness = isAir ? 1000f : math.max(infos[cell.Material].Hardness, 1);
                        newField = math.max(target, field - strength * 2f * (1000f / hardness));
                    }

                    byte density = Density.FromField(newField);
                    if (command.Add && density > EntityCellCap && OverlapsEntity(p))
                        density = (byte)math.max((int)oldDensity, EntityCellCap); // не замуровываем игрока, пропы и врагов
                    if (density == oldDensity)
                        continue;

                    ushort material = 0;
                    if (Density.IsSolid(density))
                        material = isAir ? command.Material : cell.Material;
                    if (material != 0 && infos[material].Kind != MaterialKind.Smooth)
                        continue;
                    if (material == 0)
                        density = (byte)math.min((int)density, Density.Iso - 1); // воздух твёрдым не бывает
                    if (density == oldDensity)
                        continue;

                    if (!isAir && density < oldDensity)
                    {
                        mined.TryGetValue(cell.Material, out float amount);
                        mined[cell.Material] = amount + (oldDensity - density) / 255f;
                    }

                    world.SetCell(p, new Cell(material, density));
                    if (!isAir && material == 0)
                        becameEmpty.Add(p);
                }
            }
        }
    }
}
