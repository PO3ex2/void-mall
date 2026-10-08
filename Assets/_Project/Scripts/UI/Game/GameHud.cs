namespace VoidMall.UI
{
    /// <summary>Игровой HUD (показатели, хотбар) виден: в лобби (этап 10) он скрыт, и пока игрок работает за ПК.</summary>
    public static class GameHud
    {
        private static bool _visible = true;

        /// <summary>HUD включён (лобби его выключает); с учётом <see cref="Suppressed"/>.</summary>
        public static bool Visible
        {
            get => _visible && !Suppressed;
            set => _visible = value;
        }

        /// <summary>Временно скрыт поверх всего (камера у экрана ПК).</summary>
        public static bool Suppressed;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _visible = true;
            Suppressed = false;
        }
    }
}
