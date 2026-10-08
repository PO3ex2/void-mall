using UnityEngine;
using UnityEngine.InputSystem;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Player;

namespace VoidMall.Game
{
    /// <summary>
    /// ПК-консоль (этап 10): в лобби и (редко) в мире. E у монитора — камера подлетает к экрану, ввод переключается на
    /// режим ПК (курсор), Esc — назад. Экран — текстура, в которую рисует ОС (PcOs, UI Toolkit); клики мыши переводятся
    /// в координаты экрана лучом по коллайдеру экрана. Сломанный ПК — тёмный экран, не включается.
    /// </summary>
    public sealed class PcTerminal : MonoBehaviour
    {
        public const int ScreenWidth = 1024, ScreenHeight = 768;
        private const float Reach = 2.6f;
        private const float FlyTime = 0.55f;

        /// <summary>Камера за ПК: на таком расстоянии от экрана и с таким углом обзора экран занимает почти весь вид.</summary>
        private const float ViewDistance = 0.34f, ViewFov = 45f;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Renderer _screen;
        [SerializeField] private Collider _screenCollider;

        [Tooltip("Куда встаёт камера при работе за ПК (смотрит в экран).")]
        [SerializeField] private Transform _viewPoint;

        [SerializeField] private Texture2D _logo;
        [SerializeField] private bool _inWorld;
        [SerializeField] private bool _broken;

        /// <summary>ПК, за которым сейчас работают (null — ни за каким).</summary>
        public static PcTerminal Active { get; private set; }

        /// <summary>ПК под прицелом (для подсказки «E — включить»).</summary>
        public static PcTerminal Hovered { get; private set; }

        public bool InWorld => _inWorld;
        public bool Broken => _broken;
        public Texture2D Logo => _logo;
        public Collider ScreenCollider => _screenCollider;
        public RenderTexture Texture { get; private set; }

        private InputService _input;
        private InputAction _interact, _exit;
        private Material _material;
        private PcOs _os;
        private AudioSource _hum;
        private float _blend;
        private bool _using;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = Hovered = null;

        public void Configure(Renderer screen, Collider screenCollider, Transform viewPoint, Texture2D logo, bool inWorld, bool broken)
        {
            _screen = screen;
            _screenCollider = screenCollider;
            _viewPoint = viewPoint;
            _logo = logo;
            _inWorld = inWorld;
            _broken = broken;
        }

        private void Start()
        {
            Services.TryGet(out _input);
            if (_input != null)
            {
                _interact = _input.Action(InputService.Maps.Player, "Interact");
                _exit = _input.Action(InputService.Maps.Terminal, "Exit");
            }
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "PC Screen" };
            ShowStandby();
            if (_screen != null)
                _screen.sharedMaterial = _material;
        }

        private void OnDestroy()
        {
            if (Active == this)
                Leave();
            // Камера не должна остаться «у экрана», если ПК исчез (разобрали, выгрузили) посреди работы.
            if (_blend > 0f && PlayerCameraRig.Local != null)
                PlayerCameraRig.Local.SetOverride(Vector3.zero, Quaternion.identity, 0f);
            if (Hovered == this)
                Hovered = null;
            if (_os != null)
                Destroy(_os.gameObject);
            if (_hum != null)
                Destroy(_hum);
            if (_material != null)
                Destroy(_material);
            if (Texture != null)
                Texture.Release();
        }

        /// <summary>Экран без ОС: логотип на тёмном (выключенный — просто чёрный).</summary>
        private void ShowStandby()
        {
            if (_material == null)
                return;
            _material.SetTexture(BaseMapId, _broken ? null : _logo);
            _material.SetColor(BaseColorId, _broken ? new Color(0.02f, 0.02f, 0.025f) : new Color(0.35f, 0.42f, 0.5f));
        }

