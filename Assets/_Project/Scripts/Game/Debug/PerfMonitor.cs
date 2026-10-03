using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Player;

namespace VoidMall.Game
{
    /// <summary>
    /// Запись кадров для поиска лагов: время кадра, мусор для GC и сборки, время наших систем (метки Perf) и
    /// встроенных меток Unity. Команды: perf_record (запись, пока бегаешь сам), perf_run (автоматический
    /// забег по прямой), perf_markers (какие метки Unity есть). Отчёт — в консоль и в файл.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class PerfMonitor : MonoBehaviour
    {
        private const int MaxFrames = 20000;

        /// <summary>Встроенные метки Unity, которые полезно видеть рядом с нашими (какие есть — решает версия Unity).</summary>
        private static readonly string[] UnityMarkers =
        {
            "GC.Collect", "Physics.Simulate", "Physics.SyncTransforms", "Physics.BakeMesh", "MeshCollider.Bake",
            "Mesh.UploadMeshData", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "FixedUpdate.ScriptRunBehaviourFixedUpdate", "FixedUpdate.PhysicsFixedUpdate", "Gfx.WaitForPresentOnGfxThread",
            "Gfx.WaitForRenderThread", "PostLateUpdate.FinishFrameRendering", "Inl_RenderCameraStack", "UIR.Update",
            "UpdateRendererBoundingVolumes", "Instantiate", "Loading.AwakeFromLoad", "Shader.CreateGPUProgram", "Shadows.RenderShadowMap", "Shadows.PrepareShadowmap", "Culling",
            "RenderSettings.CalculateReflectionFromSkybox", "ReflectionProbes.Convolution", "UIR.Update", "UIR.RaiseEngineUpdate",
        };

        private static PerfMonitor _instance;

        private ProfilerRecorder _mainThread, _gcAlloc;
        private readonly List<(string name, ProfilerRecorder recorder)> _unity = new();

        private bool _recording;
        private float _stopAt;
        private int _frames;
        private int _gcAtStart;
        private float[] _frameMs;
        private long[] _frameGc;
        private int[] _frameCollections;
        private long[] _probeTicks, _probeBytes;
        private long[] _unityNs;
        private int _probeCount;

        // Значения наших меток прошлого кадра: встроенные счётчики Unity отдают прошлый кадр, сводим их вместе.
        private long[] _pendingTicks, _pendingBytes;
        private bool _hasPending;
        private int _lastCollections;

        // Забег.
        private bool _running;
        private Vector3 _runFrom, _runDirection;
        private float _runDistance, _runSpeed, _runTravelled, _runDelay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (_instance != null)
                return;
            var go = new GameObject("PerfMonitor");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PerfMonitor>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private void OnEnable()
        {
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var name in UnityMarkers)
            {
                foreach (var handle in handles)
                {
                    var description = ProfilerRecorderHandle.GetDescription(handle);
                    if (description.Name != name)
                        continue;
                    _unity.Add((name, ProfilerRecorder.StartNew(description.Category, name, 1)));
                    break;
                }
            }
        }

        private void OnDisable()
        {
            _mainThread.Dispose();
            _gcAlloc.Dispose();
            foreach (var (_, recorder) in _unity)
                recorder.Dispose();
            _unity.Clear();
        }

        private void Update()
        {
            if (!_running)
                return;
            if (_runDelay > 0f)
            {
                // Ждём, пока прогрузится мир вокруг старта, — начальная загрузка не должна попасть в замер.
                _runDelay -= Time.unscaledDeltaTime;
                if (_runDelay <= 0f)
                    Begin(_runDistance / _runSpeed + 5f);
                return;
            }
            var motor = FindPlayer();
            if (motor == null)
            {
                _running = false;
                return;
            }
            _runTravelled = Mathf.Min(_runDistance, _runTravelled + _runSpeed * Time.deltaTime);
            motor.Teleport(_runFrom + _runDirection * _runTravelled);
            if (_runTravelled >= _runDistance)
            {
                _running = false;
                StopAndReport("perf_run");
            }
        }

        private void LateUpdate()
        {
            if (_recording)
                RecordFrame();
            // Снимок наших меток этого кадра — дополнится встроенными счётчиками на следующем.
            int count = Perf.Probes.Count;
            if (_pendingTicks == null || _pendingTicks.Length < count)
            {
                _pendingTicks = new long[Mathf.Max(64, count * 2)];
                _pendingBytes = new long[_pendingTicks.Length];
            }
            for (int i = 0; i < count; i++)
            {
                _pendingTicks[i] = Perf.Probes[i].FrameTicks;
                _pendingBytes[i] = Perf.Probes[i].FrameBytes;
            }
            _hasPending = true;
            Perf.ResetFrame();

            if (_recording && !_running && Time.unscaledTime >= _stopAt)
                StopAndReport("perf_record");
        }

