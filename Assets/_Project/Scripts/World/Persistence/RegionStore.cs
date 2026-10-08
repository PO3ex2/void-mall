using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Откуда ChunkManager берёт сохранённые колонны и куда отдаёт изменённые (этап 9).</summary>
    public interface IColumnStore
    {
        /// <summary>Сохранённая колонна (готовая, Generated) или false — генерировать заново.</summary>
        bool TryLoad(int2 coord, out ChunkColumn column);

        /// <summary>Запомнить изменённую колонну (сжать сейчас, на диск — при следующей записи).</summary>
        void Store(ChunkColumn column);

        /// <summary>Забыть колонну (перегенерировали — правки выброшены).</summary>
        void Remove(int2 coord);
    }

    /// <summary>
    /// Колонны сейва по регионам 32×32 колонны: файл regions/r.X.Z.bin — сжатые колонны подряд (ColumnCodec). В памяти —
    /// сжатые блоки (килобайты на колонну), регион читается с диска при первом обращении. Изменённые регионы забирает
    /// TakeDirty — запись на диск делает SaveSystem в фоне.
    /// </summary>
    public sealed class RegionStore : IColumnStore
    {
        public const int RegionShift = 5;
        private const int Magic = 0x47524D56; // "VMRG"
        private const int FileVersion = 1;

        private readonly string _folder;
        private readonly IMaterialIds _materials;
        private readonly Dictionary<int2, Dictionary<int2, byte[]>> _regions = new();
        private readonly HashSet<int2> _dirty = new();

        public RegionStore(string folder, IMaterialIds materials)
        {
            _folder = folder;
            _materials = materials;
        }

        public int LoadedRegions => _regions.Count;
        public bool HasDirty => _dirty.Count > 0;

        public static int2 RegionOf(int2 column) => new(column.x >> RegionShift, column.y >> RegionShift);

        public string RegionPath(int2 region) => Path.Combine(_folder, "regions", $"r.{region.x}.{region.y}.bin");

        public bool Contains(int2 coord) => Region(RegionOf(coord)).ContainsKey(coord);

        public bool TryLoad(int2 coord, out ChunkColumn column)
        {
            column = null;
            if (!Region(RegionOf(coord)).TryGetValue(coord, out var blob))
                return false;
            try
            {
                column = ColumnCodec.Decode(coord, blob, _materials);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Save] Колонна {coord} повреждена — будет сгенерирована заново: {e.Message}");
                return false;
            }
        }

        public void Store(ChunkColumn column)
        {
            int2 region = RegionOf(column.Coord);
            Region(region)[column.Coord] = ColumnCodec.Encode(column, _materials);
            _dirty.Add(region);
        }

        public void Remove(int2 coord)
        {
            int2 region = RegionOf(coord);
            if (Region(region).Remove(coord))
                _dirty.Add(region);
        }

        /// <summary>Изменённые регионы как файлы для записи (путь, байты); помечает их записанными.</summary>
        public List<(string path, byte[] data)> TakeDirty()
        {
            var files = new List<(string, byte[])>();
            foreach (var region in _dirty)
                files.Add((RegionPath(region), Serialize(_regions[region])));
            _dirty.Clear();
            return files;
        }

        private Dictionary<int2, byte[]> Region(int2 region)
        {
            if (_regions.TryGetValue(region, out var columns))
                return columns;
            columns = new Dictionary<int2, byte[]>();
            string path = RegionPath(region);
            if (File.Exists(path))
            {
                try
                {
                    columns = Parse(File.ReadAllBytes(path));
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[Save] Регион {path} не прочитан — его колонны сгенерируются заново: {e.Message}");
                }
            }
            _regions.Add(region, columns);
            return columns;
        }

        public static byte[] Serialize(Dictionary<int2, byte[]> columns)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(FileVersion);
                writer.Write(columns.Count);
                foreach (var (coord, blob) in columns)
                {
                    writer.Write(coord.x);
                    writer.Write(coord.y);
                    writer.Write(blob.Length);
                    writer.Write(blob);
                }
            }
            return stream.ToArray();
        }

        public static Dictionary<int2, byte[]> Parse(byte[] data)
        {
            var columns = new Dictionary<int2, byte[]>();
            using var reader = new BinaryReader(new MemoryStream(data));
            if (reader.ReadInt32() != Magic)
                throw new InvalidDataException("не файл региона");
            int version = reader.ReadInt32();
            if (version > FileVersion)
                throw new InvalidDataException($"версия региона {version} новее игры");
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var coord = new int2(reader.ReadInt32(), reader.ReadInt32());
                int length = reader.ReadInt32();
                columns[coord] = reader.ReadBytes(length);
            }
            return columns;
        }
    }
}
