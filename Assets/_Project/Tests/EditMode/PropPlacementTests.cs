using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using VoidMall.World;

namespace VoidMall.Tests
{
    public sealed class PropPlacementTests
    {
        private static readonly string[] Ids =
        {
            "prop.shelf", "prop.fridge", "prop.checkout", "prop.display_table", "prop.clothes_rack", "prop.table", "prop.chair",
            "prop.pallet_rack", "prop.box_stack", "prop.car", "prop.cabinet", "prop.boiler", "prop.bench", "prop.planter",
            "prop.sofa", "prop.cardboard_box", "prop.plastic_crate", "prop.trash_bin", "prop.switchboard", "prop.cable_reel", "prop.vent_unit",
        };

        private PropSet _set;
        private PropRegistry _registry;
        private PropIds _ids;
        private NativeArray<SectionInfo> _sections;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _set = ScriptableObject.CreateInstance<PropSet>();
            _created.Add(_set);
            for (int i = 0; i < Ids.Length; i++)
            {
                var def = ScriptableObject.CreateInstance<PropDef>();
                var so = new SerializedObject(def);
                so.FindProperty("_id").stringValue = Ids[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                def.Shape = (PropShape)i;
                def.Size = PropMeshes.DefaultSize(def.Shape);
                _set.Props.Add(def);
                _created.Add(def);
            }
            _registry = new PropRegistry(_set);
            _ids = PropIds.Resolve(_registry);

            _sections = new NativeArray<SectionInfo>(5, Allocator.Persistent);
            _sections[0] = new SectionInfo { Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 1f };
            _sections[1] = new SectionInfo { Layout = SectionLayout.Clothing, RetailFloorMask = 0b11111, Weight = 1f };
            _sections[2] = new SectionInfo { Layout = SectionLayout.Hall, RetailFloorMask = 0b11111, Weight = 1f };
            _sections[3] = new SectionInfo { Layout = SectionLayout.Warehouse, RetailFloorMask = 0b11111, Service = true, Weight = 1f };
            _sections[4] = new SectionInfo { Layout = SectionLayout.Tech, RetailFloorMask = 0, Service = true, Weight = 1f };
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Dispose();
            _sections.Dispose();
            foreach (var obj in _created)
                Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void Placement_IsDeterministic_WithUniqueIds()
        {
            var a = Place(7, new int2(2, -3), 3);
            var b = Place(7, new int2(2, -3), 3);
            Assert.That(a.Count, Is.GreaterThan(20));
            Assert.That(b.Count, Is.EqualTo(a.Count));
            var ids = new HashSet<ulong>();
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Id, Is.EqualTo(a[i].Id));
                Assert.That(b[i].Position, Is.EqualTo(a[i].Position));
                Assert.That(ids.Add(a[i].Id), Is.True, "ID пропов уникальны");
            }
        }

        [Test]
        public void Props_DoNotOverlap_AndStayInCell()
        {
            for (int level = 0; level < MallLayout.LevelCount; level++)
            {
                var cell = new int2(1, 4);
                var props = Place(11, cell, level);
                float2 origin = MallLayout.CellOrigin(cell);
                var boxes = new List<float4>();
                foreach (var p in props)
                {
                    Vector3 size = _registry.Size(p.Def);
                    bool swap = Mathf.Abs(Mathf.Repeat(p.Yaw, 180f) - 90f) < 1f;
                    float2 half = (swap ? new float2(size.z, size.x) : new float2(size.x, size.z)) * 0.5f;
                    float2 c = new(p.Position.x, p.Position.z);
                    Assert.That(c.x - half.x, Is.GreaterThanOrEqualTo(origin.x - 0.01f));
                    Assert.That(c.y + half.y, Is.LessThanOrEqualTo(origin.y + MallLayout.CellSize + 0.01f));
                    Assert.That(p.Position.y, Is.EqualTo(MallLayout.FloorTop(level)));
                    var box = new float4(c - half + 0.01f, c + half - 0.01f);
                    foreach (var other in boxes)
                        Assert.That(box.x < other.z && other.x < box.z && box.y < other.w && other.y < box.w, Is.False,
                            $"level {level}: пропы пересекаются ({_registry.Get(p.Def).Id} в {p.Position})");
                    boxes.Add(box);
                }
            }
        }

        private List<PropSpawn> Place(uint seed, int2 cell, int level)
        {
            var info = MallPlanner.GetCellInfo(seed, 0f, cell);
            var plan = new MallLevelPlan(Allocator.Temp);
            var output = new List<PropSpawn>();
            try
            {
                MallPlanner.BuildLevel(seed, info, level, _sections, ref plan);
                PropPlacer.Place(seed, info, plan, _sections, _registry, _ids, output);
            }
            finally
            {
                plan.Dispose();
            }
            return output;
        }
    }
}
