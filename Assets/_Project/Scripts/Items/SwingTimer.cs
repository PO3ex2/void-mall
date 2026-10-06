namespace VoidMall.Items
{
    /// <summary>
    /// Перезарядка удара инструментом или рукой. Отсчёт идёт и когда кнопка отпущена, но не сбрасывается:
    /// раньше отпускание обнуляло таймер, и частые клики били сразу — быстрее, чем удержание.
    /// </summary>
    public struct SwingTimer
    {
        private float _remaining;

        /// <summary>Кадр: true — удар. pressed — кнопка удара зажата.</summary>
        public bool Tick(bool pressed, float dt, float interval)
        {
            if (_remaining > 0f)
                _remaining -= dt;
            if (!pressed || _remaining > 0f)
                return false;
            _remaining += interval;
            if (_remaining < 0f)
                _remaining = interval;
            return true;
        }
    }
}
