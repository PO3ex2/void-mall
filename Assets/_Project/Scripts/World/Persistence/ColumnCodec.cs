using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>Строковые ID материалов ↔ текущие индексы (реализует MaterialRegistry; в тестах — заглушка).</summary>
    public interface IMaterialIds
    {
        string IdOf(ushort material);
        bool TryGetIndex(string id, out ushort material);
    }

    /// <summary>
    /// Колонна в сейве (этап 9): у каждой колонны своя палитра — строковые ID материалов, клетки хранят номер в палитре.
    /// Поэтому сейв не зависит от порядка материалов в каталоге: после обновления индексы переназначаются по ID, а
    /// убранный из игры материал становится воздухом. Чанк — однородный (одна клетка) или полный (RLE по порядку
    /// индексов клеток), затем урон по клеткам. Всё вместе сжимается Deflate.
    /// </summary>
    public static class ColumnCodec
    {
        public const byte Version = 1;

        private const byte NullChunk = 0, UniformChunk = 1, FullChunk = 2;

        public static byte[] Encode(ChunkColumn column, IMaterialIds materials)
        {
            var palette = new List<ushort>();
            var lookup = new Dictionary<ushort, ushort>();
            ushort Index(ushort material)
            {
                if (!lookup.TryGetValue(material, out ushort index))
                {
                    index = (ushort)palette.Count;
                    lookup.Add(material, index);
                    palette.Add(material);
                }
                return index;
            }

            // Тело — сначала клетки (палитра растёт по ходу), потом заголовок с палитрой.
            using var body = new MemoryStream();
            using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, true))
            {
                foreach (var chunk in column.Chunks)
                {
                    if (chunk == null)
                    {
                        writer.Write(NullChunk);
                        continue;
                    }
                    if (chunk.IsUniform)
                    {
                        writer.Write(UniformChunk);
                        WriteCell(writer, chunk.Get(0), Index);
                    }
                    else
                    {
                        writer.Write(FullChunk);
                        WriteRuns(writer, chunk, Index);
                    }
                    var damage = new List<KeyValuePair<int, int>>(chunk.Damage);
                    WriteVarInt(writer, damage.Count);
                    foreach (var (index, value) in damage)
                    {
                        WriteVarInt(writer, index);
                        WriteVarInt(writer, value);
                    }
                }
            }

            using var output = new MemoryStream();
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
            using (var writer = new BinaryWriter(deflate))
            {
                writer.Write(Version);
                WriteVarInt(writer, palette.Count);
                foreach (var material in palette)
                    writer.Write(materials.IdOf(material) ?? string.Empty);
                writer.Write(body.GetBuffer(), 0, (int)body.Length);
            }
            return output.ToArray();
        }

        /// <summary>Собрать колонну из байтов. Колонна помечена изменённой (её снова запишут), состояние — Generated.</summary>
        public static ChunkColumn Decode(int2 coord, byte[] data, IMaterialIds materials)
        {
            using var input = new MemoryStream(data);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate);
            byte version = reader.ReadByte();
            if (version > Version)
                throw new InvalidDataException($"Колонна {coord} записана более новой версией ({version}).");
            int paletteCount = ReadVarInt(reader);
            var remap = new ushort[paletteCount];
            for (int i = 0; i < paletteCount; i++)
                remap[i] = materials.TryGetIndex(reader.ReadString(), out ushort material) ? material : (ushort)0;

            var column = new ChunkColumn(coord) { State = ColumnState.Generated };
            for (int y = 0; y < WorldConstants.ChunksPerColumn; y++)
            {
                var chunkCoord = new int3(coord.x, y, coord.y);
                byte kind = reader.ReadByte();
                if (kind == NullChunk)
                    continue;
                ChunkData chunk;
                if (kind == UniformChunk)
                {
                    chunk = new ChunkData(chunkCoord, ReadCell(reader, remap));
                }
                else
                {
                    chunk = ChunkData.CreateForGeneration(chunkCoord);
                    ReadRuns(reader, chunk.Cells, remap);
                    chunk.Compress();
                }
                int damageCount = ReadVarInt(reader);
                for (int i = 0; i < damageCount; i++)
                {
                    int index = ReadVarInt(reader);
                    chunk.SetDamage(index, ReadVarInt(reader));
                }
                chunk.IsModified = true;
                column.Chunks[y] = chunk;
            }
            return column;
        }

        private static void WriteRuns(BinaryWriter writer, ChunkData chunk, Func<ushort, ushort> index)
        {
            int i = 0;
            while (i < WorldConstants.ChunkVolume)
            {
                Cell cell = chunk.Get(i);
                int run = 1;
                while (i + run < WorldConstants.ChunkVolume && chunk.Get(i + run) == cell)
                    run++;
                WriteVarInt(writer, run);
                WriteCell(writer, cell, index);
                i += run;
            }
        }

        private static void ReadRuns(BinaryReader reader, Unity.Collections.NativeArray<Cell> cells, ushort[] remap)
        {
            int i = 0;
            while (i < WorldConstants.ChunkVolume)
            {
                int run = ReadVarInt(reader);
                if (run <= 0 || i + run > WorldConstants.ChunkVolume)
                    throw new InvalidDataException("Повреждённые данные чанка.");
                Cell cell = ReadCell(reader, remap);
                for (int k = 0; k < run; k++)
                    cells[i + k] = cell;
                i += run;
            }
        }

        private static void WriteCell(BinaryWriter writer, Cell cell, Func<ushort, ushort> index)
        {
            WriteVarInt(writer, cell.Material == 0 ? 0 : index(cell.Material) + 1);
            writer.Write(cell.Value);
            writer.Write(cell.Meta);
        }

        private static Cell ReadCell(BinaryReader reader, ushort[] remap)
        {
            int index = ReadVarInt(reader);
            byte value = reader.ReadByte();
            byte meta = reader.ReadByte();
            if (index == 0)
                return Cell.Air;
            ushort material = index - 1 < remap.Length ? remap[index - 1] : (ushort)0;
            // Убранный материал — воздух (форма и плотность у воздуха не нужны).
            return material == 0 ? Cell.Air : new Cell(material, value, meta);
        }

        public static void WriteVarInt(BinaryWriter writer, int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                writer.Write((byte)(v | 0x80));
                v >>= 7;
            }
            writer.Write((byte)v);
        }

        public static int ReadVarInt(BinaryReader reader)
        {
            int result = 0, shift = 0;
            while (true)
            {
                byte b = reader.ReadByte();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
                shift += 7;
                if (shift > 28)
                    throw new InvalidDataException("Слишком длинное число.");
            }
        }
    }
}
