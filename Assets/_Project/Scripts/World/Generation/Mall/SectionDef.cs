using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Как раздел расставляет мебель-заглушки в комнате.</summary>
    public enum SectionLayout : byte
    {
        Empty,
        Rows,
        Islands,
        Perimeter,
        Hall,
        Warehouse,
        Clothing,
        Showroom,
        Tech,
    }

    /// <summary>
    /// Раздел магазина: отделка (материалы блоков), расстановка и (позже) таблицы лута.
    /// Генератор выбирает раздел комнаты по ячейке Вороного с учётом весов из WorldSettings.
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/World/Section", fileName = "Section")]
    public sealed class SectionDef : ContentDef
    {
        public string DisplayName;

        [Tooltip("Базовый вес выбора (WorldSettings может переопределить).")]
        [Min(0)] public float Weight = 1f;

        [Tooltip("Цвет на превью раскладки.")]
        public Color MapColor = Color.gray;

        public SectionLayout Layout = SectionLayout.Rows;

        [Header("Отделка")]
        public MaterialDef Floor;
        public MaterialDef Wall;
        public MaterialDef Facade;
        public MaterialDef Ceiling;

        [Header("Где встречается")]
        [Tooltip("Торговые этажи 1–5 (биты 0–4).")]
        public int RetailFloorMask = 0b11111;

        [Tooltip("Служебный раздел: ставится в комнаты без витрины и на технический этаж, в случайный выбор не входит.")]
        public bool Service;
    }

    /// <summary>Сжатое описание раздела для Burst.</summary>
    public struct SectionInfo
    {
        public ushort Floor, Wall, Facade, Ceiling;
        public SectionLayout Layout;
        public byte RetailFloorMask;
        public bool Service;
        public float Weight;
    }

    /// <summary>Таблица разделов мира: NativeArray для джобов + управляемые описания.</summary>
    public sealed class SectionTable : IDisposable
    {
        public NativeArray<SectionInfo> Infos { get; private set; }
        public IReadOnlyList<SectionDef> Defs => _defs;

        private readonly List<SectionDef> _defs = new();

        public SectionTable(IReadOnlyList<SectionDef> sections, Func<SectionDef, float> weight, MaterialRegistry materials)
        {
            foreach (var section in sections)
                if (section != null && !_defs.Contains(section))
                    _defs.Add(section);
            if (_defs.Count == 0)
                throw new ArgumentException("World has no sections.");
            if (_defs.Count > 250)
                throw new ArgumentException("Too many sections (max 250).");

            var infos = new NativeArray<SectionInfo>(_defs.Count, Allocator.Persistent);
            for (int i = 0; i < _defs.Count; i++)
            {
                var def = _defs[i];
                infos[i] = new SectionInfo
                {
                    Floor = Resolve(def.Floor, materials, "block.carpet"),
                    Wall = Resolve(def.Wall, materials, "block.drywall"),
                    Facade = Resolve(def.Facade, materials, "block.facade_dark"),
                    Ceiling = Resolve(def.Ceiling, materials, "block.ceiling_tile"),
                    Layout = def.Layout,
                    RetailFloorMask = (byte)(def.RetailFloorMask & 0x1F),
                    Service = def.Service,
                    Weight = Mathf.Max(0f, weight(def)),
                };
            }
            Infos = infos;
        }

        private static ushort Resolve(MaterialDef def, MaterialRegistry materials, string fallback)
        {
            if (def != null && materials.TryGetIndex(def.Id, out ushort index))
                return index;
            return materials.TryGetIndex(fallback, out ushort fallbackIndex) ? fallbackIndex : (ushort)0;
        }

        public void Dispose()
        {
            if (Infos.IsCreated)
                Infos.Dispose();
        }
    }
}
