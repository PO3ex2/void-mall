using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Упорядоченный список материалов мира. Первый элемент обязан быть воздухом.</summary>
    [CreateAssetMenu(menuName = "Void Mall/World/Material Catalog", fileName = "MaterialCatalog")]
    public sealed class MaterialCatalog : ScriptableObject
    {
        public List<MaterialDef> Materials = new();
    }
}
