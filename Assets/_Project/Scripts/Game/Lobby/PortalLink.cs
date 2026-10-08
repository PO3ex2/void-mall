using System;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Player;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Портал лобби ↔ мир (этап 10): два проёма (A — в лобби, B — в мире), у каждого «вперёд» — направление прохода.
    /// Одна камера рисует вид по ту сторону того проёма, у которого стоит игрок: поза камеры игрока, перенесённая через
    /// пару проёмов, и наклонная ближняя плоскость по проёму выхода. Проём берёт картинку по экранным координатам —
    /// виден параллакс. Пересёк плоскость глазами — игрок, его скорость и взгляд переносятся на другую сторону в тот же
    /// кадр (камера тоже), без вспышки. Пока мир грузится, проём закрыт (рябь); потом открывается волной; схлопывается
    /// по Collapse.
    /// </summary>
    [DefaultExecutionOrder(100)] // после PlayerCameraRig: камера игрока уже на месте
    public sealed class PortalLink : MonoBehaviour
    {
        public const float Width = 2f, Height = 3f;

        /// <summary>
        /// Глаз ближе этого к плоскости проёма — плоскость отодвигается на SurfaceDepth вглубь: иначе её срезает ближняя
        /// плоскость камеры и на кадр видно, что за порталом (мерцание на проходе). Картинка берётся по экранным
        /// координатам, так что сдвиг не виден, а край проёма в этот момент далеко за пределами экрана.
        /// </summary>
        private const float NearZone = 0.3f, SurfaceDepth = 0.2f;

        /// <summary>Ближе этого к проёму камера портала рисует без наклонной ближней плоскости.</summary>
        private const float ObliqueMinDistance = 0.2f;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int OpenId = Shader.PropertyToID("_Open");
        private static readonly int CollapseId = Shader.PropertyToID("_Collapse");

        [SerializeField] private Transform _lobby;
        [SerializeField] private Renderer _lobbySurface;

        private Transform _world;
        private Renderer _worldSurface;
        private Vector3 _lobbySurfaceBase, _worldSurfaceBase;
        private Material _lobbyMaterial, _worldMaterial;
        private Camera _portalCamera;
        private RenderTexture _texture;
        private PlayerMotor _motor;
        private PlayerLook _look;
        private Camera _playerCamera;
        private Vector3 _lastEye;
        private bool _hasLastEye;
        private float _open, _openTarget;
        private float _collapse = -1f;

        /// <summary>Игрок по эту сторону — в мире.</summary>
        public bool PlayerInWorld { get; private set; }

        /// <summary>Проём открыт полностью (можно пройти).</summary>
        public bool IsOpen => _open >= 0.99f && _collapse < 0f;
        public bool Collapsed { get; private set; }

        /// <summary>Игрок прошёл: true — в мир, false — обратно в лобби.</summary>
        public event Action<bool> Crossed;

        public Transform LobbyEnd => _lobby;
        public Transform WorldEnd => _world;

        public void Configure(Transform lobby, Renderer lobbySurface)
        {
            _lobby = lobby;
            _lobbySurface = lobbySurface;
        }

        private void Awake()
        {
            if (_lobbySurface != null)
            {
                _lobbyMaterial = _lobbySurface.material;
                _lobbySurfaceBase = _lobbySurface.transform.localPosition;
                _lobbySurface.gameObject.layer = GameLayers.Portal;
            }
            var go = new GameObject("Portal Camera");
            go.transform.SetParent(transform, false);
            _portalCamera = go.AddComponent<Camera>();
            _portalCamera.enabled = false;
            _portalCamera.cullingMask = ~(1 << GameLayers.Portal);
        }

        private void OnDestroy()
        {
            if (LightingDirector.ViewerOverride == _portalCamera.transform)
                LightingDirector.ViewerOverride = null;
            if (_texture != null)
                _texture.Release();
            if (_lobbyMaterial != null)
                Destroy(_lobbyMaterial);
            if (_worldMaterial != null)
                Destroy(_worldMaterial);
        }

        /// <summary>Поставить проём в мире (лицом по направлению выхода) с рамкой; surface — плоскость проёма.</summary>
        public void PlaceWorldEnd(Transform world, Renderer surface)
        {
            _world = world;
            _worldSurface = surface;
            _worldSurfaceBase = surface.transform.localPosition;
            _worldMaterial = surface.material;
            surface.gameObject.layer = GameLayers.Portal;
            ApplyMaterials();
        }

        public void SetOpen(bool open) => _openTarget = open ? 1f : 0f;

        public void Collapse()
        {
            if (_collapse < 0f)
                _collapse = 0f;
        }

        public void SetPlayer(PlayerMotor motor, PlayerLook look, Camera camera, bool inWorld)
        {
            _motor = motor;
            _look = look;
            _playerCamera = camera;
            PlayerInWorld = inWorld;
            _hasLastEye = false;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            _open = Mathf.MoveTowards(_open, _openTarget, dt * 0.8f);
            if (_collapse >= 0f && !Collapsed)
            {
                _collapse = Mathf.Min(1f, _collapse + dt / 1.5f);
                if (_collapse >= 1f)
                    Finish();
            }
            ApplyMaterials();
            if (Collapsed || _playerCamera == null || _lobby == null || _world == null)
                return;

            CheckCrossing();
            KeepSurfaceAhead(_lobbySurface, _lobbySurfaceBase, _lobby.rotation);
            KeepSurfaceAhead(_worldSurface, _worldSurfaceBase, _world.rotation * Quaternion.Euler(0f, 180f, 0f));
            RenderView();
        }

        private void ApplyMaterials()
        {
            foreach (var material in new[] { _lobbyMaterial, _worldMaterial })
            {
                if (material == null)
                    continue;
                material.SetFloat(OpenId, _open);
                material.SetFloat(CollapseId, Mathf.Max(0f, _collapse));
                if (_texture != null)
                    material.SetTexture(MainTexId, _texture);
            }
        }

        private void CheckCrossing()
        {
            Vector3 eye = _playerCamera.transform.position;
            if (_hasLastEye && IsOpen)
            {
                if (!PlayerInWorld && PortalMath.Crossed(_lastEye, eye, _lobby.position, _lobby.rotation, Width, Height))
                    Teleport(_lobby, _world, true);
                else if (PlayerInWorld && PortalMath.Crossed(_lastEye, eye, _world.position, _world.rotation * Quaternion.Euler(0f, 180f, 0f), Width, Height))
                    Teleport(_world, _lobby, false);
            }
            _lastEye = _playerCamera.transform.position;
            _hasLastEye = true;
        }

        private void Teleport(Transform from, Transform to, bool toWorld)
        {
            Vector3 feet = PortalMath.MapPoint(_motor.State.Position, from.position, from.rotation, to.position, to.rotation);
            Vector3 velocity = PortalMath.MapDirection(_motor.State.Velocity, from.rotation, to.rotation);
            float yawDelta = to.rotation.eulerAngles.y - from.rotation.eulerAngles.y;
            var cameraPose = _playerCamera.transform;
            Vector3 cameraPosition = PortalMath.MapPoint(cameraPose.position, from.position, from.rotation, to.position, to.rotation);
            Quaternion cameraRotation = PortalMath.MapRotation(cameraPose.rotation, from.rotation, to.rotation);

            _motor.Teleport(feet);
            _motor.SetVelocity(velocity);
            _look.SetAngles(_look.Yaw + yawDelta, _look.Pitch);
            // Тот же кадр — камера уже на той стороне (риг поставит её сам со следующего кадра).
            cameraPose.SetPositionAndRotation(cameraPosition, cameraRotation);
            PlayerInWorld = toWorld;
            _lastEye = cameraPosition;
            Crossed?.Invoke(toWorld);
        }

        /// <summary>Отодвинуть плоскость проёма от глаза, подошедшего вплотную (entry — поворот проёма по направлению входа).</summary>
        private void KeepSurfaceAhead(Renderer surface, Vector3 baseLocal, Quaternion entry)
        {
            if (surface == null)
                return;
            var t = surface.transform;
            Vector3 eye = PortalMath.Local(_playerCamera.transform.position, t.parent.position, entry);
            bool close = eye.z < 0f && eye.z > -NearZone && Mathf.Abs(eye.x) < Width * 0.5f + NearZone;
            Vector3 away = t.parent.InverseTransformDirection(entry * Vector3.forward);
            t.localPosition = close ? baseLocal + away * SurfaceDepth : baseLocal;
        }

        /// <summary>Вид по ту сторону проёма, у которого игрок: камера игрока, перенесённая через пару проёмов.</summary>
        private void RenderView()
        {
            var near = PlayerInWorld ? _world : _lobby;
            var far = PlayerInWorld ? _lobby : _world;
            var surface = PlayerInWorld ? _worldSurface : _lobbySurface;
            bool visible = surface != null && surface.isVisible && _open > 0.01f;
            _portalCamera.enabled = visible;
            // Из лобби лампы мира светят вокруг камеры портала — мир в проёме освещён так же, как после прохода.
            LightingDirector.ViewerOverride = PlayerInWorld ? null : _portalCamera.transform;
            var main = _playerCamera;
            var view = main.transform;
            // У проёма в мире игрок стоит с обратной стороны (по его forward): переносим через развёрнутые на 180° пары.
            Quaternion nearRot = PlayerInWorld ? near.rotation * Quaternion.Euler(0f, 180f, 0f) : near.rotation;
            Quaternion farRot = PlayerInWorld ? far.rotation * Quaternion.Euler(0f, 180f, 0f) : far.rotation;
            Vector3 position = PortalMath.MapPoint(view.position, near.position, nearRot, far.position, farRot);
            Quaternion rotation = PortalMath.MapRotation(view.rotation, nearRot, farRot);
            _portalCamera.transform.SetPositionAndRotation(position, rotation);
            if (!visible)
                return;

            EnsureTexture();
            _portalCamera.fieldOfView = main.fieldOfView;
            _portalCamera.nearClipPlane = main.nearClipPlane;
            _portalCamera.farClipPlane = main.farClipPlane;
            _portalCamera.aspect = main.aspect;
            _portalCamera.depth = main.depth - 1f;
            _portalCamera.targetTexture = _texture;
            _portalCamera.ResetProjectionMatrix();
            Vector3 planeNormal = farRot * Vector3.forward;
            // Вплотную к проёму наклонная плоскость проходит почти через камеру — матрица вырождается (кадр каши). Там
            // хватает обычной: между камерой и проёмом ничего нет.
            if (Mathf.Abs(Vector3.Dot(planeNormal, far.position - position)) > ObliqueMinDistance)
            {
                var clip = PortalMath.ClipPlane(_portalCamera.worldToCameraMatrix, far.position, planeNormal, position);
                _portalCamera.projectionMatrix = _portalCamera.CalculateObliqueMatrix(clip);
            }
        }

        private void EnsureTexture()
        {
            if (_texture != null && _texture.width == Screen.width && _texture.height == Screen.height)
                return;
            if (_texture != null)
                _texture.Release();
            // HDR (тот же объём, что у обычной): яркие лампы не обрезаются, и bloom главной камеры одинаков по обе стороны.
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.DefaultHDR;
            _texture = new RenderTexture(Mathf.Max(16, Screen.width), Mathf.Max(16, Screen.height), 24, format) { name = "Portal View" };
            ApplyMaterials();
        }

        private void Finish()
        {
            Collapsed = true;
            if (LightingDirector.ViewerOverride == _portalCamera.transform)
                LightingDirector.ViewerOverride = null;
            _portalCamera.enabled = false;
            if (_lobbySurface != null)
                _lobbySurface.enabled = false;
            if (_worldSurface != null)
                _worldSurface.transform.parent.gameObject.SetActive(false);
        }
    }
}
