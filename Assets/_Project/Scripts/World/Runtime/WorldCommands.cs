using Unity.Mathematics;
using VoidMall.Net;

namespace VoidMall.World
{
    /// <summary>Ударить блок: урон копится, при достижении прочности блок ломается.</summary>
    public struct DamageBlockCommand : INetCommand
    {
        public int3 Position;
        public int Amount;
    }

    /// <summary>Поставить блок игроком: только в воздух и не внутрь игрока/пропа.</summary>
    public struct PlaceBlockCommand : INetCommand
    {
        public int3 Position;
        public ushort Material;
    }

    /// <summary>Задать клетку без проверок (консоль, читы, отладка).</summary>
    public struct SetBlockCommand : INetCommand
    {
        public int3 Position;
        public Cell Cell;
    }

    /// <summary>Заполнить параллелепипед (включительно) без проверок.</summary>
    public struct FillBlocksCommand : INetCommand
    {
        public int3 Min;
        public int3 Max;
        public Cell Cell;
    }

    /// <summary>
    /// Бур: сферическая кисть по гладким вокселям. Add = false — срезать, true — наращивать материалом Material.
    /// Strength — доля полного перехода «твёрдое ↔ пусто» за одно применение (до учёта прочности).
    /// Блоки буром не меняются.
    /// </summary>
    public struct SmoothBrushCommand : INetCommand
    {
        public float3 Center;
        public float Radius;
        public float Strength;
        public bool Add;
        public ushort Material;
    }

    /// <summary>Авторитет сообщает: клетка изменилась (или изменился её урон).</summary>
    public struct BlockChangedEvent : INetEvent
    {
        public int3 Position;
        public Cell Cell;
        public int Damage;
    }
}
