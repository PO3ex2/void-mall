using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Карта загрузки мира на ПК (как в Minecraft): зона портала по колоннам 32×32 м, каждая проходит стадии — в очереди
    /// (тёмная), генерация блоков (мигает серым), меш и коллайдеры (синеватая), готова (вспышка, затем кусок плана этажа
    /// из превью). План рисуется один раз при открытии; дальше каждый кадр только раскрашиваются колонны.
    /// </summary>
    public sealed class LoadingMap
    {
        public const float DisplaySize = 320f;
        private const float FlashTime = 0.35f;

        private static readonly Color32 QueuedColor = new(14, 18, 23, 255);
        private static readonly Color32 GeneratingDark = new(45, 50, 56, 255);
        private static readonly Color32 GeneratingLight = new(95, 100, 106, 255);
        private static readonly Color32 MeshingColor = new(42, 82, 118, 255);
        private static readonly Color32 GridColor = new(8, 10, 13, 255);
        private static readonly Color32 PortalColor = new(160, 225, 255, 255);

        /// <summary>Подписи и цвета стадий для легенды.</summary>
        public static readonly (string caption, Color color)[] Legend =
        {
            ("очередь", QueuedColor),
            ("блоки", GeneratingLight),
            ("меш", MeshingColor),
            ("готово", new Color32(205, 200, 185, 255)),
        };

        private const int Columns = GameFlow.ReadyRadius * 2 + 1;
        private const int Size = Columns * WorldConstants.ChunkSize;

        public readonly Image Element;

        private Texture2D _texture;
        private Color32[] _plan, _pixels;
        private readonly float[] _readyAt = new float[Columns * Columns];
        private int2 _firstColumn;
        private int2 _portalPixel;
        private bool _hasCenter;

        public LoadingMap()
        {
            Element = new Image { scaleMode = ScaleMode.ScaleToFit };
            Element.style.width = Element.style.height = DisplaySize;
            Element.style.alignSelf = Align.Center;
            Element.style.backgroundColor = new Color(QueuedColor.r / 255f, QueuedColor.g / 255f, QueuedColor.b / 255f);
            PcOs.Border(Element, PcOs.Accent * 0.4f, 1f, 2f);
        }

        /// <summary>Раскрасить по стадиям; center — выход портала (null — пока неизвестен, карта не рисуется).</summary>
        public void Tick(ChunkManager world, Vector3? center)
        {
            if (world == null)
                return;
            if (!_hasCenter)
            {
                if (center == null || world.Sections == null)
                    return;
                Build(world, center.Value);
            }

            float now = Time.unscaledTime;
            for (int cz = 0; cz < Columns; cz++)
            for (int cx = 0; cx < Columns; cx++)
            {
                int index = cx + cz * Columns;
                var stage = world.StageOf(_firstColumn + new int2(cx, cz));
                if (stage == ColumnStage.Ready && _readyAt[index] < 0f)
                    _readyAt[index] = now;
                // Готовая колонна готовой и остаётся (выгрузка за спиной игрока карту не портит).
                if (_readyAt[index] >= 0f)
                    stage = ColumnStage.Ready;
                Fill(cx, cz, stage, now - _readyAt[index], now + index * 0.37f);
            }

            // Метка выхода портала мигает поверх.
            if (math.frac(now * 1.5f) < 0.6f)
            {
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int2 p = _portalPixel + new int2(dx, dz);
                    if (p.x >= 0 && p.y >= 0 && p.x < Size && p.y < Size)
                        _pixels[p.x + p.y * Size] = PortalColor;
                }
            }
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private void Build(ChunkManager world, Vector3 center)
        {
            _hasCenter = true;
            int3 cell = (int3)math.floor((float3)center);
            int2 centerColumn = WorldConstants.CellToColumn(cell);
            _firstColumn = centerColumn - GameFlow.ReadyRadius;
            int2 min = _firstColumn * WorldConstants.ChunkSize;
            _portalPixel = new int2(cell.x, cell.z) - min;
            int level = math.clamp(MallLayout.LevelAt(cell.y), 0, MallLayout.LevelCount - 1);
            _plan = new Color32[Size * Size];
            _pixels = new Color32[Size * Size];
            LayoutPreview.RenderArea(world.Seed, world.AnomalyFrequency, world.Sections, min, Size, level, _plan);
            _texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Loading Map" };
            for (int i = 0; i < _readyAt.Length; i++)
                _readyAt[i] = -1f;
            Element.image = _texture;
        }

        private void Fill(int cx, int cz, ColumnStage stage, float sinceReady, float phase)
        {
            Color32 flat = stage switch
            {
                ColumnStage.Generating => Color32.Lerp(GeneratingDark, GeneratingLight, 0.5f + 0.5f * math.sin(phase * 7f)),
                ColumnStage.Meshing => MeshingColor,
                _ => QueuedColor,
            };
            float flash = stage == ColumnStage.Ready ? 1f - math.saturate(sinceReady / FlashTime) : 0f;
            int x0 = cx * WorldConstants.ChunkSize, z0 = cz * WorldConstants.ChunkSize;
            for (int z = 0; z < WorldConstants.ChunkSize; z++)
            for (int x = 0; x < WorldConstants.ChunkSize; x++)
            {
                int i = x0 + x + (z0 + z) * Size;
                if (stage == ColumnStage.Ready)
                {
                    // Готовые колонны сливаются в один план; вспышка — светлее только что готовой.
                    _pixels[i] = flash > 0f ? Color32.Lerp(_plan[i], new Color32(255, 255, 255, 255), flash * 0.7f) : _plan[i];
                }
                else
                {
                    // У неготовых — тёмная рамка: видно сетку колонн.
                    _pixels[i] = x == 0 || z == 0 || x == WorldConstants.ChunkSize - 1 || z == WorldConstants.ChunkSize - 1 ? GridColor : flat;
                }
            }
        }

        public void Dispose()
        {
            if (_texture != null)
                Object.Destroy(_texture);
            _texture = null;
        }
    }
}
