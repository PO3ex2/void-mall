using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Освещение мола (ARCHITECTURE §4.7–4.8). Все лампы — светящиеся блоки: эмиссию считает шейдер по LampRules.
    /// Настоящие источники Light (Forward+) — только у ближайших к игроку ламп из пула, тени — у нескольких самых близких.
    /// Ночью лампы гаснут волнами, ambient и туман темнеют. Также отвечает на запрос «освещённость в точке».
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class LightingDirector : MonoBehaviour, ILightProbe
    {
        public static readonly CVar<int> MaxLights = new("r_lamplights", 40, "Сколько ближайших ламп светят настоящим светом.", ConsoleFlags.Archive);
        public static readonly CVar<int> ShadowLights = new("r_lampshadows", 2, "Сколько ближайших ламп отбрасывают тени.", ConsoleFlags.Archive);
        public static readonly CVar<float> LightRadius = new("r_lampradius", 34f, "Радиус поиска ламп для настоящего света, м.");

        [SerializeField] private Color _lampColor = new(1f, 0.93f, 0.82f);
        [SerializeField] private float _lampIntensity = 2.4f;
        [SerializeField] private float _lampRange = 8.5f;
        [SerializeField] private Color _emergencyColor = new(1f, 0.22f, 0.16f);
        [SerializeField] private float _emergencyIntensity = 1.2f;
        [SerializeField] private float _emergencyRange = 5f;
        [SerializeField] private Color _skylightColor = new(0.8f, 0.88f, 1f);
        [SerializeField] private float _skylightIntensity = 3f;
        [SerializeField] private float _skylightRange = 14f;

        [Header("Ambient и туман")]
        [SerializeField] private Color _dayAmbient = new(0.2f, 0.2f, 0.21f);
        [SerializeField] private Color _nightAmbient = new(0.015f, 0.015f, 0.02f);
        [SerializeField] private Color _dayFog = new(0.32f, 0.31f, 0.29f);
        [SerializeField] private Color _nightFog = new(0f, 0f, 0f);
        [SerializeField, Range(0f, 1f)] private float _dayReflections = 0.25f;

        /// <summary>Насколько туман темнеет вечером (18–21 ч, вместе со световыми фонарями) ещё до отключения ламп.</summary>
        [SerializeField, Range(0f, 1f)] private float _eveningFogDarkness = 0.8f;

        private Camera _camera;
        private Material _sky, _sourceSky;
        private Color _appliedSky = new(-1f, -1f, -1f, -1f);
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private float _fogStart, _fogEnd;

        private Color _appliedAmbient = new(-1f, -1f, -1f, -1f);

        private static readonly int SeedId = Shader.PropertyToID("_VM_Seed");
        private static readonly int WaveId = Shader.PropertyToID("_VM_LampWave");
        private static readonly int SkylightId = Shader.PropertyToID("_VM_Skylight");
        private static readonly int EmergencyWaveId = Shader.PropertyToID("_VM_EmergencyWave");
        private static readonly int TimeId = Shader.PropertyToID("_VM_Time");
        private static readonly int FieldCellsId = Shader.PropertyToID("_VM_FieldCells");
        private static readonly int FieldCountId = Shader.PropertyToID("_VM_FieldCount");

        private readonly Vector4[] _fieldCells = new Vector4[9];

        private struct Slot
        {
            public Light Light;
            public int3 Lamp;
            public bool Assigned;
            public float Fade;
        }

        private readonly List<Slot> _slots = new();
        private readonly List<int3> _lamps = new();
        private readonly List<(float distance, int3 cell)> _candidates = new();
        private readonly HashSet<int3> _wanted = new();
        private readonly List<int3> _probe = new();
        private Transform _lightRoot;
        private float _nextRefresh;
        private float _wave;
        private float _skylight;
        private float _emergencyWave;
        private float _time;

        public static LightingDirector Instance { get; private set; }

        public float Wave => _wave;
        public int ActiveLights { get; private set; }

        /// <summary>Источники света игрока и построек (фонарик, лампа, генератор) — учитываются в освещённости.</summary>
        public static readonly List<Light> DynamicLights = new();

        private void Awake()
        {
            Instance = this;
            _lightRoot = new GameObject("Lamp Lights").transform;
            _lightRoot.SetParent(transform, false);
            _fogStart = RenderSettings.fogStartDistance;
            _fogEnd = RenderSettings.fogEndDistance;
        }

        private void OnEnable() => Services.Register<ILightProbe>(this);

        private void OnDisable() => Services.Unregister<ILightProbe>(this);

        /// <summary>
        /// Небо — градиент (VoidMall/FogSky): выше горизонта цвет тумана, поэтому даль за прогруженными чанками и проёмы
        /// растворяются в дымке, а внизу чёрная пустота — в бездну и вырезы смотреть темно. Подкрашиваем копию материала
        /// сцены (сам ассет не трогаем), только при изменении цвета.
        /// </summary>
        private void UpdateSky(Color fog)
        {
            if (_camera == null || !_camera.isActiveAndEnabled)
                _camera = Camera.main;
            if (_camera != null && _camera.clearFlags != CameraClearFlags.Skybox)
                _camera.clearFlags = CameraClearFlags.Skybox;
            var current = RenderSettings.skybox;
            if (current == null)
                return;
            if (current != _sky)
            {
                if (!current.HasProperty(TopColorId))
                    return;
                if (_sky != null)
                    Destroy(_sky);
                _sourceSky = current;
                _sky = new Material(current) { name = current.name + " (runtime)", hideFlags = HideFlags.DontSave };
                _appliedSky = new Color(-1f, -1f, -1f, -1f);
                RenderSettings.skybox = _sky;
            }
            if (fog != _appliedSky)
            {
                _appliedSky = fog;
                _sky.SetColor(TopColorId, fog);
            }
        }

        private void OnDestroy()
        {
            if (_sky == null)
                return;
            if (RenderSettings.skybox == _sky)
                RenderSettings.skybox = _sourceSky;
            Destroy(_sky);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            DynamicLights.Clear();
            Instance = null;
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("World.Lighting");

        private void LateUpdate()
        {
            using var perf = Perf.Measure(UpdateProbe);
            var world = ChunkManager.Instance;
            if (world == null)
                return;

            float hour = WorldClock.Instance != null ? WorldClock.Instance.Hour : 12f;
            _wave = LampRules.Wave(hour);
            _skylight = LampRules.SkylightLevel(hour);
            _emergencyWave = LampRules.EmergencyWave(hour);
            _time = Time.time;
            Shader.SetGlobalInteger(SeedId, unchecked((int)world.Seed));
            Shader.SetGlobalFloat(WaveId, _wave);
            Shader.SetGlobalFloat(SkylightId, _skylight);
            Shader.SetGlobalFloat(EmergencyWaveId, _emergencyWave);
            Shader.SetGlobalFloat(TimeId, _time);

            UpdateField(world);

            float darkness = math.saturate(_wave);
            Color ambient = Color.Lerp(_dayAmbient, _nightAmbient, darkness);
            if (ambient != _appliedAmbient)
            {
                // Плоский ambient надо отдать и в пробу SH (её читают шейдеры), иначе ночью останется дневной свет неба.
                _appliedAmbient = ambient;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                var probe = new SphericalHarmonicsL2();
                probe.AddAmbientLight(ambient);
                RenderSettings.ambientProbe = probe;
                // Отражения — от неба-скайбокса, в закрытом моле их почти не должно быть, ночью — совсем.
                RenderSettings.reflectionIntensity = Mathf.Lerp(_dayReflections, 0f, darkness);
            }
            // Туман (и фон камеры) темнеет вечером вместе со световыми фонарями, а чёрным становится с отключением ламп.
            float evening = 1f - math.saturate((_skylight - 0.03f) / 0.97f);
            Color fog = Color.Lerp(_dayFog, _nightFog, math.max(darkness, evening * _eveningFogDarkness));
            RenderSettings.fogColor = fog;
            // Туман густеет до края прогрузки, иначе на краю видна граница между миром и фоном.
            float edge = ChunkManager.ViewDistance.Value * WorldConstants.ChunkSize - WorldConstants.ChunkSize * 0.75f;
            RenderSettings.fogEndDistance = Mathf.Max(24f, Mathf.Min(_fogEnd, edge));
            RenderSettings.fogStartDistance = Mathf.Min(_fogStart, RenderSettings.fogEndDistance * 0.3f);
            // Под водой — густой сине-зелёный туман: видно на несколько метров.
            if (_camera != null && world.IsLiquidAt(_camera.transform.position))
            {
                fog = Color.Lerp(new Color(0.05f, 0.17f, 0.18f), new Color(0.01f, 0.04f, 0.05f), darkness);
                RenderSettings.fogColor = fog;
                RenderSettings.fogStartDistance = 0f;
                RenderSettings.fogEndDistance = 10f;
            }
            UpdateSky(fog);

            Vector3 center = ViewerPosition();
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.2f;
                RefreshAssignments(world, center);
            }
            UpdateLights(world, center);
        }

        /// <summary>Выбрать ближайшие горящие лампы и раздать им источники света из пула (без лишних перестановок).</summary>
        private void RefreshAssignments(ChunkManager world, Vector3 center)
        {
            _lamps.Clear();
            world.CollectLamps(center, LightRadius.Value, _lamps);
            _candidates.Clear();
            foreach (var cell in _lamps)
            {
                // Мёртвые лампы и погашенные группы не берём (мерцающие — берём, мерцание анимируется в UpdateLights).
                LampKind kind = KindAt(world, cell);
                if (kind == LampKind.None || BaseIntensity(world.Seed, cell, kind) <= 0f)
                    continue;
                _candidates.Add((math.distancesq((float3)center, (float3)cell + 0.5f), cell));
            }
            _candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            int limit = Mathf.Clamp(MaxLights.Value, 0, 128);
            _wanted.Clear();
            for (int i = 0; i < _candidates.Count && _wanted.Count < limit; i++)
                _wanted.Add(_candidates[i].cell);

            // Освобождаем слоты ламп, которые больше не нужны (они плавно погаснут).
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Assigned && !_wanted.Contains(slot.Lamp))
                {
                    slot.Assigned = false;
                    _slots[i] = slot;
                }
                else if (slot.Assigned)
                {
                    _wanted.Remove(slot.Lamp);
                }
            }
            // Новые лампы — в свободные (погасшие) слоты или в новые.
            foreach (var cell in _wanted)
            {
                int free = -1;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (!_slots[i].Assigned && _slots[i].Fade <= 0.01f)
                    {
                        free = i;
                        break;
                    }
                }
                if (free < 0)
                {
                    if (_slots.Count >= limit + 16)
                        break;
                    _slots.Add(new Slot { Light = CreateLight() });
                    free = _slots.Count - 1;
                }
                var slot = _slots[free];
                slot.Lamp = cell;
                slot.Assigned = true;
                slot.Fade = 0f;
                PlaceLight(world, slot.Light, cell);
                _slots[free] = slot;
            }
        }

        private void UpdateLights(ChunkManager world, Vector3 center)
        {
            int shadowBudget = Mathf.Max(0, ShadowLights.Value);
            float fadeStep = Time.deltaTime * 4f;
            int active = 0;

            // Тени — только у ближайших назначенных ламп (у гаснущих и дальних — без теней).
            _probe.Clear();
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Assigned)
                    _probe.Add(new int3(i, 0, 0));
                else
                    _slots[i].Light.shadows = LightShadows.None;
            }
            _probe.Sort((a, b) =>
                math.distancesq((float3)center, _slots[a.x].Lamp).CompareTo(math.distancesq((float3)center, _slots[b.x].Lamp)));

            for (int order = 0; order < _probe.Count; order++)
            {
                var slot = _slots[_probe[order].x];
                slot.Light.shadows = order < shadowBudget ? LightShadows.Soft : LightShadows.None;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                slot.Fade = Mathf.MoveTowards(slot.Fade, slot.Assigned ? 1f : 0f, fadeStep);
                LampKind kind = slot.Fade > 0f ? KindAt(world, slot.Lamp) : LampKind.None;
                float intensity = kind == LampKind.None ? 0f
                    : LampRules.Intensity(world.Seed, slot.Lamp, kind, _wave, _skylight, _emergencyWave, _time, InField(world, slot.Lamp)) * slot.Fade;
                if (intensity <= 0.001f)
                {
                    slot.Light.enabled = false;
                }
                else
                {
                    slot.Light.enabled = true;
                    slot.Light.intensity = intensity * BaseLightIntensity(kind);
                    active++;
                }
                _slots[i] = slot;
            }
            ActiveLights = active;
        }

        private Light CreateLight()
        {
            var go = new GameObject("LampLight");
            go.transform.SetParent(_lightRoot, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            // Низкий уровень разрешения теней: несколько точечных теней (по 6 граней) помещаются в атлас без «ужатия».
            if (!go.TryGetComponent(out UniversalAdditionalLightData data))
                data = go.AddComponent<UniversalAdditionalLightData>();
            data.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            light.enabled = false;
            return light;
        }

        private void PlaceLight(ChunkManager world, Light light, int3 cell)
        {
            LampKind kind = KindAt(world, cell);
            // Лампа в потолке светит вниз: источник чуть ниже блока, чтобы не заслонять сам себя (в полу — выше).
            light.transform.position = LampPoint(world, cell, 0.35f);
            switch (kind)
            {
                case LampKind.Emergency:
                    light.color = _emergencyColor;
                    light.range = _emergencyRange;
                    break;
                case LampKind.Skylight:
                    light.color = _skylightColor;
                    light.range = _skylightRange;
                    break;
                default:
                    light.color = _lampColor;
                    light.range = _lampRange;
                    break;
            }
        }

        private float BaseLightIntensity(LampKind kind) => kind switch
        {
            LampKind.Emergency => _emergencyIntensity,
            LampKind.Skylight => _skylightIntensity,
            _ => _lampIntensity,
        };

        private float BaseIntensity(uint seed, int3 cell, LampKind kind)
        {
            if (kind != LampKind.Lamp)
                return LampRules.Intensity(seed, cell, kind, _wave, _skylight, _emergencyWave, _time);
            // Для выбора кандидатов мерцание не важно: берём «горит ли в принципе».
            if (LampRules.IsDarkGroup(seed, LampRules.GroupOf(cell)))
                return 0f;
            return LampRules.LampIntensity(seed, cell, _wave, 0f) > 0f || LampRules.LampIntensity(seed, cell, _wave, 0.37f) > 0f ? 1f : 0f;
        }

        private static bool InField(ChunkManager world, int3 cell) =>
            world.GetCellInfo(new int2(cell.x >> MallLayout.CellShift, cell.z >> MallLayout.CellShift)).Biome == MallBiome.AnomalyField;

        /// <summary>Ячейки поля аномалий 3×3 вокруг камеры — шейдеру (там лампы мерцают чаще).</summary>
        private void UpdateField(ChunkManager world)
        {
            Vector3 eye = ViewerPosition();
            int2 center = MallLayout.CellOf(eye);
            int count = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 cell = center + new int2(dx, dz);
                if (world.GetCellInfo(cell).Biome == MallBiome.AnomalyField)
                    _fieldCells[count++] = new Vector4(cell.x, cell.y, 0f, 0f);
            }
            Shader.SetGlobalVectorArray(FieldCellsId, _fieldCells);
            Shader.SetGlobalFloat(FieldCountId, count);
        }

        /// <summary>
        /// Точка источника лампы: под блоком (лампа в потолке) или над ним, если лампа в полу — в перевёрнутом чанке
        /// пол и потолок поменялись местами.
        /// </summary>
        private static Vector3 LampPoint(ChunkManager world, int3 cell, float offset)
        {
            bool inFloor = world.GetCell(cell + new int3(0, 1, 0)).IsAir && !world.GetCell(cell - new int3(0, 1, 0)).IsAir;
            return new Vector3(cell.x + 0.5f, inFloor ? cell.y + 1f + offset : cell.y - offset, cell.z + 0.5f);
        }

        private static LampKind KindAt(ChunkManager world, int3 cell)
        {
            Cell value = world.GetCell(cell);
            return value.Material == 0 ? LampKind.None : world.GetInfo(value.Material).Lamp;
        }

        private static Vector3 ViewerPosition()
        {
            var camera = Camera.main;
            if (camera != null)
                return camera.transform.position;
            var viewer = WorldViewer.Primary;
            return viewer != null ? viewer.transform.position : Vector3.zero;
        }

        // ───────────────────────────── Освещённость в точке ─────────────────────────────

        private readonly List<int3> _sampleLamps = new();
        private readonly List<(float value, int3 cell)> _contributions = new();

        /// <summary>
        /// Приближённая освещённость: вклад ближайших горящих ламп с затуханием по расстоянию,
        /// для трёх сильнейших — проверка заслонения лучом. Плюс динамические источники и небольшой ambient.
        /// </summary>
        public float SampleLight(Vector3 position)
        {
            var world = ChunkManager.Instance;
            if (world == null)
                return 1f;
            _sampleLamps.Clear();
            _contributions.Clear();
            world.CollectLamps(position, 12f, _sampleLamps);
            foreach (var cell in _sampleLamps)
            {
                LampKind kind = KindAt(world, cell);
                float intensity = LampRules.Intensity(world.Seed, cell, kind, _wave, _skylight, _emergencyWave, _time, InField(world, cell));
                if (intensity <= 0f)
                    continue;
                float range = kind == LampKind.Emergency ? _emergencyRange : kind == LampKind.Skylight ? _skylightRange : _lampRange;
                float distance = Vector3.Distance(position, LampPoint(world, cell, 0.35f));
                float falloff = Mathf.Clamp01(1f - distance / range);
                float strength = kind == LampKind.Emergency ? 0.25f : 1f;
                float value = intensity * falloff * falloff * strength;
                if (value > 0.001f)
                    _contributions.Add((value, cell));
            }
            _contributions.Sort((a, b) => b.value.CompareTo(a.value));

            float sum = 0f;
            int mask = 1 << GameLayers.World;
            for (int i = 0; i < _contributions.Count; i++)
            {
                var (value, cell) = _contributions[i];
                if (i < 3)
                {
                    Vector3 lampPoint = LampPoint(world, cell, 0.05f);
                    if (Physics.Linecast(position, lampPoint, out var hit, mask, QueryTriggerInteraction.Ignore)
                        && (hit.point - lampPoint).sqrMagnitude > 0.36f)
                        continue;
                }
                sum += value;
            }

            foreach (var light in DynamicLights)
            {
                if (light == null || !light.isActiveAndEnabled)
                    continue;
                float distance = Vector3.Distance(position, light.transform.position);
                float falloff = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, light.range));
                sum += falloff * falloff * Mathf.Clamp01(light.intensity / 2f);
            }

            float ambient = RenderSettings.ambientLight.grayscale;
            return Mathf.Clamp01(sum + ambient * 0.5f);
        }
    }
}
