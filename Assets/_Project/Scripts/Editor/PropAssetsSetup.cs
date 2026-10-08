using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Editor
{
    /// <summary>Ассеты пропов: описания PropDef (масса, прочность, разбор) и набор PropSet для мира.</summary>
    public static class PropAssetsSetup
    {
        public const string PropsFolder = "Assets/_Project/Data/Props";
        public const string PropSetPath = PropsFolder + "/PropSet.asset";

        private readonly struct Spec
        {
            public readonly string Id, Name;
            public readonly PropShape Shape;
            public readonly float Mass, DisassembleTime;
            public readonly int Hardness;
            public readonly Color Debris;
            public readonly (string material, int full, int broken)[] Yields;

            public Spec(string id, string name, PropShape shape, float mass, int hardness, float disassembleTime, Color debris,
                params (string, int, int)[] yields)
            {
                Id = id;
                Name = name;
                Shape = shape;
                Mass = mass;
                Hardness = hardness;
                DisassembleTime = disassembleTime;
                Debris = debris;
                Yields = yields;
            }
        }

        private static readonly Color MetalDust = new(0.55f, 0.57f, 0.6f);
        private static readonly Color WoodDust = new(0.55f, 0.42f, 0.3f);
        private static readonly Color PaperDust = new(0.65f, 0.55f, 0.4f);
        private static readonly Color WhiteDust = new(0.8f, 0.8f, 0.78f);

        /// <summary>Порядок = индекс PropSpawn.Def. Новые пропы добавлять только в конец.</summary>
        private static readonly Spec[] Specs =
        {
            new("prop.shelf", "Стеллаж", PropShape.Shelf, 60f, 400, 6f, MetalDust, ("material.scrap_metal", 6, 3), ("material.screws", 8, 2)),
            new("prop.fridge", "Холодильник", PropShape.Fridge, 120f, 600, 9f, WhiteDust, ("material.scrap_metal", 8, 4), ("material.glass_shard", 3, 1), ("material.wire", 2, 1), ("material.plastic", 3, 1)),
            new("prop.checkout", "Кассовая стойка", PropShape.Checkout, 90f, 500, 7f, WhiteDust, ("material.scrap_metal", 4, 2), ("material.plastic", 4, 2), ("material.wire", 2, 0)),
            new("prop.display_table", "Витринный стол", PropShape.DisplayTable, 35f, 250, 4f, WoodDust, ("material.plank", 4, 2), ("material.screws", 4, 1)),
            new("prop.clothes_rack", "Вешало", PropShape.ClothesRack, 12f, 150, 3f, MetalDust, ("material.scrap_metal", 2, 1), ("material.fabric", 3, 1)),
            new("prop.table", "Стол", PropShape.Table, 18f, 200, 3f, WoodDust, ("material.plank", 3, 1), ("material.scrap_metal", 1, 1)),
            new("prop.chair", "Стул", PropShape.Chair, 5f, 80, 2f, WoodDust, ("material.plank", 2, 1), ("material.scrap_metal", 1, 0)),
            new("prop.pallet_rack", "Паллетный стеллаж", PropShape.PalletRack, 250f, 900, 12f, MetalDust, ("material.scrap_metal", 12, 6), ("material.cardboard", 4, 2), ("material.screws", 10, 3)),
            new("prop.box_stack", "Штабель коробок", PropShape.BoxStack, 30f, 120, 3f, PaperDust, ("material.cardboard", 6, 3)),
            new("prop.car", "Машина", PropShape.Car, 1200f, 3000, 30f, MetalDust, ("material.scrap_metal", 30, 15), ("material.rubber", 4, 2), ("material.glass_shard", 6, 2), ("material.wire", 6, 2)),
            new("prop.cabinet", "Электрощит", PropShape.Cabinet, 70f, 500, 6f, MetalDust, ("material.scrap_metal", 5, 3), ("material.wire", 5, 2)),
            new("prop.boiler", "Бойлер", PropShape.Boiler, 400f, 1500, 15f, MetalDust, ("material.scrap_metal", 15, 8), ("material.pipe", 4, 2)),
            new("prop.bench", "Скамейка", PropShape.Bench, 25f, 250, 4f, WoodDust, ("material.plank", 4, 2), ("material.scrap_metal", 2, 1)),
            new("prop.planter", "Кадка с растением", PropShape.Planter, 60f, 300, 4f, WhiteDust, ("material.plastic", 3, 1)),
            new("prop.sofa", "Диван", PropShape.Sofa, 45f, 200, 5f, PaperDust, ("material.fabric", 5, 2), ("material.foam", 4, 2), ("material.plank", 3, 1)),
            new("prop.cardboard_box", "Картонная коробка", PropShape.CardboardBox, 4f, 30, 1f, PaperDust, ("material.cardboard", 2, 1)),
            new("prop.plastic_crate", "Пластиковый ящик", PropShape.PlasticCrate, 6f, 60, 1.5f, WhiteDust, ("material.plastic", 3, 1)),
            new("prop.trash_bin", "Урна", PropShape.TrashBin, 8f, 80, 2f, MetalDust, ("material.scrap_metal", 2, 1), ("material.plastic", 1, 0)),
            new("prop.switchboard", "Главный распределительный щит", PropShape.Switchboard, 320f, 1400, 14f, MetalDust, ("material.scrap_metal", 10, 5), ("material.wire", 8, 3), ("material.fuse", 4, 1)),
            new("prop.cable_reel", "Кабельный барабан", PropShape.CableReel, 70f, 300, 6f, WoodDust, ("material.wire", 10, 5), ("material.plank", 3, 1)),
            new("prop.vent_unit", "Вентустановка", PropShape.VentUnit, 380f, 1300, 14f, MetalDust, ("material.scrap_metal", 12, 6), ("material.wire", 3, 1), ("material.pipe", 2, 1)),
            new("prop.bed", "Кровать", PropShape.Bed, 40f, 250, 5f, WoodDust, ("material.plank", 4, 2), ("material.fabric", 3, 1), ("material.foam", 3, 1), ("material.screws", 4, 1)),
            new("prop.sleeping_bag", "Спальник", PropShape.SleepingBag, 2f, 40, 1f, PaperDust, ("item.sleeping_bag", 1, 0), ("material.fabric", 0, 2)),
            new("prop.workbench", "Верстак", PropShape.Workbench, 60f, 450, 8f, WoodDust, ("material.plank", 4, 2), ("material.nails", 6, 2), ("material.scrap_metal", 2, 1)),
            new("prop.sewing_table", "Швейный стол", PropShape.SewingTable, 35f, 300, 6f, MetalDust, ("material.plank", 3, 1), ("material.scrap_metal", 2, 1), ("material.screws", 6, 2)),
            new("prop.chem_table", "Химстол", PropShape.ChemTable, 45f, 350, 6f, WhiteDust, ("material.plank", 3, 1), ("material.glass_shard", 2, 1), ("material.pipe", 1, 0), ("material.screws", 4, 1)),
            new("prop.stove", "Кухонная плита", PropShape.Stove, 90f, 700, 10f, WhiteDust, ("material.scrap_metal", 6, 3), ("material.wire", 2, 1), ("material.pipe", 1, 0)),
            new("prop.burner", "Туристическая горелка", PropShape.Burner, 3f, 80, 2f, MetalDust, ("item.burner", 1, 0), ("material.scrap_metal", 0, 1)),
            new("prop.barricade", "Баррикада", PropShape.Barricade, 25f, 400, 5f, WoodDust, ("material.plank", 3, 2), ("material.nails", 3, 0)),
            new("prop.crusher", "Дробилка", PropShape.Crusher, 150f, 900, 10f, MetalDust, ("material.scrap_metal", 6, 3), ("material.pipe", 2, 1), ("material.wire", 2, 1)),
            // Этап 8Б: вход в мол, офисы, стенд с машиной, заброшенный вид, техэтаж.
            new("prop.turnstile", "Турникет", PropShape.Turnstile, 90f, 600, 8f, MetalDust, ("material.scrap_metal", 5, 2), ("material.pipe", 2, 1), ("material.wire", 1, 0)),
            new("prop.office_desk", "Офисный стол", PropShape.OfficeDesk, 30f, 250, 5f, WoodDust, ("material.plank", 3, 1), ("material.screws", 4, 1), ("material.wire", 1, 0), ("material.plastic", 2, 1)),
            new("prop.office_chair", "Офисное кресло", PropShape.OfficeChair, 9f, 120, 3f, PaperDust, ("material.fabric", 2, 1), ("material.foam", 1, 1), ("material.scrap_metal", 1, 0)),
            new("prop.filing_cabinet", "Шкаф для документов", PropShape.FilingCabinet, 45f, 400, 5f, MetalDust, ("material.scrap_metal", 4, 2), ("material.cardboard", 3, 2)),
            new("prop.water_cooler", "Кулер", PropShape.WaterCooler, 15f, 120, 3f, WhiteDust, ("material.plastic", 3, 1), ("material.wire", 1, 0)),
            new("prop.cubicle_wall", "Перегородка", PropShape.CubicleWall, 15f, 150, 3f, PaperDust, ("material.fabric", 2, 1), ("material.scrap_metal", 1, 0)),
            new("prop.stanchion", "Столбик ограждения", PropShape.Stanchion, 6f, 100, 2f, MetalDust, ("material.scrap_metal", 1, 0), ("material.fabric", 1, 0)),
            new("prop.trash_pile", "Куча мусора", PropShape.TrashPile, 10f, 40, 2f, PaperDust, ("material.cardboard", 2, 1), ("material.plastic", 2, 1)),
            new("prop.loot_crate", "Ящик с лутом", PropShape.LootCrate, 35f, 140, 4f, new Color(0.95f, 0.95f, 0.93f), ("material.plank", 2, 1), ("material.nails", 3, 1)),
            new("prop.pallet", "Поддон", PropShape.Pallet, 20f, 200, 3f, WoodDust, ("material.plank", 3, 1), ("material.nails", 4, 1)),
            new("prop.impulse_rack", "Стойка у кассы", PropShape.ImpulseRack, 12f, 120, 3f, MetalDust, ("material.scrap_metal", 2, 1), ("material.screws", 3, 1)),
            new("prop.work_light", "Строительная лампа", PropShape.WorkLight, 18f, 200, 4f, MetalDust, ("item.car_battery", 1, 0), ("material.scrap_metal", 2, 1), ("material.wire", 2, 1)),
            // Этап 8В: растения оазиса (ломаются, ничего не дают).
            new("prop.bush", "Куст", PropShape.Bush, 15f, 60, 2f, new Color(0.3f, 0.5f, 0.25f)),
            new("prop.fern", "Папоротник", PropShape.Fern, 3f, 25, 1f, new Color(0.3f, 0.55f, 0.25f)),
            // Этап 8Г: бочка-костёр — греет и светит, на ней можно готовить.
            new("prop.fire_barrel", "Бочка-костёр", PropShape.FireBarrel, 25f, 500, 4f, MetalDust, ("item.fire_barrel", 1, 0), ("material.scrap_metal", 0, 3)),
            // Этап 8Ж: хранилища игрока (верстак). Ящик лёгкий — уносится в руках, шкаф — только волоком.
            new("prop.wooden_crate", "Деревянный ящик", PropShape.LootCrate, 8f, 250, 3f, WoodDust, ("material.plank", 4, 2), ("material.nails", 4, 1)),
            new("prop.metal_locker", "Металлический шкаф", PropShape.FilingCabinet, 60f, 700, 5f, MetalDust, ("material.scrap_metal", 6, 3), ("material.screws", 4, 2)),
            // Этап 10: компьютер — стол с ЭЛТ-монитором и системником; экран-терминал вешает WorldTerminals.
            new("prop.pc_desk", "Компьютер", PropShape.PcDesk, 40f, 300, 5f, MetalDust, ("material.plastic", 3, 1), ("material.wire", 3, 1), ("material.scrap_metal", 2, 1), ("material.glass_shard", 1, 0)),
        };

        /// <summary>На чём можно спать (E): сон сохраняет точку возрождения. Диван — хуже кровати.</summary>
        private static readonly HashSet<string> Sleepable = new() { "prop.bed", "prop.sleeping_bag", "prop.sofa" };

        public static void CreatePropAssets()
        {
            ProjectSetup.EnsureFolder(PropsFolder);
            var set = AssetDatabase.LoadAssetAtPath<PropSet>(PropSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<PropSet>();
                AssetDatabase.CreateAsset(set, PropSetPath);
            }
            set.Props.Clear();

            foreach (var spec in Specs)
            {
                string path = $"{PropsFolder}/{spec.Id.Replace("prop.", "")}.asset";
                var def = AssetDatabase.LoadAssetAtPath<PropDef>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<PropDef>();
                    AssetDatabase.CreateAsset(def, path);
                }
                var so = new SerializedObject(def);
                so.FindProperty("_id").stringValue = spec.Id;
                so.ApplyModifiedPropertiesWithoutUndo();
                def.DisplayName = spec.Name;
                def.Shape = spec.Shape;
                // Размер, своя модель и материал — на усмотрение художника: не перезаписываем, если уже заданы.
                if (def.Size == Vector3.one || def.Size == Vector3.zero)
                    def.Size = PropMeshes.DefaultSize(spec.Shape);
                def.Mass = spec.Mass;
                def.Hardness = spec.Hardness;
                def.DisassembleTime = spec.DisassembleTime;
                def.DebrisColor = spec.Debris;
                def.Sleepable = Sleepable.Contains(spec.Id);
                def.PackUp = spec.Id is "prop.sleeping_bag" or "prop.burner";
                (def.Station, def.StationSpeed, def.StationAutonomous) = spec.Id switch
                {
                    "prop.workbench" => (CraftStation.Workbench, 1f, false),
                    "prop.sewing_table" => (CraftStation.Sewing, 1f, false),
                    "prop.chem_table" => (CraftStation.Chemistry, 1f, false),
                    "prop.stove" => (CraftStation.Cooking, 1f, true),
                    "prop.burner" => (CraftStation.Cooking, 0.7f, true),
                    "prop.fire_barrel" => (CraftStation.Cooking, 0.6f, true),
                    "prop.crusher" => (CraftStation.Crusher, 1f, false),
                    _ => (CraftStation.None, 1f, false),
                };
                def.SleepQuality = spec.Id == "prop.sofa" ? 0.7f : 1f;
                // Тепло: бочка-костёр горит, пока есть топливо; плита и горелка греют, пока готовят.
                (def.Heater, def.HeatOutput, def.HeatRadius) = spec.Id switch
                {
                    "prop.fire_barrel" => (true, 26f, 4f),
                    "prop.stove" => (false, 12f, 2.5f),
                    "prop.burner" => (false, 7f, 1.8f),
                    _ => (false, 0f, 2f),
                };
                // Хранилища (этап 8Ж): сетка, холод, лут при первом открытии.
                (def.ContainerGrid, def.ContainerCold, def.ContainerLoot) = spec.Id switch
                {
                    "prop.fridge" => (new Vector2Int(6, 8), true, "fridge"),
                    "prop.filing_cabinet" => (new Vector2Int(6, 6), false, "cabinet"),
                    "prop.office_desk" => (new Vector2Int(4, 4), false, "cabinet"),
                    "prop.wooden_crate" => (new Vector2Int(6, 4), false, ""),
                    "prop.metal_locker" => (new Vector2Int(8, 8), false, ""),
                    _ => (Vector2Int.zero, false, ""),
                };
                if (spec.Id == "prop.wooden_crate" && def.Size == PropMeshes.DefaultSize(spec.Shape))
                    def.Size = new Vector3(0.8f, 0.6f, 0.6f);
                if (spec.Id == "prop.metal_locker" && def.Size == PropMeshes.DefaultSize(spec.Shape))
                    def.Size = new Vector3(0.9f, 1.9f, 0.55f);
                def.Yields = new List<PropYield>();
                foreach (var (material, full, broken) in spec.Yields)
                    def.Yields.Add(new PropYield { Material = material, Disassembled = full, Broken = broken });
                EditorUtility.SetDirty(def);
                set.Props.Add(def);
            }
            EditorUtility.SetDirty(set);
        }
    }
}