        private void Update()
        {
            UpdateHover();
            if (_using && _exit != null && _exit.WasPressedThisFrame() && (_os == null || !_os.HandleEscape()))
                Leave();

            _blend = Mathf.MoveTowards(_blend, _using ? 1f : 0f, Time.deltaTime / FlyTime);
            var rig = PlayerCameraRig.Local;
            if (rig != null && (_using || _blend > 0f))
            {
                // Перед экраном (квад смотрит в -forward): по нормали экрана на ViewDistance.
                Vector3 position = _screen != null ? _screen.transform.position - _screen.transform.forward * ViewDistance : _viewPoint.position;
                Quaternion rotation = _screen != null ? Quaternion.LookRotation(_screen.transform.forward, Vector3.up) : _viewPoint.rotation;
                rig.SetOverride(position, rotation, _blend, ViewFov);
            }
        }

        private void UpdateHover()
        {
            bool hovered = false;
            var camera = Camera.main;
            if (!_using && camera != null && _input != null && _input.Current == InputContext.Gameplay)
            {
                var ray = new Ray(camera.transform.position, camera.transform.forward);
                if (Physics.Raycast(ray, out var hit, Reach, ~(1 << GameLayers.Player), QueryTriggerInteraction.Ignore)
                    && hit.collider.GetComponentInParent<PcTerminal>() == this)
                {
                    hovered = true;
                    // ПК в мире стоит на пропе — нажатие E разбирает PropInteractor (коротко — включить, удержание — взять).
                    if (!_inWorld && _interact != null && _interact.WasPressedThisFrame())
                        Enter();
                }
            }
            if (hovered)
                Hovered = this;
            else if (Hovered == this)
                Hovered = null;
        }

        public void Enter()
        {
            if (_broken || _using || Active != null)
                return;
            Active = this;
            _using = true;
            VoidMall.UI.GameHud.Suppressed = true;
            _input?.Push(InputContext.Terminal);
            if (_os == null)
            {
                // Со стенсилом: им UI Toolkit обрезает скруглённые блоки (без него поле ввода рисуется белой плашкой).
                Texture = new RenderTexture(ScreenWidth, ScreenHeight, 24) { name = "PC Screen" };
                var go = new GameObject("PC OS");
                go.transform.SetParent(transform, false);
                go.SetActive(false);
                _os = go.AddComponent<PcOs>();
                _os.Setup(this);
                go.SetActive(true);
                _hum = gameObject.AddComponent<AudioSource>();
                _hum.clip = PcSounds.Hum;
                _hum.loop = true;
                _hum.spatialBlend = 1f;
                _hum.maxDistance = 6f;
                _hum.volume = 0.6f;
                _hum.Play();
            }
            _material.SetTexture(BaseMapId, Texture);
            _material.SetColor(BaseColorId, Color.white);
            _os.Wake();
        }

        public void Leave()
        {
            if (!_using)
                return;
            _using = false;
            if (Active == this)
                Active = null;
            VoidMall.UI.GameHud.Suppressed = false;
            _os?.ReleasePointer();
            _input?.Pop(InputContext.Terminal);
        }

        /// <summary>Точка экрана (в пикселях окна) → координаты панели ОС; NaN — мимо экрана.</summary>
        public Vector2 ScreenToPanel(Vector2 screenPosition)
        {
            var camera = Camera.main;
            if (camera == null || _screen == null)
                return new Vector2(float.NaN, float.NaN);
            var ray = camera.ScreenPointToRay(new Vector3(screenPosition.x, Screen.height - screenPosition.y, 0f));
            // Луч — в плоскость экрана (квад 1×1 в своих осях): коллайдер не нужен, у ПК-пропа его и нет.
            var screen = _screen.transform;
            if (!new Plane(screen.forward, screen.position).Raycast(ray, out float distance))
                return new Vector2(float.NaN, float.NaN);
            Vector3 local = screen.InverseTransformPoint(ray.GetPoint(distance));
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
                return new Vector2(float.NaN, float.NaN);
            var uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
            return new Vector2(uv.x * ScreenWidth, (1f - uv.y) * ScreenHeight);
        }

        /// <summary>Звук на ПК (щелчок, аккорд включения).</summary>
        public void Play(AudioClip clip, float volume = 1f)
        {
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, _screen != null ? _screen.transform.position : transform.position, volume);
        }
    }
}