        private void RecordFrame()
        {
            if (!_hasPending || _frames >= MaxFrames)
                return;
            int f = _frames++;
            _frameMs[f] = _mainThread.LastValue / 1_000_000f;
            _frameGc[f] = _gcAlloc.LastValue;
            int collections = System.GC.CollectionCount(0);
            _frameCollections[f] = collections - _lastCollections;
            _lastCollections = collections;
            for (int i = 0; i < _probeCount && i < Perf.Probes.Count; i++)
            {
                _probeTicks[f * _probeCount + i] = _pendingTicks[i];
                _probeBytes[f * _probeCount + i] = _pendingBytes[i];
            }
            for (int i = 0; i < _unity.Count; i++)
                _unityNs[f * _unity.Count + i] = _unity[i].recorder.LastValue;
        }

        private void Begin(float seconds)
        {
            _probeCount = Perf.Probes.Count;
            int frames = Mathf.Min(MaxFrames, Mathf.CeilToInt(seconds * 300f) + 600);
            _frameMs = new float[frames];
            _frameGc = new long[frames];
            _frameCollections = new int[frames];
            _probeTicks = new long[frames * _probeCount];
            _probeBytes = new long[frames * _probeCount];
            _unityNs = new long[frames * Mathf.Max(1, _unity.Count)];
            _frames = 0;
            _hasPending = false;
            _gcAtStart = System.GC.CollectionCount(0);
            _lastCollections = _gcAtStart;
            _stopAt = Time.unscaledTime + seconds;
            _recording = true;
        }

        private static PlayerMotor FindPlayer() => FindAnyObjectByType<PlayerMotor>();

        // ───────────────────────────── Отчёт ─────────────────────────────

        private void StopAndReport(string title)
        {
            _recording = false;
            string report = BuildReport(title);
            DevConsole.Log(report);
            string path = Path.Combine(Application.isEditor ? Path.Combine(Application.dataPath, "..", "Temp") : Application.persistentDataPath, "VoidMallPerf.txt");
            File.WriteAllText(path, report);
            DevConsole.Log($"Отчёт сохранён: {Path.GetFullPath(path)}");
        }

