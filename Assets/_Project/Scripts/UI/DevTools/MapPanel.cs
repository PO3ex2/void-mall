using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Input;
using VoidMall.Player;
using VoidMall.World;
using VoidMall.Core;

namespace VoidMall.UI
{
    /// <summary>
    /// Карта этажа (F6): превью раскладки 5×5 макроячеек вокруг игрока на его уровне, маркер игрока.
    /// Перерисовывается при смене ячейки или этажа. Отладочная панель: в игре карты не будет.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapPanel : MonoBehaviour
    {
        private const int RadiusCells = 2;
        private const float MapSize = 520f;

        private InputAction _toggle;
        private VisualElement _panel;
        private Image _image;
        private VisualElement _marker;
        private Label _title;
        private Texture2D _texture;
        private bool _open;
        private int2 _cell = new(int.MinValue, int.MinValue);
        private int _level = -1;
        private uint _seed;

        public void Initialize(InputService input)
        {
            _toggle = input.Action(InputService.Maps.Global, "ToggleMap");

            var root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(root);

            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            DevToolsStyle.Panel(_panel);
            _panel.style.position = Position.Absolute;
            _panel.style.left = 12f;
            _panel.style.top = 12f;
            _panel.style.display = DisplayStyle.None;
            root.Add(_panel);

            _title = new Label { pickingMode = PickingMode.Ignore };
            _title.style.marginBottom = 4f;
            _panel.Add(_title);

            // Нижняя строка текстуры (минимальный Z) рисуется внизу: север (+Z) сверху, без переворотов.
            _image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _image.style.width = MapSize;
            _image.style.height = MapSize;
            _panel.Add(_image);

            _marker = new VisualElement { pickingMode = PickingMode.Ignore };
            _marker.style.position = Position.Absolute;
            _marker.style.width = 8f;
            _marker.style.height = 8f;
            _marker.style.backgroundColor = new Color(1f, 0.15f, 0.1f);
            _marker.style.borderTopLeftRadius = _marker.style.borderTopRightRadius = 4f;
            _marker.style.borderBottomLeftRadius = _marker.style.borderBottomRightRadius = 4f;
            _image.Add(_marker);

            var legend = new Label("жёлтый — лестница, голубой — лифт, чёрный — пустота атриума, фиолетовый — аномалия; цвета комнат — разделы; блёклые ячейки — пустые разделы, сиреневые — поле аномалий")
            {
                pickingMode = PickingMode.Ignore,
            };
            legend.style.fontSize = 11f;
            legend.style.color = DevToolsStyle.Muted;
            legend.style.maxWidth = MapSize;
            legend.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(legend);
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.Map");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_toggle != null && _toggle.WasPressedThisFrame())
            {
                _open = !_open;
                _panel.style.display = _open ? DisplayStyle.Flex : DisplayStyle.None;
                _cell = new int2(int.MinValue, int.MinValue);
            }
            var world = ChunkManager.Instance;
            var motor = PlayerMotor.Local;
            if (!_open || world == null || world.Sections == null || motor == null)
                return;

            Vector3 position = motor.State.Position;
            int2 cell = MallLayout.CellOf(position);
            int level = MallLayout.LevelAtFeet(position.y);
            if (!cell.Equals(_cell) || level != _level || world.Seed != _seed)
            {
                _cell = cell;
                _level = level;
                _seed = world.Seed;
                _texture = LayoutPreview.Render(world.Seed, world.AnomalyFrequency, world.Sections, cell, RadiusCells, level, _texture);
                _image.image = _texture;
                _title.text = $"Этаж {MallLayout.LevelName(level)} · ячейка ({cell.x}, {cell.y}), {world.GetCellInfo(cell).Biome.DisplayName()} · [F6] закрыть";
            }

            // Маркер: X — слева направо, Z — снизу вверх (координата top отсчитывается сверху).
            float span = (RadiusCells * 2 + 1) * MallLayout.CellSize;
            float2 origin = MallLayout.CellOrigin(cell - RadiusCells);
            float u = (position.x - origin.x) / span;
            float v = (position.z - origin.y) / span;
            _marker.style.left = u * MapSize - 4f;
            _marker.style.top = (1f - v) * MapSize - 4f;
        }

        private void OnDestroy()
        {
            if (_texture != null)
                Destroy(_texture);
        }
    }
}
