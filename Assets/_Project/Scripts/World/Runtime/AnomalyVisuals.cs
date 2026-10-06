using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Объекты-эффекты аномалий рядом с игроком (ячейки 3×3): вспышки «Смотри на меня» (билборды, свет не дают)
    /// и оазисы — прожектор «неба» сверху вниз (светит по времени суток и входит в освещённость: место светлое),
    /// конус лучей и иногда дождь. Всё выводится из плана ячеек и пересоздаётся при подходе.
    /// </summary>
    public sealed class AnomalyVisuals : MonoBehaviour
    {
        private const float RefreshInterval = 0.3f;
        private const float FlareRadius = 60f;
        private const float FlareSize = 1.5f;

        /// <summary>Доля игровых часов, когда в оазисе идёт дождь.</summary>
        public const float RainChance = 0.25f;

        [SerializeField] private Material _vfxMaterial;

        private sealed class Flare
        {
            public ulong Key;
            public Transform View;
            public bool Seen;
        }

        private sealed class Oasis
        {
            public int2 Cell;
            public MallCellInfo Info;
            public GameObject Root;
            public Light Sun;
            public MeshRenderer Shaft;
            public ParticleSystem Rain;
            public bool Seen;
        }

        private readonly Dictionary<ulong, Flare> _flares = new();
        private readonly Dictionary<int2, Oasis> _oases = new();
        private readonly List<ulong> _staleFlares = new();
        private readonly List<int2> _staleOases = new();
        private Material _flareMaterial, _shaftMaterial;
        private Mesh _quad;
        private float _timer;

        private void Awake()
        {
            if (_vfxMaterial != null)
            {
                _flareMaterial = new Material(_vfxMaterial) { name = "Flare (runtime)", hideFlags = HideFlags.DontSave };
                _flareMaterial.SetFloat("_Mode", 1f);
                _flareMaterial.SetFloat("_Intensity", 1.8f);
                _shaftMaterial = new Material(_vfxMaterial) { name = "Shaft (runtime)", hideFlags = HideFlags.DontSave };
                _shaftMaterial.SetFloat("_Mode", 2f);
                _shaftMaterial.SetColor("_Color", new Color(1f, 0.96f, 0.86f));
            }
            _quad = BuildQuad();
        }

        private void OnDestroy()
        {
            Clear();
            if (_flareMaterial != null) Destroy(_flareMaterial);
            if (_shaftMaterial != null) Destroy(_shaftMaterial);
            if (_quad != null) Destroy(_quad);
        }

        private void OnEnable()
        {
            var world = ChunkManager.Instance;
            if (world != null)
                world.WorldReset += Clear;
        }

        private void OnDisable()
        {
            var world = ChunkManager.Instance;
            if (world != null)
                world.WorldReset -= Clear;
        }

        private void Clear()
        {
            foreach (var flare in _flares.Values)
                if (flare.View != null)
                    Destroy(flare.View.gameObject);
            _flares.Clear();
            foreach (var oasis in _oases.Values)
                DestroyOasis(oasis);
            _oases.Clear();
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("World.AnomalyVisuals");

        private void LateUpdate()
        {
            using var perf = Perf.Measure(UpdateProbe);
            var world = ChunkManager.Instance;
            var camera = Camera.main;
            if (world == null || camera == null)
                return;
            Vector3 eye = camera.transform.position;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = RefreshInterval;
                Refresh(world, eye);
            }

            // Вспышки всегда смотрят в камеру.
            Quaternion facing = camera.transform.rotation;
            foreach (var flare in _flares.Values)
                flare.View.rotation = facing;

            float hour = WorldClock.Instance != null ? WorldClock.Instance.Hour : 12f;
            float sky = LampRules.SkylightLevel(hour);
            foreach (var oasis in _oases.Values)
                UpdateOasis(world, oasis, sky);
        }

        private void Refresh(ChunkManager world, Vector3 eye)
        {
            foreach (var flare in _flares.Values)
                flare.Seen = false;
            foreach (var oasis in _oases.Values)
                oasis.Seen = false;

            int2 center = MallLayout.CellOf(eye);
            int eyeLevel = MallLayout.LevelAtFeet(eye.y - 1.6f);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 cell = center + new int2(dx, dz);
                var info = world.GetCellInfo(cell);
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    if (marker.Kind != AnomalyMarkerKind.LookAtMe || math.distance((float3)eye, marker.Center) > FlareRadius)
                        continue;
                    ulong key = ((ulong)(uint)cell.x << 36) ^ ((ulong)(uint)cell.y << 8) ^ (uint)i;
                    if (!_flares.TryGetValue(key, out var flare))
                    {
                        flare = new Flare { Key = key, View = CreateFlare(marker) };
                        _flares.Add(key, flare);
                    }
                    flare.Seen = true;
                }
                // Оазис виден со своего этажа и с этажа шахты.
                if (info.Anomaly == AnomalyKind.Oasis && math.abs(eyeLevel - info.AnomalyLevel - 0.5f) <= 1.5f)
                {
                    if (!_oases.TryGetValue(cell, out var oasis))
                    {
                        oasis = CreateOasis(info);
                        _oases.Add(cell, oasis);
                    }
                    oasis.Seen = true;
                }
            }

            _staleFlares.Clear();
            foreach (var pair in _flares)
                if (!pair.Value.Seen)
                    _staleFlares.Add(pair.Key);
            foreach (var key in _staleFlares)
            {
                Destroy(_flares[key].View.gameObject);
                _flares.Remove(key);
            }
            _staleOases.Clear();
            foreach (var pair in _oases)
                if (!pair.Value.Seen)
                    _staleOases.Add(pair.Key);
            foreach (var key in _staleOases)
            {
                DestroyOasis(_oases[key]);
                _oases.Remove(key);
            }
        }

        // ───────────────────────────── «Смотри на меня» ─────────────────────────────

        private Transform CreateFlare(in AnomalyMarker marker)
        {
            var go = new GameObject("LookAtMe");
            go.transform.SetParent(transform, false);
            go.transform.position = marker.Center;
            go.transform.localScale = Vector3.one * FlareSize;
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _flareMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        // ───────────────────────────── Оазис ─────────────────────────────

        /// <summary>Высота экрана «неба» (низ потолочного ряда этажа над оазисом) и пола оазиса.</summary>
        public static float OasisSkyY(in MallCellInfo info) => MallLayout.CeilingRow(info.AnomalyLevel + 1);

        public static float OasisFloorY(in MallCellInfo info) => MallLayout.FloorTop(info.AnomalyLevel);

        private Oasis CreateOasis(in MallCellInfo info)
        {
            float skyY = OasisSkyY(info), floorY = OasisFloorY(info);
            float height = skyY - floorY;
            float radius = info.AnomalyRadius;
            var root = new GameObject("Oasis");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(info.AnomalyCenter.x, floorY, info.AnomalyCenter.y);

            // «Солнце» экрана: прожектор вниз, в освещённости (рассудок) — как свет.
            var sun = new GameObject("Sky Light").AddComponent<Light>();
            sun.transform.SetParent(root.transform, false);
            sun.transform.localPosition = new Vector3(0f, height - 0.1f, 0f);
            sun.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            sun.type = LightType.Spot;
            sun.range = height + 6f;
            sun.spotAngle = 2f * Mathf.Atan(radius / height) * Mathf.Rad2Deg + 35f;
            sun.innerSpotAngle = sun.spotAngle * 0.5f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            LightingDirector.DynamicLights.Add(sun);

            // Лучи: открытый конус от экрана к полу.
            var shaft = new GameObject("God Rays");
            shaft.transform.SetParent(root.transform, false);
            shaft.AddComponent<MeshFilter>().sharedMesh = BuildCone(radius * 0.92f, radius * 1.1f, height, 28);
            var shaftRenderer = shaft.AddComponent<MeshRenderer>();
            shaftRenderer.sharedMaterial = _shaftMaterial;
            shaftRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shaftRenderer.receiveShadows = false;

            return new Oasis
            {
                Cell = info.Cell,
                Info = info,
                Root = root,
                Sun = sun,
                Shaft = shaftRenderer,
                Rain = CreateRain(root.transform, radius, height),
            };
        }

        private ParticleSystem CreateRain(Transform parent, float radius, float height)
        {
            var material = PropManager.Instance != null ? PropManager.Instance.DustMaterial : null;
            if (material == null)
                return null;
            var go = new GameObject("Rain");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, height - 0.2f, 0f);
            var rain = go.AddComponent<ParticleSystem>();
            rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            const float speed = 11f;
            var main = rain.main;
            main.loop = true;
            main.startSpeed = speed;
            main.startLifetime = (height - 0.2f) / speed;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.04f);
            main.startColor = new Color(0.75f, 0.85f, 1f, 0.45f);
            main.maxParticles = 1500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = rain.emission;
            emission.rateOverTime = 260f * radius / 4f;
            var shape = rain.shape;
            // Тонкий квадрат внутри круга дыры, капли летят по локальной +Z — повёрнутой вниз.
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 1.4f, radius * 1.4f, 0.1f);
            shape.rotation = new Vector3(90f, 0f, 0f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 4f;
            renderer.velocityScale = 0.04f;
            return rain;
        }

        private void UpdateOasis(ChunkManager world, Oasis oasis, float sky)
        {
            // Днём экран — как небо, ночью темно (немного «лунного» света остаётся).
            oasis.Sun.intensity = Mathf.Lerp(0.2f, 9f, Mathf.InverseLerp(0.03f, 1f, sky));
            oasis.Shaft.sharedMaterial.SetFloat("_Intensity", Mathf.Lerp(0.05f, 1.3f, Mathf.InverseLerp(0.03f, 1f, sky)));
            if (oasis.Rain == null)
                return;
            bool raining = IsRaining(world.Seed, oasis.Cell);
            if (raining && !oasis.Rain.isEmitting)
                oasis.Rain.Play();
            else if (!raining && oasis.Rain.isEmitting)
                oasis.Rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>Идёт ли дождь в оазисе ячейки: по игровым часам, около четверти времени.</summary>
        public static bool IsRaining(uint seed, int2 cell)
        {
            double hours = WorldClock.Instance != null ? WorldClock.Instance.TotalHours : 0.0;
            int hour = (int)System.Math.Floor(hours);
            return Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, hour + 7700)) < RainChance;
        }

        private static void DestroyOasis(Oasis oasis)
        {
            if (oasis.Sun != null)
                LightingDirector.DynamicLights.Remove(oasis.Sun);
            if (oasis.Shaft != null)
                Destroy(oasis.Shaft.GetComponent<MeshFilter>().sharedMesh);
            if (oasis.Root != null)
                Destroy(oasis.Root);
        }

        // ───────────────────────────── Меши ─────────────────────────────

        /// <summary>Квадрат 1×1 в плоскости XY, uv −1…1.</summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "FlareQuad" };
            mesh.SetVertices(new List<Vector3> { new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f), new(0.5f, 0.5f, 0f), new(-0.5f, 0.5f, 0f) });
            mesh.SetUVs(0, new List<Vector2> { new(-1f, -1f), new(1f, -1f), new(1f, 1f), new(-1f, 1f) });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Открытый конус (усечённый): низ радиуса bottom на y = 0, верх радиуса top на y = height.</summary>
        private static Mesh BuildCone(float top, float bottom, float height, int segments)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices.Add(dir * bottom);
                vertices.Add(dir * top + Vector3.up * height);
                normals.Add(dir);
                normals.Add(dir);
                uvs.Add(new Vector2((float)i / segments, 0f));
                uvs.Add(new Vector2((float)i / segments, 1f));
                if (i > 0)
                {
                    int b = i * 2;
                    triangles.Add(b - 2);
                    triangles.Add(b - 1);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(b - 1);
                    triangles.Add(b + 1);
                }
            }
            var mesh = new Mesh { name = "GodRays" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
