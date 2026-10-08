using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Crafting;
using VoidMall.Items;
using VoidMall.Player;
using VoidMall.Skills;
using VoidMall.Survival;

namespace VoidMall.Game
{
    /// <summary>
    /// Всё состояние игрока одним объектом с версией (этап 8Ж): позиция и взгляд, показатели и статусы, инвентарь с
    /// экипировкой и хотбаром, навыки, изученные чертежи, точка возрождения. JSON с полями по именам: новое поле в старом
    /// сейве получает значение по умолчанию, убранное — игнорируется. Предметы, статусы и навыки — строковыми ID.
    /// Запись на диск — этап 9.
    /// </summary>
    [Serializable]
    public sealed class PlayerSnapshot
    {
        /// <summary>Версия формата. Поднимать, когда старые поля меняют смысл (тогда нужна миграция в Migrate).</summary>
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public Vector3 Position;
        public float Yaw, Pitch;
        public VitalsData Vitals = new();
        public InventoryData Inventory = new();
        public SkillsData Skills = new();
        public List<string> Blueprints = new();

        /// <summary>Очередь крафта в руках (ингредиенты уже списаны — без неё потерялись бы).</summary>
        public QueueData HandQueue = new();

        /// <summary>Кровать (ID пропа строкой: JsonUtility не пишет ulong) и место у неё. Пусто — нет.</summary>
        public string SpawnProp;
        public Vector3 SpawnPosition;

        public string ToJson(bool pretty = false) => JsonUtility.ToJson(this, pretty);

        public static PlayerSnapshot FromJson(string json)
        {
            var snapshot = JsonUtility.FromJson<PlayerSnapshot>(json) ?? new PlayerSnapshot();
            Migrate(snapshot);
            return snapshot;
        }

        /// <summary>Поднять старый формат до текущего (пока версия одна).</summary>
        private static void Migrate(PlayerSnapshot snapshot)
        {
            snapshot.Vitals ??= new VitalsData();
            snapshot.Inventory ??= new InventoryData();
            snapshot.Skills ??= new SkillsData();
            snapshot.Blueprints ??= new List<string>();
            snapshot.HandQueue ??= new QueueData();
            snapshot.Version = CurrentVersion;
        }

        /// <summary>Снять состояние с объекта игрока.</summary>
        public static PlayerSnapshot Capture(GameObject player)
        {
            var snapshot = new PlayerSnapshot();
            if (player.TryGetComponent(out PlayerMotor motor))
            {
                snapshot.Position = motor.State.Position;
                snapshot.Yaw = motor.Look.Yaw;
                snapshot.Pitch = motor.Look.Pitch;
            }
            if (player.TryGetComponent(out PlayerVitals vitals))
            {
                snapshot.Vitals = vitals.Model.Capture();
                if (vitals.SpawnPropId != 0)
                {
                    snapshot.SpawnProp = vitals.SpawnPropId.ToString();
                    snapshot.SpawnPosition = vitals.SpawnPosition;
                }
            }
            if (player.TryGetComponent(out PlayerInventory inventory) && inventory.Items != null)
                snapshot.Inventory = inventory.Capture();
            if (player.TryGetComponent(out PlayerSkills skills))
                snapshot.Skills = skills.Tree.Capture();
            if (player.TryGetComponent(out PlayerCrafting crafting))
            {
                snapshot.Blueprints = new List<string>(crafting.Learned);
                if (inventory != null && inventory.Items != null)
                    snapshot.HandQueue = CraftSave.Write(crafting.Queue, inventory.Items);
            }
            return snapshot;
        }

        /// <summary>Вернуть состояние на объект игрока.</summary>
        public void Restore(GameObject player)
        {
            if (player.TryGetComponent(out PlayerVitals vitals))
            {
                vitals.Model.Restore(Vitals);
                vitals.RestoreSpawnPoint(ulong.TryParse(SpawnProp, out ulong id) ? id : 0, SpawnPosition);
            }
            if (player.TryGetComponent(out PlayerInventory inventory) && inventory.Items != null)
                inventory.Restore(Inventory);
            if (player.TryGetComponent(out PlayerSkills skills))
                skills.Tree.Restore(Skills);
            if (player.TryGetComponent(out PlayerCrafting crafting))
            {
                crafting.RestoreLearned(Blueprints);
                if (inventory != null && inventory.Items != null && crafting.Queue.IsEmpty && CraftingSystem.Instance != null)
                    CraftSave.Read(HandQueue, crafting.Queue, inventory.Items, id => CraftingSystem.Instance.TryGetRecipe(id, out var recipe) ? recipe : null);
            }
            if (player.TryGetComponent(out PlayerMotor motor))
            {
                motor.Teleport(Position);
                motor.Look.SetAngles(Yaw, Pitch);
            }
        }

        // ───────────────────────────── Консоль (проверка до этапа 9) ─────────────────────────────

        private static string _stored;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _stored = null;

        [ConsoleCommand("player_snapshot", "Снять состояние игрока в память (и в Temp/player_snapshot.json): проверка формата сейва.", Flags = ConsoleFlags.Cheat)]
        private static string SnapshotCommand()
        {
            var player = PlayerInventory.Local != null ? PlayerInventory.Local.gameObject : null;
            if (player == null)
                return "Нет игрока.";
            var snapshot = Capture(player);
            _stored = snapshot.ToJson();
            System.IO.File.WriteAllText("Temp/player_snapshot.json", snapshot.ToJson(true));
            return $"Снимок v{snapshot.Version}: {snapshot.Inventory.Stacks.Count} стаков, {snapshot.Vitals.Statuses.Count} статусов, {_stored.Length} байт";
        }

        [ConsoleCommand("player_restore", "Вернуть состояние игрока из снимка player_snapshot.", Flags = ConsoleFlags.Cheat)]
        private static string RestoreCommand()
        {
            var player = PlayerInventory.Local != null ? PlayerInventory.Local.gameObject : null;
            if (player == null || _stored == null)
                return "Нет игрока или снимка (сначала player_snapshot).";
            FromJson(_stored).Restore(player);
            return "Состояние восстановлено.";
        }
    }
}
