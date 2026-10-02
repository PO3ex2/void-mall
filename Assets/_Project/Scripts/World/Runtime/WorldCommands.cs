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

    /// <summary>Авторитет сообщает: клетка изменилась (или изменился её урон).</summary>
    public struct BlockChangedEvent : INetEvent
    {
        public int3 Position;
        public Cell Cell;
        public int Damage;
    }
}
