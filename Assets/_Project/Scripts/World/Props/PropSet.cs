using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Набор пропов мира (порядок = индекс в PropSpawn.Def).</summary>
    [CreateAssetMenu(menuName = "Void Mall/Props/Prop Set", fileName = "PropSet")]
    public sealed class PropSet : ScriptableObject
    {
        public List<PropDef> Props = new();
    }
}