        private string BuildReport(string title)
        {
            int n = _frames;
            var sb = new StringBuilder();
            sb.AppendLine($"=== {title}: {n} кадров ===");
            if (n == 0)
                return sb.ToString();

            var sorted = new float[n];
            System.Array.Copy(_frameMs, sorted, n);
            System.Array.Sort(sorted);
            double total = 0;
            int over33 = 0, over50 = 0;
            long gcTotal = 0;
            int collections = 0;
            for (int f = 0; f < n; f++)
            {
                total += _frameMs[f];
                if (_frameMs[f] > 33.3f) over33++;
                if (_frameMs[f] > 50f) over50++;
                gcTotal += _frameGc[f];
                collections += _frameCollections[f];
            }
            sb.AppendLine($"Кадр (основной поток), мс: среднее {total / n:0.0}, медиана {sorted[n / 2]:0.0}, 95% {sorted[(int)(n * 0.95f)]:0.0}, 99% {sorted[(int)(n * 0.99f)]:0.0}, худший {sorted[n - 1]:0.0}");
            sb.AppendLine($"Кадров дольше 33 мс (<30 FPS): {over33}, дольше 50 мс: {over50}");
            sb.AppendLine($"Мусор для GC: всего {gcTotal / 1048576.0:0.0} МБ, в среднем {gcTotal / (double)n / 1024.0:0.0} КБ за кадр; сборок мусора: {collections}");

            sb.AppendLine();
            sb.AppendLine("Наши системы (среднее мс / худший мс / мусор КБ за кадр в среднем / всего мусора МБ):");
            var order = new List<int>();
            for (int i = 0; i < _probeCount; i++)
                order.Add(i);
            order.Sort((a, b) => SumTicks(b).CompareTo(SumTicks(a)));
            foreach (int i in order)
            {
                long sumTicks = SumTicks(i), sumBytes = 0, maxTicks = 0;
                for (int f = 0; f < n; f++)
                {
                    sumBytes += _probeBytes[f * _probeCount + i];
                    maxTicks = System.Math.Max(maxTicks, _probeTicks[f * _probeCount + i]);
                }
                if (sumTicks == 0 && sumBytes == 0)
                    continue;
                sb.AppendLine($"  {Perf.Probes[i].Name,-34} {Perf.TicksToMs(sumTicks) / n,6:0.00} {Perf.TicksToMs(maxTicks),7:0.0} {sumBytes / (double)n / 1024.0,8:0.0} {sumBytes / 1048576.0,7:0.0}");
            }

            if (_unity.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Метки Unity (среднее мс / худший мс):");
                for (int u = 0; u < _unity.Count; u++)
                {
                    long sum = 0, max = 0;
                    for (int f = 0; f < n; f++)
                    {
                        long v = _unityNs[f * _unity.Count + u];
                        sum += v;
                        max = System.Math.Max(max, v);
                    }
                    sb.AppendLine($"  {_unity[u].name,-44} {sum / 1e6 / n,6:0.00} {max / 1e6,7:0.0}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Худшие кадры (мс, мусор КБ, сборка GC, кто больше всех):");
            var worst = new List<int>();
            for (int f = 0; f < n; f++)
                worst.Add(f);
            worst.Sort((a, b) => _frameMs[b].CompareTo(_frameMs[a]));
            for (int k = 0; k < Mathf.Min(15, n); k++)
            {
                int f = worst[k];
                sb.Append($"  #{f,-5} {_frameMs[f],6:0.0} мс  {_frameGc[f] / 1024.0,7:0.0} КБ  {(_frameCollections[f] > 0 ? "GC!" : "   ")}  ");
                var top = new List<(double ms, string name)>();
                for (int i = 0; i < _probeCount; i++)
                    top.Add((Perf.TicksToMs(_probeTicks[f * _probeCount + i]), Perf.Probes[i].Name));
                for (int u = 0; u < _unity.Count; u++)
                    top.Add((_unityNs[f * _unity.Count + u] / 1e6, _unity[u].name));
                top.Sort((a, b) => b.ms.CompareTo(a.ms));
                for (int t = 0; t < 5 && t < top.Count && top[t].ms >= 0.5; t++)
                    sb.Append($"{top[t].name} {top[t].ms:0.0}; ");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private long SumTicks(int probe)
        {
            long sum = 0;
            for (int f = 0; f < _frames; f++)
                sum += _probeTicks[f * _probeCount + probe];
            return sum;
        }

        // ───────────────────────────── Команды ─────────────────────────────

        [ConsoleCommand("perf_record", "Записать N секунд (бегай сам), затем отчёт о лагах: perf_record [секунды].")]
        private static string Record(float seconds = 20f)
        {
            if (_instance == null)
                return "Нет PerfMonitor.";
            _instance.Begin(seconds);
            return $"Запись {seconds:0} с…";
        }

        [ConsoleCommand("perf_run", "Забег по прямой взгляда с записью и отчётом: perf_run [метров] [м/с] [задержка, с].")]
        private static string Run(float distance = 600f, float speed = 9f, float delay = 0f)
        {
            if (_instance == null)
                return "Нет PerfMonitor.";
            var motor = FindPlayer();
            var camera = Camera.main;
            if (motor == null || camera == null)
                return "Нет игрока.";
            Vector3 direction = camera.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f)
                return "Смотри вдоль пола.";
            _instance._runDirection = direction.normalized;
            _instance._runFrom = motor.transform.position;
            _instance._runDistance = distance;
            _instance._runSpeed = speed;
            _instance._runTravelled = 0f;
            _instance._running = true;
            _instance._runDelay = delay;
            if (delay <= 0f)
                _instance.Begin(distance / speed + 5f);
            return $"Забег {distance:0} м со скоростью {speed:0.#} м/с" + (delay > 0f ? $" через {delay:0} с…" : "…");
        }

        [ConsoleCommand("perf_markers", "Список встроенных меток Unity, доступных для записи: perf_markers [фильтр].")]
        private static string Markers(string filter = "")
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var sb = new StringBuilder();
            int count = 0;
            foreach (var handle in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(handle);
                if (filter.Length > 0 && d.Name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (count++ < 200)
                    sb.AppendLine($"{d.Category.Name}: {d.Name}");
            }
            sb.Insert(0, $"Меток: {count}\n");
            return sb.ToString();
        }
    }
}
