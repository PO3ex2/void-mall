using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Индексы пропов, которые ставит генератор (резолвятся по ID при старте).</summary>
    public struct PropIds
    {
        public ushort Shelf, Fridge, Checkout, DisplayTable, ClothesRack, Table, Chair, PalletRack, BoxStack, Car;
        public ushort Cabinet, Boiler, Bench, Planter, Sofa, CardboardBox, PlasticCrate, TrashBin;
        public ushort Switchboard, CableReel, VentUnit, Bed;
        public ushort Workbench, SewingTable, ChemTable, Stove, Crusher;
        public ushort PcDesk, Turnstile, OfficeDesk, OfficeChair, FilingCabinet, WaterCooler, CubicleWall, Stanchion, TrashPile, LootCrate, Pallet, ImpulseRack, WorkLight, Bush, Fern;

        public ushort StationProp(CraftStation station) => station switch
        {
            CraftStation.Workbench => Workbench,
            CraftStation.Sewing => SewingTable,
            CraftStation.Chemistry => ChemTable,
            CraftStation.Cooking => Stove,
            CraftStation.Crusher => Crusher,
            _ => ushort.MaxValue,
        };

        public static PropIds Resolve(PropRegistry props) => new()
        {
            Shelf = props.IndexOf("prop.shelf"),
            Fridge = props.IndexOf("prop.fridge"),
            Checkout = props.IndexOf("prop.checkout"),
            DisplayTable = props.IndexOf("prop.display_table"),
            ClothesRack = props.IndexOf("prop.clothes_rack"),
            Table = props.IndexOf("prop.table"),
            Chair = props.IndexOf("prop.chair"),
            PalletRack = props.IndexOf("prop.pallet_rack"),
            BoxStack = props.IndexOf("prop.box_stack"),
            Car = props.IndexOf("prop.car"),
            Cabinet = props.IndexOf("prop.cabinet"),
            Boiler = props.IndexOf("prop.boiler"),
            Bench = props.IndexOf("prop.bench"),
            Planter = props.IndexOf("prop.planter"),
            Sofa = props.IndexOf("prop.sofa"),
            CardboardBox = props.IndexOf("prop.cardboard_box"),
            PlasticCrate = props.IndexOf("prop.plastic_crate"),
            TrashBin = props.IndexOf("prop.trash_bin"),
            Switchboard = props.IndexOf("prop.switchboard"),
            CableReel = props.IndexOf("prop.cable_reel"),
            VentUnit = props.IndexOf("prop.vent_unit"),
            Bed = props.IndexOf("prop.bed"),
            Workbench = props.IndexOf("prop.workbench"),
            SewingTable = props.IndexOf("prop.sewing_table"),
            ChemTable = props.IndexOf("prop.chem_table"),
            Stove = props.IndexOf("prop.stove"),
            Crusher = props.IndexOf("prop.crusher"),
            Turnstile = props.IndexOf("prop.turnstile"),
            OfficeDesk = props.IndexOf("prop.office_desk"),
            OfficeChair = props.IndexOf("prop.office_chair"),
            FilingCabinet = props.IndexOf("prop.filing_cabinet"),
            WaterCooler = props.IndexOf("prop.water_cooler"),
            CubicleWall = props.IndexOf("prop.cubicle_wall"),
            Stanchion = props.IndexOf("prop.stanchion"),
            TrashPile = props.IndexOf("prop.trash_pile"),
            LootCrate = props.IndexOf("prop.loot_crate"),
            Pallet = props.IndexOf("prop.pallet"),
            ImpulseRack = props.IndexOf("prop.impulse_rack"),
            WorkLight = props.IndexOf("prop.work_light"),
            Bush = props.IndexOf("prop.bush"),
            Fern = props.IndexOf("prop.fern"),
            PcDesk = props.IndexOf("prop.pc_desk"),
        };
    }

    /// <summary>
    /// Расстановка пропов по плану уровня (проход 6 из ARCHITECTURE §4.6): шаблон раздела
    /// (ряды, острова, периметр, зал, склад...) + скамейки и урны в галереях, машины на парковке, мелочь для переноски.
    /// Детерминирована: зависит от сида, ячейки, уровня и комнаты. Каждый проп получает устойчивый ID.
    /// </summary>
    public static class PropPlacer
    {
        private const int Salt = 8000;

        /// <summary>
        /// Зазор между мебелью в рядах и вдоль стен. Вплотную нельзя: соседи спят кинематикой, и проснувшийся проп,
        /// зажатый ими с двух сторон, не сдвинуть (крайний в ряду тянулся, средний — нет).
        /// </summary>
        public const float Gap = 0.04f;

        private struct Context
        {
            public uint Seed;
            public MallCellInfo Info;
            public int Level;
            public float FloorY;
            public float2 Origin;
            public PropRegistry Props;
            public PropIds Ids;

            /// <summary>Занятые прямоугольники (min.xy, max.xy) — мебель не пересекается.</summary>
            public List<float4> Occupied;
        }

        /// <summary>
        /// Устойчивый ID пропа (этап 8Ж): ячейка и уровень, вид пропа (по строковому ID — не по индексу в реестре) и клетка,
        /// где он стоит. Новый проп в генераторе не сдвигает ID остальных; проп, который правка генератора передвинула на
        /// другую клетку, получает новый ID и появляется «как новый» — без состояния чужой мебели. Одинаковые пропы в одной
        /// клетке различает dup (номер среди таких же). Старший бит всегда 0: не пересекается с ID, созданными игроком.
        /// </summary>
        public static ulong MakeId(int2 cell, int level, string defId, int3 position, int dup)
        {
            uint kind = Hash.String(defId);
            uint high = Hash.Get(0x50524F50u, cell.x, cell.y, level, (int)kind);
            uint low = Hash.Get(kind ^ 0x1D5u, position.x, position.y, position.z, dup);
            return (((ulong)high << 32) | low) & 0x7FFFFFFFFFFFFFFFUL;
        }

        public static void Place(uint seed, in MallCellInfo info, in MallLevelPlan plan, NativeArray<SectionInfo> sections,
            PropRegistry props, in PropIds ids, List<PropSpawn> output)
        {
            var ctx = new Context
            {
                Seed = seed,
                Info = info,
                Level = plan.Level,
                FloorY = MallLayout.FloorTop(plan.Level),
                Origin = MallLayout.CellOrigin(info.Cell),
                Props = props,
                Ids = ids,
                Occupied = new List<float4>(),
            };
            int start = output.Count;
            LevelKind kind = MallLayout.Kind(plan.Level);

            if (kind == LevelKind.Parking)
            {
                PlaceCars(ctx, plan, output);
            }
            else if (kind == LevelKind.Tech)
            {
                PlaceWorkLights(ctx, plan, output);
            }
            else if (kind == LevelKind.Retail)
            {
                PlaceOasis(ctx, plan, output);
                // Особые места раньше обычной мебели галерей — им нужно своё место.
                PlaceEntrance(ctx, plan, output);
                PlacePlaza(ctx, plan, output);
                PlaceCarStand(ctx, plan, output);
                PlaceGalleryFurniture(ctx, plan, output);
                GalleryTrash(ctx, plan, output);
            }

            for (int i = 1; i < plan.Rooms.Length; i++)
            {
                var room = plan.Rooms[i];
                if (room.Has(RoomFlags.Stairwell))
                    continue;
                uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, Salt + plan.Level * 256 + i);
                if (room.Has(RoomFlags.Empty))
                {
                    if (h % 3 == 0)
                        TryAdd(ctx, plan, i, ctx.Ids.BoxStack, Center(room.Rect) + new float2(0.5f, 0.5f), 0f, output);
                    continue;
                }
                if (room.Has(RoomFlags.Corridor))
                {
                    Corridor(ctx, plan, i, h, output);
                    continue;
                }
                int roomStart = output.Count;
                SectionLayout layout = sections[room.Section].Layout;
                // Крафтовая станция у стены — раньше остальной мебели, чтобы ей хватило места.
                var section = sections[room.Section];
                if (room.Has(RoomFlags.Storefront))
                    Checkouts(ctx, plan, i, layout, output);
                if (section.Station != CraftStation.None && Hash.ToFloat01(Hash.Get(h, 0x57A7)) < section.StationChance)
                    AgainstWall(ctx, plan, i, ctx.Ids.StationProp(section.Station), output);
                switch (layout)
                {
                    case SectionLayout.Rows:
                        Rows(ctx, plan, i, ctx.Ids.Shelf, 3f, 2f, h, output);
                        Scatter(ctx, plan, i, ctx.Ids.PlasticCrate, 3, h ^ 0x1234u, output);
                        break;
                    case SectionLayout.Warehouse:
                        Rows(ctx, plan, i, ctx.Ids.PalletRack, 3.9f, 2.7f, h, output);
                        Scatter(ctx, plan, i, ctx.Ids.BoxStack, 3, h, output);
                        Scatter(ctx, plan, i, ctx.Ids.CardboardBox, 6, h ^ 0x5678u, output);
                        break;
                    case SectionLayout.Islands: Grid(ctx, plan, i, ctx.Ids.DisplayTable, 3.4f, 3f, 2.2f, h, output); break;
                    case SectionLayout.Perimeter:
                        Perimeter(ctx, plan, i, ctx.Ids.Shelf, output);
                        Grid(ctx, plan, i, ctx.Ids.DisplayTable, 4.5f, 4f, 3.4f, h, output);
                        break;
                    case SectionLayout.Clothing:
                        Perimeter(ctx, plan, i, ctx.Ids.Shelf, output);
                        Grid(ctx, plan, i, ctx.Ids.ClothesRack, 2.6f, 2.4f, 2.4f, h, output);
                        break;
                    case SectionLayout.Hall: Dining(ctx, plan, i, output); break;
                    case SectionLayout.Showroom:
                        // Мебельный: через раз — кровать изголовьем к стене (на ней можно спать).
                        if (h % 2 == 0)
                            AgainstWall(ctx, plan, i, ctx.Ids.Bed, output);
                        Grid(ctx, plan, i, ctx.Ids.Sofa, 4.6f, 4.2f, 2.5f, h, output);
                        break;
                    case SectionLayout.Tech:
                    {
                        // Вентиляционная: вентустановка посередине (в тесной — у стены), в больших — бойлер.
                        int4 r = room.Rect;
                        bool longX = r.z - r.x >= r.w - r.y;
                        if (math.min(r.z - r.x, r.w - r.y) >= 5)
                            TryAdd(ctx, plan, i, ctx.Ids.VentUnit, Center(r), longX ? 0f : 90f, output);
                        else
                            AgainstWall(ctx, plan, i, ctx.Ids.VentUnit, output);
                        if (math.min(r.z - r.x, r.w - r.y) >= 8)
                            Scatter(ctx, plan, i, ctx.Ids.Boiler, 1, h ^ 0x4321u, output);
                        if (h % 3 == 0)
                            Scatter(ctx, plan, i, ctx.Ids.CableReel, 1, h ^ 0x1357u, output);
                        Scatter(ctx, plan, i, ctx.Ids.CardboardBox, 2, h ^ 0x9ABCu, output);
                        break;
                    }
                    case SectionLayout.Office: Office(ctx, plan, i, h, output); break;
                    case SectionLayout.Construction: Construction(ctx, plan, i, h, output); break;
                    case SectionLayout.Electrical:
                    {
                        // Щитовая: главный щит у самой длинной стены (позже — кандидат в рабочий щиток), по остальным
                        // стенам — электрощиты, посередине — стеллаж с электрикой, кабельные барабаны.
                        int4 r = room.Rect;
                        AgainstWall(ctx, plan, i, ctx.Ids.Switchboard, output);
                        Perimeter(ctx, plan, i, ctx.Ids.Cabinet, output);
                        if (math.min(r.z - r.x, r.w - r.y) >= 5)
                            TryAdd(ctx, plan, i, ctx.Ids.Shelf, Center(r), r.z - r.x >= r.w - r.y ? 0f : 90f, output);
                        Scatter(ctx, plan, i, ctx.Ids.CableReel, 1 + (int)(h % 2), h ^ 0x2468u, output);
                        Scatter(ctx, plan, i, ctx.Ids.CardboardBox, 1, h ^ 0x9ABCu, output);
                        break;
                    }
                }
                // Техэтаж без ламп: в комнатах — строительные лампы на аккумуляторах (раньше мебели раздела не успели — ставим сейчас).
                if (plan.Level == MallLayout.TechLevel && h % 10 < 7)
                    Scatter(ctx, plan, i, ctx.Ids.WorkLight, 1, h ^ 0x11A7u, output);
                // Ящики с лутом — на техническом этаже (GDD §2.6), кроме склада стройматериалов (там свои).
                if (plan.Level == MallLayout.TechLevel && layout != SectionLayout.Construction && h % 5 < 3)
                    Scatter(ctx, plan, i, ctx.Ids.LootCrate, 1 + (int)((h >> 6) % 2), h ^ 0xC4A7u, output);
                if (info.Biome == MallBiome.Empty)
                    Thin(ctx, i, roomStart, output);
                Abandon(ctx, plan, i, layout, h, output);
            }

            PlaceTerminal(ctx, plan, sections, output);

            var seen = new Dictionary<ulong, int>();
            for (int i = start; i < output.Count; i++)
            {
                var spawn = output[i];
                var position = (int3)math.floor(new float3(spawn.Position.x, spawn.Position.y, spawn.Position.z));
                string defId = props.Get(spawn.Def).Id;
                ulong id = MakeId(info.Cell, plan.Level, defId, position, 0);
                seen.TryGetValue(id, out int dup);
                seen[id] = dup + 1;
                spawn.Id = dup == 0 ? id : MakeId(info.Cell, plan.Level, defId, position, dup);
                // Перевёрнутый чанк: мебель «стоит» на потолке вверх ногами (пол и потолок поменялись местами).
                if (MallPlanner.InInverted(info, new float2(spawn.Position.x, spawn.Position.z)))
                {
                    spawn.Position.y = MallPlanner.MirrorY(plan.Level, spawn.Position.y);
                    spawn.Inverted = true;
                }
                output[i] = spawn;
            }
        }

        /// <summary>Доля мебели, что остаётся в комнатах пустого биома (GDD §2.9).</summary>
        public const float EmptyBiomeKeep = 0.3f;

        /// <summary>Пустой биом: из мебели комнаты остаётся около трети, ряды с прорехами — как после выноса.</summary>
        private static void Thin(in Context ctx, int room, int from, List<PropSpawn> output)
        {
            int write = from;
            for (int read = from; read < output.Count; read++)
            {
                uint h = Hash.Get(ctx.Seed, ctx.Info.Cell.x, ctx.Info.Cell.y, Salt + 50000 + ctx.Level * 4096 + room * 64 + (read - from));
                if (Hash.ToFloat01(h) < EmptyBiomeKeep)
                    output[write++] = output[read];
            }
            output.RemoveRange(write, output.Count - write);
        }

        // ───────────────────────────── Особые места (этап 8Б) ─────────────────────────────

        /// <summary>Вход в мол: ряд турникетов поперёк западной галереи и две кассы по краям (фасад — в плане).</summary>
        /// <summary>Доля уровней (ячейка × этаж), где стоит ПК: на квартал из семи уровней — около одного на десять кварталов.</summary>
        public const float TerminalChance = 0.015f;

        /// <summary>Редкий ПК в мире (этап 10): в офисе или служебной комнате, последним в расстановке — остальной мебели не мешает.</summary>
        private static void PlaceTerminal(in Context ctx, in MallLevelPlan plan, NativeArray<SectionInfo> sections, List<PropSpawn> output)
        {
            if (ctx.Level < MallLayout.ParkingLevels)
                return;
            uint h = Hash.Get(ctx.Seed, ctx.Info.Cell.x, ctx.Info.Cell.y, Salt + 7100 + ctx.Level);
            if (Hash.ToFloat01(h) >= TerminalChance)
                return;
            var candidates = new List<int>();
            for (int i = 1; i < plan.Rooms.Length; i++)
            {
                var r = plan.Rooms[i];
                if (r.Has(RoomFlags.Stairwell) || r.Has(RoomFlags.Corridor) || r.Has(RoomFlags.Empty))
                    continue;
                var section = sections[r.Section];
                if (section.Layout == SectionLayout.Office || section.Service)
                    candidates.Add(i);
            }
            if (candidates.Count == 0)
                return;
            int room = candidates[(int)(Hash.Get(h, 1) % (uint)candidates.Count)];
            int before = output.Count;
            for (int attempt = 0; attempt < 4 && output.Count == before; attempt++)
                Scatter(ctx, plan, room, ctx.Ids.PcDesk, 1, Hash.Get(h, 2 + attempt), output);
        }

        private static void PlaceEntrance(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            if (!ctx.Info.HasEntrance || ctx.Level != MallLayout.FirstRetailLevel)
                return;
            float z = MallPlanner.EntranceTurnstileZ;
            for (int i = 0; i < 4; i++)
                TryAdd(ctx, plan, 0, ctx.Ids.Turnstile, new float2(4.2f + i * 1.6f, z), 0f, output);
            TryAdd(ctx, plan, 0, ctx.Ids.Checkout, new float2(1.9f, z + 1f), 90f, output);
            TryAdd(ctx, plan, 0, ctx.Ids.Checkout, new float2(12.1f, z + 1f), 270f, output);
            // ПК у входа (этап 10): всегда один — консоль, сейвы, настройки, «в лобби».
            TryAdd(ctx, plan, 0, ctx.Ids.PcDesk, new float2(12.2f, z + 5f), 270f, output);
        }

        /// <summary>Круглая площадь: место под фонтан занято, вокруг — кольцо скамеек и кадок.</summary>
        private static void PlacePlaza(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            // Над фонтаном — круглый проём, скамеек там нет.
            if (!MallPlanner.RoundPlaza(ctx.Seed, ctx.Info, ctx.Level, out bool fountain) || MallPlanner.PlazaWell(ctx.Seed, ctx.Info, ctx.Level))
                return;
            float2 c = MallPlanner.PlazaCenter;
            for (int i = 0; i < 8; i++)
            {
                // Скамейки — по диагоналям (там нет прохода вдоль галерей), кадки — между ними.
                float angle = (i * 45f + 45f) * Mathf.Deg2Rad;
                float2 dir = new(math.cos(angle), math.sin(angle));
                bool bench = i % 2 == 0;
                float radius = bench ? MallPlanner.PlazaBenchRadius : MallPlanner.PlazaBenchRadius + 0.3f;
                if (!bench && !fountain)
                    continue;
                ushort kind = bench ? ctx.Ids.Bench : ctx.Ids.Planter;
                float yaw = bench ? (i % 4 == 0 ? 45f : 135f) : 0f;
                TryAddLoose(ctx, plan, 0, kind, c + dir * radius, yaw, output);
            }
            // Чаша фонтана (структура) — после кольца: квадрат вокруг неё накрыл бы скамейки на диагоналях.
            if (fountain)
            {
                float r = MallPlanner.FountainRadius + 0.2f;
                ctx.Occupied.Add(new float4(c - r, c + r));
            }
        }

        /// <summary>Стенд с машиной: машина на подиуме (подиум — структура), столбики ограждения по углам.</summary>
        private static void PlaceCarStand(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            if (!MallPlanner.CarStand(ctx.Seed, ctx.Info, ctx.Level, out float2 center, out bool alongX))
                return;
            float2 half = (alongX ? MallPlanner.CarStandSize : MallPlanner.CarStandSize.yx) * 0.5f;
            ctx.Occupied.Add(new float4(center - half - 0.6f, center + half + 0.6f));
            output.Add(new PropSpawn { Def = ctx.Ids.Car, Position = new Vector3(ctx.Origin.x + center.x, ctx.FloorY + MallPlanner.CarStandHeight, ctx.Origin.y + center.y), Yaw = alongX ? 90f : 0f });
            for (int i = 0; i < 4; i++)
            {
                float2 corner = center + new float2((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f) * (half + 0.35f);
                output.Add(new PropSpawn { Def = ctx.Ids.Stanchion, Position = new Vector3(ctx.Origin.x + corner.x, ctx.FloorY, ctx.Origin.y + corner.y), Yaw = (i & 1) == 0 ? 0f : 180f });
            }
        }

        /// <summary>
        /// Можно ли ставить на плитку: открытый пол или пустота балкона на первом торговом этаже (там пол есть,
        /// открыт только потолок). Пустота атриума не годится — там низ эскалаторов.
        /// </summary>
        private static bool IsFloor(in Context ctx, in MallLevelPlan plan, int index)
        {
            byte tile = plan.Tiles[index];
            if (tile == (byte)Tile.Open)
                return true;
            if (tile != (byte)Tile.AtriumVoid || MallLayout.RetailFloor(ctx.Level) != 0)
                return false;
            int x = index % MallLevelPlan.Size, z = index / MallLevelPlan.Size;
            bool atriumX = ctx.Info.AtriumX && x >= MallPlanner.AtriumAlongMin - 1 && x <= MallPlanner.AtriumAlongMax && z < MallLayout.Band;
            bool atriumZ = ctx.Info.AtriumZ && z >= MallPlanner.AtriumAlongMin - 1 && z <= MallPlanner.AtriumAlongMax && x < MallLayout.Band;
            return !atriumX && !atriumZ;
        }

        /// <summary>Пропа с произвольным поворотом: занятость по описанной окружности (без проверки плиток пола под углом).</summary>
        private static bool TryAddLoose(in Context ctx, in MallLevelPlan plan, int room, ushort kind, float2 local, float yaw, List<PropSpawn> output)
        {
            Vector3 size = ctx.Props.Size(kind);
            float radius = math.length(new float2(size.x, size.z)) * 0.5f;
            int2 tile = (int2)math.floor(local);
            if (math.any(tile < 0) || math.any(tile >= MallLayout.CellSize))
                return false;
            int index = MallLevelPlan.Index(tile.x, tile.y);
            if (!IsFloor(ctx, plan, index) || plan.RoomIds[index] != room)
                return false;
            float4 box = new(local - radius * 0.7f, local + radius * 0.7f);
            foreach (var other in ctx.Occupied)
                if (box.x < other.z && other.x < box.z && box.y < other.w && other.y < box.w)
                    return false;
            ctx.Occupied.Add(box);
            float2 world = ctx.Origin + local;
            output.Add(new PropSpawn { Def = kind, Position = new Vector3(world.x, ctx.FloorY, world.y), Yaw = yaw });
            return true;
        }

        /// <summary>
        /// Кассы (GDD §2.9) у витрины со входом: в крупных магазинах — ряд стоек поперёк выхода с проходами между ними
        /// и стойками с мелочью за ними, в мелких — одна касса сбоку от двери. Ставятся раньше мебели раздела.
        /// </summary>
        private static void Checkouts(in Context ctx, in MallLevelPlan plan, int room, SectionLayout layout, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            // Сторона фасада с дверью: 0 — −Z, 1 — +Z, 2 — −X, 3 — +X (вовнутрь — противоположное направление).
            int side = -1;
            float doorSum = 0f;
            int doors = 0;
            for (int s = 0; s < 4 && side < 0; s++)
            {
                bool alongX = s < 2;
                int a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
                for (int a = a0; a < a1; a++)
                {
                    int2 t = s switch { 0 => new int2(a, r.y - 1), 1 => new int2(a, r.w), 2 => new int2(r.x - 1, a), _ => new int2(r.z, a) };
                    if (t.x < 0 || t.y < 0 || t.x >= MallLayout.CellSize || t.y >= MallLayout.CellSize)
                        continue;
                    if (plan.TileAt(t.x, t.y) == Tile.FacadeDoor)
                    {
                        doorSum += a + 0.5f;
                        doors++;
                    }
                }
                if (doors > 0)
                    side = s;
            }
            if (side < 0)
                return;
            float door = doorSum / doors;
            bool sideAlongX = side < 2;
            float wall = side switch { 0 => r.y, 1 => r.w, 2 => r.x, _ => r.z };
            float inward = side is 0 or 2 ? 1f : -1f;
            float sideMin = sideAlongX ? r.x : r.y, sideMax = sideAlongX ? r.z : r.w;
            int area = (r.z - r.x) * (r.w - r.y);
            bool big = area >= 260 || (layout is SectionLayout.Rows or SectionLayout.Warehouse && area >= 140);
            float2 At(float along, float depth) => sideAlongX ? new float2(along, wall + inward * depth) : new float2(wall + inward * depth, along);

            if (big)
            {
                // Стойки поперёк выхода (длинной стороной вглубь), шаг 2.4 — между ними проходы 1.5 м.
                int count = math.clamp((int)((sideMax - sideMin - 4f) / 2.4f), 2, 6);
                float start = math.clamp(door - (count - 1) * 1.2f, sideMin + 1.6f, sideMax - 1.6f - (count - 1) * 2.4f);
                float yaw = sideAlongX ? 90f : 0f;
                float rackYaw = side switch { 0 => 180f, 1 => 0f, 2 => 270f, _ => 90f };
                for (int k = 0; k < count; k++)
                {
                    float along = start + k * 2.4f;
                    if (TryAdd(ctx, plan, room, ctx.Ids.Checkout, At(along, 2.3f), yaw, output))
                        TryAdd(ctx, plan, room, ctx.Ids.ImpulseRack, At(along, 3.9f), rackYaw, output);
                }
                return;
            }
            // Мелкий магазин: одна касса вдоль фасада, сбоку от двери (с той стороны, где больше места).
            float offset = door - sideMin > sideMax - door ? -2.6f : 2.6f;
            TryAdd(ctx, plan, room, ctx.Ids.Checkout, At(door + offset, 1.6f), sideAlongX ? 0f : 90f, output);
        }

        /// <summary>Технический этаж без ламп: в его коридорах-галереях через каждые 16 м — строительная лампа у стены (через раз).</summary>
        /// <summary>Оазис: под дырой в потолке — кусты и папоротники по траве (в обход занятости зоны аномалии).</summary>
        private static void PlaceOasis(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            var info = ctx.Info;
            if (info.Anomaly != AnomalyKind.Oasis || ctx.Level != info.AnomalyLevel)
                return;
            uint h = Hash.Get(ctx.Seed, info.Cell.x, info.Cell.y, Salt + 9500);
            int wanted = 6 + (int)(h % 6), placed = 0;
            for (int k = 0; k < wanted * 3 && placed < wanted; k++)
            {
                uint hk = Hash.Get(h, k);
                float angle = Hash.ToFloat01(hk) * math.PI * 2f;
                float distance = math.sqrt(Hash.ToFloat01(Hash.Get(hk, 1))) * (info.AnomalyRadius - 0.6f);
                float2 local = info.AnomalyCenter + new float2(math.cos(angle), math.sin(angle)) * distance - ctx.Origin;
                int2 tile = (int2)math.floor(local);
                if (math.any(tile < 0) || math.any(tile >= MallLayout.CellSize))
                    continue;
                int room = plan.RoomIds[MallLevelPlan.Index(tile.x, tile.y)];
                ushort kind = (hk >> 8) % 3 == 0 ? ctx.Ids.Bush : ctx.Ids.Fern;
                if (TryAddLoose(ctx, plan, room, kind, local, Hash.ToFloat01(Hash.Get(hk, 2)) * 360f, output))
                    placed++;
            }
        }

        private static void PlaceWorkLights(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            for (int axis = 0; axis < 2; axis++)
            for (int along = 18; along < MallLayout.CellSize; along += 16)
            {
                uint h = Hash.Get(ctx.Seed, ctx.Info.Cell.x, ctx.Info.Cell.y, Salt + 9000 + axis * 64 + along);
                if (Hash.ToFloat01(h) >= 0.6f)
                    continue;
                float across = (h >> 8) % 2 == 0 ? 1.2f : MallLayout.Band - 1.2f;
                float yaw = axis == 0 ? (across < 7f ? 0f : 180f) : (across < 7f ? 90f : 270f);
                TryAdd(ctx, plan, 0, ctx.Ids.WorkLight, axis == 0 ? new float2(along, across) : new float2(across, along), yaw, output);
            }
        }

        /// <summary>Служебный коридор-тупик: коробки, урна, иногда куча мусора — и стена в конце.</summary>
        private static void Corridor(in Context ctx, in MallLevelPlan plan, int room, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            bool alongX = r.z - r.x > r.w - r.y;
            float2 far = alongX ? new float2(r.z - 0.6f, (r.y + r.w) * 0.5f) : new float2((r.x + r.z) * 0.5f, r.w - 0.6f);
            TryAdd(ctx, plan, room, ctx.Ids.CardboardBox, far, (h >> 4) % 4 * 90f, output);
            // Остальное — вдоль одной стены, чтобы по коридору можно было пройти до конца.
            float a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
            float side = (h & 1) == 0 ? (alongX ? r.y + 0.3f : r.x + 0.3f) : (alongX ? r.w - 0.3f : r.z - 0.3f);
            ushort[] kinds = { ctx.Ids.CardboardBox, ctx.Ids.TrashBin, ctx.Ids.CardboardBox };
            for (int k = 0; k < kinds.Length; k++)
            {
                float a = math.lerp(a0 + 2f, a1 - 2f, (k + 0.5f) / kinds.Length);
                TryAdd(ctx, plan, room, kinds[k], alongX ? new float2(a, side) : new float2(side, a), 0f, output);
            }
        }

        /// <summary>
        /// Офис: в вытянутом — коридор посередине и кабинки по бокам (перегородки, стол с креслом в каждой),
        /// в обычном — сетка столов с креслами; у стены шкаф для документов и кулер.
        /// </summary>
        private static void Office(in Context ctx, in MallLevelPlan plan, int room, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            bool alongX = r.z - r.x >= r.w - r.y;
            float length = alongX ? r.z - r.x : r.w - r.y, width = alongX ? r.w - r.y : r.z - r.x;
            AgainstWall(ctx, plan, room, ctx.Ids.FilingCabinet, output);
            AgainstWall(ctx, plan, room, ctx.Ids.WaterCooler, output);

            if (length >= width * 2f && width >= 6f)
            {
                // Кабинки: шаг 2.4 вдоль, по обе стороны от прохода шириной ~1.6.
                float a0 = alongX ? r.x : r.y, b0 = alongX ? r.y : r.x, b1 = alongX ? r.w : r.z;
                int count = (int)((length - 1f) / 2.4f);
                for (int side = 0; side < 2; side++)
                for (int k = 0; k < count; k++)
                {
                    float a = a0 + 0.8f + k * 2.4f;
                    float deskB = side == 0 ? b0 + 0.45f : b1 - 0.45f;
                    float chairB = side == 0 ? b0 + 1.25f : b1 - 1.25f;
                    float wallB = side == 0 ? b0 + (width - 1.6f) * 0.25f : b1 - (width - 1.6f) * 0.25f;
                    float deskYaw = alongX ? (side == 0 ? 180f : 0f) : (side == 0 ? 270f : 90f);
                    float2 wall = alongX ? new float2(a, wallB) : new float2(wallB, a);
                    float2 desk = alongX ? new float2(a + 1.2f, deskB) : new float2(deskB, a + 1.2f);
                    float2 chair = alongX ? new float2(a + 1.2f, chairB) : new float2(chairB, a + 1.2f);
                    TryAdd(ctx, plan, room, ctx.Ids.CubicleWall, wall, alongX ? 90f : 0f, output);
                    if (TryAdd(ctx, plan, room, ctx.Ids.OfficeDesk, desk, deskYaw, output))
                        TryAdd(ctx, plan, room, ctx.Ids.OfficeChair, chair, deskYaw + 180f, output);
                }
                return;
            }

            // Открытый офис: столы смотрят мониторами в одну сторону, кресла за ними.
            const float pitchX = 2.4f, pitchZ = 2.6f, margin = 1.5f;
            if (r.z - r.x < margin * 2f + 1.4f || r.w - r.y < margin * 2f + 1.4f)
            {
                AgainstWall(ctx, plan, room, ctx.Ids.OfficeDesk, output);
                return;
            }
            int nx = (int)((r.z - r.x - margin * 2f) / pitchX) + 1;
            int nz = (int)((r.w - r.y - margin * 2f) / pitchZ) + 1;
            float sx = (r.x + r.z) * 0.5f - (nx - 1) * pitchX * 0.5f;
            float sz = (r.y + r.w) * 0.5f - (nz - 1) * pitchZ * 0.5f;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float2 desk = new(sx + ix * pitchX, sz + iz * pitchZ);
                if (TryAdd(ctx, plan, room, ctx.Ids.OfficeDesk, desk, 0f, output))
                    TryAdd(ctx, plan, room, ctx.Ids.OfficeChair, desk + new float2(0f, -0.75f), 180f, output);
            }
        }

        /// <summary>Склад стройматериалов: поддоны рядами (товары на них — лут раздела), ящики с лутом, коробки.</summary>
        private static void Construction(in Context ctx, in MallLevelPlan plan, int room, uint h, List<PropSpawn> output)
        {
            Grid(ctx, plan, room, ctx.Ids.Pallet, 2.2f, 2f, 1.4f, h, output);
            Scatter(ctx, plan, room, ctx.Ids.LootCrate, 2, h ^ 0x71u, output);
            Scatter(ctx, plan, room, ctx.Ids.BoxStack, 1, h ^ 0x72u, output);
        }

        /// <summary>Доли комнат с поваленным стеллажом и с кучей мусора: в обычном моле и в пустом биоме.</summary>
        public const float FallenShelfChance = 0.2f, FallenShelfChanceEmpty = 0.6f;
        public const float TrashChance = 0.15f, TrashChanceEmpty = 0.5f;

        /// <summary>Заброшенный вид (GDD §2.6): поваленный стеллаж (товар рассыпан рядом — ShelfLoot) и кучи мусора.</summary>
        private static void Abandon(in Context ctx, in MallLevelPlan plan, int room, SectionLayout layout, uint h, List<PropSpawn> output)
        {
            bool empty = ctx.Info.Biome == MallBiome.Empty;
            bool shelves = layout is SectionLayout.Rows or SectionLayout.Perimeter or SectionLayout.Clothing or SectionLayout.Warehouse;
            if (shelves && Hash.ToFloat01(Hash.Get(h, 0xFA11)) < (empty ? FallenShelfChanceEmpty : FallenShelfChance))
            {
                int4 r = plan.Rooms[room].Rect;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    uint ha = Hash.Get(h, 0xFA12 + attempt);
                    float2 p = new(math.lerp(r.x + 1.5f, r.z - 1.5f, Hash.ToFloat01(ha)), math.lerp(r.y + 1.5f, r.w - 1.5f, Hash.ToFloat01(Hash.Get(ha, 1))));
                    if (TryAddLying(ctx, plan, room, ctx.Ids.Shelf, p, (ha >> 4) % 4 * 90f, output))
                        break;
                }
            }
            if (Hash.ToFloat01(Hash.Get(h, 0x7A54)) < (empty ? TrashChanceEmpty : TrashChance))
                Scatter(ctx, plan, room, ctx.Ids.TrashPile, empty ? 2 : 1, h ^ 0x7A55u, output);
        }

        /// <summary>
        /// Стеллаж лицом вниз (Pitch −90): на полу занимает ширину × высоту, опора поднята на полглубины,
        /// верх лежит в сторону −Z (в кадре поворота). local — центр следа на полу.
        /// </summary>
        private static bool TryAddLying(in Context ctx, in MallLevelPlan plan, int room, ushort kind, float2 local, float yaw, List<PropSpawn> output)
        {
            Vector3 size = ctx.Props.Size(kind);
            bool swap = Mathf.Abs(Mathf.Repeat(yaw, 180f) - 90f) < 1f;
            float2 half = (swap ? new float2(size.y, size.x) : new float2(size.x, size.y)) * 0.5f;
            int2 min = (int2)math.floor(local - half + 0.02f);
            int2 max = (int2)math.floor(local + half - 0.02f);
            if (min.x < 0 || min.y < 0 || max.x >= MallLayout.CellSize || max.y >= MallLayout.CellSize)
                return false;
            for (int z = min.y; z <= max.y; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                int index = MallLevelPlan.Index(x, z);
                if (!IsFloor(ctx, plan, index) || plan.RoomIds[index] != room)
                    return false;
            }
            if (InAnomaly(ctx, ctx.Origin + local, math.length(half)))
                return false;
            float4 box = new(local - half - Gap, local + half + Gap);
            foreach (var other in ctx.Occupied)
                if (box.x < other.z && other.x < box.z && box.y < other.w && other.y < box.w)
                    return false;
            ctx.Occupied.Add(box);
            Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
            Vector3 pivot = new Vector3(ctx.Origin.x + local.x, ctx.FloorY + size.z * 0.5f, ctx.Origin.y + local.y) - back * (size.y * 0.5f);
            output.Add(new PropSpawn { Def = kind, Position = pivot, Yaw = yaw, Pitch = -90f });
            return true;
        }

        /// <summary>Пустой биом: в галереях больше мусора у скамеек.</summary>
        private static void GalleryTrash(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            if (ctx.Info.Biome != MallBiome.Empty)
                return;
            for (int axis = 0; axis < 2; axis++)
            for (int along = 24; along < MallLayout.CellSize; along += 12)
            {
                uint h = Hash.Get(ctx.Seed, ctx.Info.Cell.x, ctx.Info.Cell.y, Salt + 7000 + ctx.Level * 64 + axis * 32 + along);
                if (Hash.ToFloat01(h) >= 0.35f)
                    continue;
                float across = 2.5f + Hash.ToFloat01(Hash.Get(h, 1)) * 9f;
                TryAdd(ctx, plan, 0, ctx.Ids.TrashPile, axis == 0 ? new float2(along, across) : new float2(across, along), (h >> 8) % 4 * 90f, output);
            }
        }

        // ───────────────────────────── Шаблоны ─────────────────────────────

        /// <summary>Двойные ряды (спина к спине) вдоль длинной стороны комнаты, поперечный проход каждые 5 секций.</summary>
        private static void Rows(in Context ctx, in MallLevelPlan plan, int room, ushort kind, float pitch, float unit, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            bool alongX = r.z - r.x >= r.w - r.y;
            float depth = ctx.Props.Size(kind).z;
            unit = math.max(unit, ctx.Props.Size(kind).x + Gap);
            float a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
            float b0 = alongX ? r.y : r.x, b1 = alongX ? r.w : r.z;
            float margin = 1.8f;
            int units = (int)((a1 - a0 - margin * 2f) / unit);
            if (units <= 0)
                return;
            float start = (a0 + a1) * 0.5f - units * unit * 0.5f;
            for (float b = b0 + margin + depth; b + depth <= b1 - margin + 0.01f; b += pitch)
            {
                for (int u = 0; u < units; u++)
                {
                    if (units > 6 && u % 5 == 4)
                        continue; // поперечный проход
                    float a = start + (u + 0.5f) * unit;
                    float2 front = alongX ? new float2(a, b - (depth + Gap) * 0.5f) : new float2(b - (depth + Gap) * 0.5f, a);
                    float2 back = alongX ? new float2(a, b + (depth + Gap) * 0.5f) : new float2(b + (depth + Gap) * 0.5f, a);
                    float yaw = alongX ? 0f : 90f;
                    TryAdd(ctx, plan, room, kind, front, yaw, output);
                    TryAdd(ctx, plan, room, kind, back, yaw + 180f, output);
                }
            }
        }

        private static void Grid(in Context ctx, in MallLevelPlan plan, int room, ushort kind, float pitchX, float pitchZ, float margin, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            int nx = (int)((r.z - r.x - margin * 2f) / pitchX) + 1;
            int nz = (int)((r.w - r.y - margin * 2f) / pitchZ) + 1;
            if (r.z - r.x < margin * 2f || r.w - r.y < margin * 2f)
                return;
            float sx = (r.x + r.z) * 0.5f - (nx - 1) * pitchX * 0.5f;
            float sz = (r.y + r.w) * 0.5f - (nz - 1) * pitchZ * 0.5f;
            float yaw = (h & 1) == 0 ? 0f : 90f;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
                TryAdd(ctx, plan, room, kind, new float2(sx + ix * pitchX, sz + iz * pitchZ), yaw, output);
        }

        /// <summary>Мебель вдоль стен: секции с отступом от стены, не ближе 1.5 м к дверям.</summary>
        /// <summary>Один проп посередине самой длинной стены, где нет двери (иначе — следующей по длине).</summary>
        private static bool AgainstWall(in Context ctx, in MallLevelPlan plan, int room, ushort kind, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            Vector3 size = ctx.Props.Size(kind);
            float half = size.z * 0.5f + 0.05f;
            bool xLonger = r.z - r.x >= r.w - r.y;
            for (int n = 0; n < 4; n++)
            {
                // Сначала две длинные стены, потом две короткие.
                int side = xLonger ? n : (n + 2) % 4;
                bool alongX = side < 2;
                float a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
                if (a1 - a0 < size.x + 1f)
                    continue;
                float a = (a0 + a1) * 0.5f;
                float2 position;
                float yaw;
                int2 wall;
                switch (side)
                {
                    case 0: position = new float2(a, r.y + half); yaw = 180f; wall = new int2((int)a, r.y - 1); break;
                    case 1: position = new float2(a, r.w - half); yaw = 0f; wall = new int2((int)a, r.w); break;
                    case 2: position = new float2(r.x + half, a); yaw = 270f; wall = new int2(r.x - 1, (int)a); break;
                    default: position = new float2(r.z - half, a); yaw = 90f; wall = new int2(r.z, (int)a); break;
                }
                if (NearDoor(plan, wall, alongX, size.x * 0.5f + 1.5f))
                    continue;
                if (TryAdd(ctx, plan, room, kind, position, yaw, output))
                    return true;
            }
            return false;
        }

        private static void Perimeter(in Context ctx, in MallLevelPlan plan, int room, ushort kind, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            Vector3 size = ctx.Props.Size(kind);
            float half = size.z * 0.5f + 0.05f;
            // Стены: −Z (y), +Z (w), −X (x), +X (z). Мебель смотрит внутрь комнаты.
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
                float step = size.x + Gap;
                int units = (int)((a1 - a0 - 1f) / step);
                float start = (a0 + a1) * 0.5f - units * step * 0.5f;
                for (int u = 0; u < units; u++)
                {
                    float a = start + (u + 0.5f) * step;
                    float2 position;
                    float yaw;
                    int2 wall;
                    switch (side)
                    {
                        case 0: position = new float2(a, r.y + half); yaw = 180f; wall = new int2((int)a, r.y - 1); break;
                        case 1: position = new float2(a, r.w - half); yaw = 0f; wall = new int2((int)a, r.w); break;
                        case 2: position = new float2(r.x + half, a); yaw = 270f; wall = new int2(r.x - 1, (int)a); break;
                        default: position = new float2(r.z - half, a); yaw = 90f; wall = new int2(r.z, (int)a); break;
                    }
                    if (NearDoor(plan, wall, alongX, size.x * 0.5f + 1.5f))
                        continue;
                    TryAdd(ctx, plan, room, kind, position, yaw, output);
                }
            }
        }

        private static void Dining(in Context ctx, in MallLevelPlan plan, int room, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            const float pitch = 3.4f, margin = 2f;
            int nx = (int)((r.z - r.x - margin * 2f) / pitch) + 1;
            int nz = (int)((r.w - r.y - margin * 2f) / pitch) + 1;
            if (r.z - r.x < margin * 2f || r.w - r.y < margin * 2f)
                return;
            float sx = (r.x + r.z) * 0.5f - (nx - 1) * pitch * 0.5f;
            float sz = (r.y + r.w) * 0.5f - (nz - 1) * pitch * 0.5f;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float2 c = new(sx + ix * pitch, sz + iz * pitch);
                if (!TryAdd(ctx, plan, room, ctx.Ids.Table, c, 0f, output))
                    continue;
                TryAdd(ctx, plan, room, ctx.Ids.Chair, c + new float2(0f, -0.95f), 180f, output);
                TryAdd(ctx, plan, room, ctx.Ids.Chair, c + new float2(0f, 0.95f), 0f, output);
                TryAdd(ctx, plan, room, ctx.Ids.Chair, c + new float2(-0.95f, 0f), 270f, output);
                TryAdd(ctx, plan, room, ctx.Ids.Chair, c + new float2(0.95f, 0f), 90f, output);
            }
        }

        private static void Scatter(in Context ctx, in MallLevelPlan plan, int room, ushort kind, int count, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            for (int i = 0; i < count; i++)
            {
                uint hi = Hash.Get(h, i);
                float2 p = new(math.lerp(r.x + 1f, r.z - 1f, Hash.ToFloat01(hi)), math.lerp(r.y + 1f, r.w - 1f, Hash.ToFloat01(hi * 2654435761u)));
                TryAdd(ctx, plan, room, kind, p, (hi >> 3) % 4 * 90f, output);
            }
        }

        private static void PlaceGalleryFurniture(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                bool atrium = axis == 0 ? ctx.Info.AtriumX : ctx.Info.AtriumZ;
                if (atrium)
                    continue;
                for (int along = 20; along < MallLayout.CellSize; along += 12)
                {
                    float2 bench = axis == 0 ? new float2(along, 7f) : new float2(7f, along);
                    float2 planter = axis == 0 ? new float2(along + 4.5f, 7f) : new float2(7f, along + 4.5f);
                    TryAdd(ctx, plan, 0, ctx.Ids.Bench, bench, axis == 0 ? 0f : 90f, output);
                    TryAdd(ctx, plan, 0, ctx.Ids.Planter, planter, 0f, output);
                    float2 bin = axis == 0 ? new float2(along + 2.2f, 7f) : new float2(7f, along + 2.2f);
                    TryAdd(ctx, plan, 0, ctx.Ids.TrashBin, bin, 0f, output);
                }
            }
        }

        private static void PlaceCars(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            for (int row = 0; row < MallLayout.CellSize / 16; row++)
            for (int side = 0; side < 2; side++)
            for (int bay = 0; bay < 21; bay++)
            {
                uint h = Hash.Get(ctx.Seed, ctx.Info.Cell.x, ctx.Info.Cell.y, Salt + 5000 + ctx.Level * 1000 + row * 64 + side * 32 + bay);
                if (h % 100 >= 30)
                    continue;
                float2 p = new(bay * 3 + 1.5f, row * 16 + (side == 0 ? 4f : 10f));
                TryAdd(ctx, plan, 0, ctx.Ids.Car, p, (h >> 8) % 2 == 0 ? 0f : 180f, output);
            }
        }

        // ───────────────────────────── Проверки ─────────────────────────────

        /// <summary>Добавить, если весь габарит лежит на свободном полу этой комнаты и вне аномалий.</summary>
        private static bool TryAdd(in Context ctx, in MallLevelPlan plan, int room, ushort kind, float2 local, float yaw, List<PropSpawn> output)
        {
            Vector3 size = ctx.Props.Size(kind);
            bool swap = Mathf.Abs(Mathf.Repeat(yaw, 180f) - 90f) < 1f;
            float2 half = (swap ? new float2(size.z, size.x) : new float2(size.x, size.z)) * 0.5f;
            int2 min = (int2)math.floor(local - half + 0.02f);
            int2 max = (int2)math.floor(local + half - 0.02f);
            if (min.x < 0 || min.y < 0 || max.x >= MallLayout.CellSize || max.y >= MallLayout.CellSize)
                return false;
            for (int z = min.y; z <= max.y; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                int index = MallLevelPlan.Index(x, z);
                if (!IsFloor(ctx, plan, index) || plan.RoomIds[index] != room)
                    return false;
            }
            float2 world = ctx.Origin + local;
            if (InAnomaly(ctx, world, math.length(half)))
                return false;
            float4 box = new(local - half, local + half);
            foreach (var other in ctx.Occupied)
                if (box.x < other.z && other.x < box.z && box.y < other.w && other.y < box.w)
                    return false;
            ctx.Occupied.Add(box);
            output.Add(new PropSpawn { Def = kind, Position = new Vector3(world.x, ctx.FloorY, world.y), Yaw = yaw });
            return true;
        }

        /// <summary>Точка в зоне своей аномалии ячейки или большой бездны соседа.</summary>
        public static bool InAnomaly(in MallCellInfo info, int level, float2 world, float radius) =>
            InOwnAnomaly(info, level, world, radius) || (info.HasBigAbyss && InOwnAnomaly(info.WithBigAbyss(), level, world, radius));

        private static bool InOwnAnomaly(in MallCellInfo info, int level, float2 world, float radius)
        {
            float distance = math.distance(world, info.AnomalyCenter);
            switch (info.Anomaly)
            {
                case AnomalyKind.Abyss:
                    if (level == info.AnomalyLevel)
                        return distance < MallAnomalies.AbyssZoneRadius(info.AnomalyRadius) + radius;
                    return level < info.AnomalyLevel && distance < info.AnomalyRadius + MallAnomalies.ShellThickness + radius + 0.5f;
                case AnomalyKind.Pool:
                    if (level == info.AnomalyLevel)
                        return distance < MallAnomalies.PoolZoneRadius(info.AnomalyRadius) + radius;
                    return level == info.AnomalyLevel - 1 && distance < info.AnomalyRadius + radius;
                case AnomalyKind.Cutout:
                    float2 c = info.AnomalyCenter;
                    return level <= info.AnomalyLevel
                        && world.x + radius > c.x && world.x - radius < c.x + WorldConstants.ChunkSize
                        && world.y + radius > c.y && world.y - radius < c.y + WorldConstants.ChunkSize;
                case AnomalyKind.Oasis:
                    // Под дырой — свои растения (PlaceOasis), этажом выше — шахта со стенкой.
                    if (level == info.AnomalyLevel)
                        return distance < MallPlanner.OasisZoneRadius(info.AnomalyRadius) + radius;
                    return level == info.AnomalyLevel + 1 && distance < MallPlanner.OasisZoneRadius(info.AnomalyRadius) + radius;
                default:
                    return false;
            }
        }

        private static bool InAnomaly(in Context ctx, float2 world, float radius) => InAnomaly(ctx.Info, ctx.Level, world, radius);

        private static bool NearDoor(in MallLevelPlan plan, int2 wall, bool alongX, float reach)
        {
            int span = (int)math.ceil(reach);
            for (int d = -span; d <= span; d++)
            {
                int2 p = wall + (alongX ? new int2(d, 0) : new int2(0, d));
                if (p.x < 0 || p.y < 0 || p.x >= MallLayout.CellSize || p.y >= MallLayout.CellSize)
                    continue;
                Tile t = plan.TileAt(p.x, p.y);
                if (t == Tile.Door || t == Tile.FacadeDoor || t == Tile.CoreDoor)
                    return true;
            }
            return false;
        }

        private static float2 Center(int4 rect) => new((rect.x + rect.z) * 0.5f, (rect.y + rect.w) * 0.5f);
    }
}
