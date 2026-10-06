using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Items;

namespace VoidMall.Survival
{
    /// <summary>Хранимое состояние: травма, болезнь или действие лекарства.</summary>
    public sealed class Status
    {
        public StatusKind Kind;

        /// <summary>Осталось игровых часов; бесконечность — пока не вылечат.</summary>
        public float Remaining = float.PositiveInfinity;

        /// <summary>Сила (боль 0…1, прогресс инфекции 0…1).</summary>
        public float Intensity = 1f;

        /// <summary>Сколько игровых часов уже длится.</summary>
        public float Age;

        public int Stacks = 1;

        /// <summary>Перелом под шиной: мешает меньше и срастается.</summary>
        public bool Splinted;
    }

    /// <summary>Что вокруг игрока — собирает PlayerVitals каждый кадр.</summary>
    public struct VitalsEnvironment
    {
        /// <summary>Температура окружения, °C.</summary>
        public float AirTemperature;

        /// <summary>Утепление одежды, °C.</summary>
        public float Warmth;

        /// <summary>Освещённость в точке игрока 0…1 (своё освещение — фонарик — учтено отдельно).</summary>
        public float Light;

        public bool HasOwnLight;
        public bool InAnomaly;

        /// <summary>Взгляд на «Смотри на меня»: рассудок тает, долгое воздействие оставляет «Отпечаток».</summary>
        public bool LookingAtAnomaly;

        /// <summary>В оазисе: свет и уют, рассудок восстанавливается быстрее.</summary>
        public bool InOasis;

        /// <summary>Лечение светящейся лужей, здоровья в секунду (уже списано с запаса лужи).</summary>
        public float PuddleHeal;
        public bool IsNight;
        public bool Sleeping;

        /// <summary>Качество сна 0…1 (кровать 1, диван 0.7, пол 0.5); 0 — как кровать.</summary>
        public float SleepQuality;

        /// <summary>До скольки процентов можно выспаться (пол — 60); 0 — до 100.</summary>
        public float SleepRestCap;
    }

    /// <summary>Бонусы навыков к выживанию (выставляет PlayerVitals по уровням навыков).</summary>
    public struct VitalsPerks
    {
        public float StaminaBonus;     // +доля максимума стамины («Выносливость»)
        public float StaminaCostMul;   // множитель расхода стамины («Атлет»)
        public float FallDamageMul;    // множитель урона от падений («Крепкие кости»)
        public float HealBonus;        // +здоровья от лекарств («Полевая хирургия»)
        public float SplintHours;      // за сколько срастается перелом под шиной
        public float CookMul;          // множитель калорий готовых блюд («Повар»)
        public float ComfortWiden;     // на сколько °C шире комфорт («Закалка»)
        public float SanityDrainMul;   // множитель потери рассудка («Крепкие нервы»)

        public static VitalsPerks None => new()
        {
            StaminaCostMul = 1f, FallDamageMul = 1f, SplintHours = 48f, CookMul = 1f, SanityDrainMul = 1f,
        };
    }

    /// <summary>
    /// Показатели игрока (GDD §4.1, решения этапа 7): одно здоровье + травмы-состояния, сытость (ккал) и вода с балансом
    /// жиров/белков/углеводов за сутки, температура тела, бодрость, рассудок, стамина. Чистая логика без сцены —
    /// покрыта EditMode-тестами. Потребности и заживление — в игровых часах (сон ускоряет время), кровотечения,
    /// температура и рассудок — в реальных секундах.
    /// </summary>
    public sealed class VitalsModel
    {
        // ── Пределы и скорости (подбираются на плейтесте) ──
        public const float BaseMaxHealth = 100f;
        public const float MaxCalories = 3000f, CaloriesPerHour = 62.5f;   // полная сытость — на 2 суток
        public const float MaxWater = 3000f, WaterPerHour = 125f;          // полная вода — на сутки
        public const float RestPerHour = 2.5f, SleepRestPerHour = 12.5f;  // бодрость 40 ч, сон 8 ч
        public const float NormalBodyTemp = 36.6f;
        public const float ComfortMin = 16f, ComfortMax = 28f;
        public const float BaseMaxStamina = 100f, StaminaRegen = 15f, StaminaRegenDelay = 1f, StaminaRecover = 0.25f;
        public const float DarkLight = 0.12f;
        public const float TraumaHours = 8f, TraumaPerStack = 0.2f;
        public const float FloorSleepQuality = 0.5f, FloorRestCap = 60f, UnrestedHours = 6f;

        /// <summary>«Смотри на меня»: потеря рассудка в секунду взгляда, секунды до «Отпечатка», сброс накопления.</summary>
        public const float LookAtMeSanityDrain = 1.5f, ImprintExposure = 20f, ImprintForget = 30f;

        /// <summary>Сколько часов сна снимают «Отпечаток»; максимум рассудка с ним.</summary>
        public const float ImprintSleepHours = 2f, ImprintSanityCap = 50f;

        /// <summary>Восстановление рассудка в оазисе, в секунду.</summary>
        public const float OasisSanityRegen = 0.4f;

        public float Health { get; private set; } = BaseMaxHealth;
        public float Calories { get; private set; } = 2000f;
        public float Water { get; private set; } = 2000f;
        public float Rest { get; private set; } = 100f;
        public float Sanity { get; private set; } = 100f;
        public float BodyTemperature { get; private set; } = NormalBodyTemp;
        public float Stamina { get; private set; } = BaseMaxStamina;
        public bool StaminaExhausted { get; private set; }
        public bool IsDead => Health <= 0f;

        /// <summary>Чем погибли (для экрана смерти).</summary>
        public string DeathReason { get; private set; }

        /// <summary>Бессмертие (чит): урон и потребности не убивают.</summary>
        public bool God;

        /// <summary>Бесконечная стамина (чит).</summary>
        public bool InfiniteStamina;

        public readonly List<Status> Statuses = new();

        /// <summary>Бонусы навыков.</summary>
        public VitalsPerks Perks = VitalsPerks.None;

        // Рацион за последние сутки: (возраст в игровых часах, жиры, белки, углеводы).
        private readonly List<(float age, float fat, float protein, float carbs)> _diet = new();
        private float _staminaDelay;
        private float _sinceLook = float.MaxValue, _sleptHours;
        private readonly System.Random _random;

        public VitalsModel(int seed = 12345) => _random = new System.Random(seed);

        // ───────────────────────────── Производные ─────────────────────────────

        /// <summary>Максимум рассудка: «Отпечаток» держит его на половине.</summary>
        public float MaxSanity => Has(StatusKind.Imprint) ? ImprintSanityCap : 100f;

        /// <summary>Накопленные секунды взгляда на «Смотри на меня» (с последнего сброса).</summary>
        public float LookExposure { get; private set; }

        public float MaxHealth => BaseMaxHealth * Mathf.Max(0.2f, 1f - TraumaPerStack * TraumaStacks);

        public int TraumaStacks => Find(StatusKind.NearDeathTrauma)?.Stacks ?? 0;

        public float MaxStamina
        {
            get
            {
                float max = BaseMaxStamina * Mathf.Max(0.2f, 1f - TraumaPerStack * TraumaStacks);
                if (Calories <= 0f) max *= 0.7f;
                else if (Calories < MaxCalories * 0.15f) max *= 0.85f;
                if (Has(StatusKind.Infection) && Find(StatusKind.Infection).Intensity > 0.3f) max *= 0.8f;
                if (IsWellFed) max *= 1.1f;
                if (Has(StatusKind.Unrested)) max *= 0.85f;
                max *= 1f + Perks.StaminaBonus;
                return max;
            }
        }

        public float CaloriesFraction => Calories / MaxCalories;
        public float WaterFraction => Water / MaxWater;

        /// <summary>Доли калорий из жиров, белков и углеводов за сутки (0, если почти ничего не ели).</summary>
        public (float fat, float protein, float carbs) DietShares
        {
            get
            {
                float fat = 0f, protein = 0f, carbs = 0f;
                foreach (var entry in _diet)
                {
                    fat += entry.fat * 9f;
                    protein += entry.protein * 4f;
                    carbs += entry.carbs * 4f;
                }
                float total = fat + protein + carbs;
                return total < 400f ? (0f, 0f, 0f) : (fat / total, protein / total, carbs / total);
            }
        }

        /// <summary>Рацион за сутки сбалансирован: белки 12–30%, жиры 15–40%, углеводы 35–70% калорий.</summary>
        public bool? DietBalanced
        {
            get
            {
                var (fat, protein, carbs) = DietShares;
                if (fat + protein + carbs <= 0f)
                    return null;
                return protein >= 0.12f && protein <= 0.3f && fat >= 0.15f && fat <= 0.4f && carbs >= 0.35f && carbs <= 0.7f;
            }
        }

        public bool IsWellFed => DietBalanced == true && CaloriesFraction > 0.6f && WaterFraction > 0.4f;

        /// <summary>Во сколько раз медленнее удары: сломанная рука ×1.5, в шине ×1.2.</summary>
        public float SwingMultiplier
        {
            get
            {
                float multiplier = 1f;
                foreach (var status in Statuses)
                    if (status.Kind == StatusKind.FractureArm)
                        multiplier *= status.Splinted ? 1.2f : 1.5f;
                return multiplier;
            }
        }

        public bool Has(StatusKind kind) => Find(kind) != null;

        public Status Find(StatusKind kind)
        {
            foreach (var status in Statuses)
                if (status.Kind == kind)
                    return status;
            return null;
        }

        // ───────────────────────────── Тик ─────────────────────────────

        /// <param name="realDt">Реальные секунды.</param>
        /// <param name="gameHours">Прошедшие игровые часы (сон их ускоряет).</param>
        public void Tick(float realDt, float gameHours, in VitalsEnvironment env)
        {
            if (IsDead)
                return;
            TickNeeds(gameHours, env);
            TickTemperature(realDt, env);
            TickStatuses(realDt, gameHours);
            TickSanity(realDt, gameHours, env);
            TickHealth(realDt, gameHours, env);
            TickStamina(realDt, env);
        }

        private void TickNeeds(float gameHours, in VitalsEnvironment env)
        {
            float calorieMul = BodyTemperature < 36f ? 1.3f : 1f;
            float waterMul = (BodyTemperature > 37.4f ? 1.5f : 1f) + (Has(StatusKind.Poisoning) ? 1.5f : 0f);
            Calories = Mathf.Max(0f, Calories - CaloriesPerHour * calorieMul * gameHours);
            Water = Mathf.Max(0f, Water - WaterPerHour * waterMul * gameHours);
            if (env.Sleeping)
            {
                float cap = SleepCap(env);
                if (Rest < cap)
                    Rest = Mathf.Min(cap, Rest + SleepRestPerHour * SleepQuality(env) * gameHours);
            }
            else
            {
                Rest = Mathf.Max(0f, Rest - RestPerHour * (Has(StatusKind.Unrested) ? 1.3f : 1f) * gameHours);
            }
            for (int i = _diet.Count - 1; i >= 0; i--)
            {
                var entry = _diet[i];
                entry.age += gameHours;
                if (entry.age > 24f)
                    _diet.RemoveAt(i);
                else
                    _diet[i] = entry;
            }
        }

        public static float SleepQuality(in VitalsEnvironment env) => env.SleepQuality > 0f ? env.SleepQuality : 1f;
        public static float SleepCap(in VitalsEnvironment env) => env.SleepRestCap > 0f ? env.SleepRestCap : 100f;

        private void TickTemperature(float realDt, in VitalsEnvironment env)
        {
            // Ощущаемая температура = воздух + одежда. Вне комфорта тело остывает или греется, в комфорте — к норме.
            float felt = env.AirTemperature + env.Warmth;
            float cold = Mathf.Max(0f, ComfortMin - Perks.ComfortWiden - felt), heat = Mathf.Max(0f, felt - ComfortMax - Perks.ComfortWiden);
            if (cold > 0f)
                BodyTemperature -= cold * 0.0008f * realDt;
            else if (heat > 0f)
                BodyTemperature += heat * 0.0008f * realDt;
            else
                BodyTemperature = Mathf.MoveTowards(BodyTemperature, NormalBodyTemp, 0.01f * realDt);
            BodyTemperature = Mathf.Clamp(BodyTemperature, 30f, 42f);
        }

        private void TickStatuses(float realDt, float gameHours)
        {
            for (int i = Statuses.Count - 1; i >= 0; i--)
            {
                var status = Statuses[i];
                status.Age += gameHours;
                status.Remaining -= gameHours;
                switch (status.Kind)
                {
                    case StatusKind.BleedingLight:
                        Hurt(0.12f * realDt, "Истёк кровью");
                        // Лёгкое кровотечение иногда останавливается само (≈30% за игровой час).
                        if (Roll(0.3f * gameHours))
                            status.Remaining = 0f;
                        // Открытая рана дольше 2 часов может загноиться.
                        if (status.Age > 2f && !Has(StatusKind.Infection) && Roll(0.25f * gameHours))
                            Add(StatusKind.Infection, intensity: 0f);
                        break;
                    case StatusKind.BleedingHeavy:
                        Hurt(0.5f * realDt, "Истёк кровью");
                        break;
                    case StatusKind.Infection:
                        status.Intensity = Mathf.Min(1f, status.Intensity + 0.1f * gameHours);
                        if (status.Intensity > 0.5f)
                            Hurt(0.02f * realDt, "Умер от инфекции");
                        break;
                    case StatusKind.Poisoning:
                        Hurt(0.05f * realDt, "Умер от отравления");
                        break;
                    case StatusKind.Pain:
                        status.Intensity -= 0.25f * gameHours;
                        if (status.Intensity <= 0f)
                            status.Remaining = 0f;
                        break;
                }
                if (status.Remaining <= 0f)
                    Statuses.RemoveAt(i);
            }
        }

        private void TickSanity(float realDt, float gameHours, in VitalsEnvironment env)
        {
            // Сон восстанавливает рассудок по игровым часам (+12 за час), бодрствование — по реальным секундам.
            TickImprint(realDt, gameHours, env);
            if (env.Sleeping)
            {
                Sanity = Mathf.Min(MaxSanity, Sanity + 12f * SleepQuality(env) * gameHours);
                return;
            }
            bool dark = env.Light < DarkLight && !env.HasOwnLight;
            float rate;
            if (env.LookingAtAnomaly)
                rate = -LookAtMeSanityDrain;
            else if (env.InAnomaly)
                rate = -0.3f;
            else if (env.InOasis)
                rate = OasisSanityRegen;
            else if (dark)
                rate = -0.12f * (env.IsNight ? 1.5f : 1f);
            else
                rate = 0.05f;
            if (rate < 0f)
                rate *= Rest <= 0f ? 2f : Rest < 25f ? 1.5f : 1f;
            if (rate < 0f && Has(StatusKind.Unrested))
                rate *= 1.3f;
            if (rate < 0f)
                rate *= Perks.SanityDrainMul;
            Sanity = Mathf.Clamp(Sanity + rate * realDt, 0f, MaxSanity);
        }

        /// <summary>
        /// «Отпечаток»: секунды взгляда копятся (сбрасываются, если ImprintForget секунд не смотреть), на
        /// ImprintExposure — состояние. Снимается ImprintSleepHours часами сна подряд.
        /// </summary>
        private void TickImprint(float realDt, float gameHours, in VitalsEnvironment env)
        {
            if (env.LookingAtAnomaly)
            {
                _sinceLook = 0f;
                LookExposure += realDt;
                if (LookExposure >= ImprintExposure && !Has(StatusKind.Imprint))
                    Add(StatusKind.Imprint);
            }
            else
            {
                _sinceLook += realDt;
                if (_sinceLook >= ImprintForget)
                    LookExposure = 0f;
            }
            _sleptHours = env.Sleeping ? _sleptHours + gameHours : 0f;
            if (_sleptHours >= ImprintSleepHours && Has(StatusKind.Imprint))
            {
                Statuses.RemoveAll(s => s.Kind == StatusKind.Imprint);
                LookExposure = 0f;
            }
        }

        private void TickHealth(float realDt, float gameHours, in VitalsEnvironment env)
        {
            if (Calories <= 0f) Hurt(0.03f * realDt, "Умер от голода");
            if (Water <= 0f) Hurt(0.08f * realDt, "Умер от жажды");
            if (BodyTemperature < 35f) Hurt(0.03f * realDt, "Замёрз");
            if (BodyTemperature > 39f) Hurt(0.03f * realDt, "Перегрелся");

            bool bleeding = Has(StatusKind.BleedingLight) || Has(StatusKind.BleedingHeavy);
            if (!bleeding && CaloriesFraction > 0.2f && WaterFraction > 0.2f)
            {
                float regen = env.Sleeping ? 6f : 2f;
                if (IsWellFed)
                    regen *= 1.5f;
                Health = Mathf.Min(MaxHealth, Health + regen * gameHours);
            }
            // Светящаяся лужа лечит и при кровотечении (запас лужи уже списан).
            if (env.PuddleHeal > 0f)
                Health += env.PuddleHeal * realDt;
            Health = Mathf.Min(Health, MaxHealth);
        }

        private void TickStamina(float realDt, in VitalsEnvironment env)
        {
            float max = MaxStamina;
            if (_staminaDelay > 0f)
            {
                _staminaDelay -= realDt;
            }
            else
            {
                float regen = StaminaRegen;
                if (Water < MaxWater * 0.15f) regen *= 0.7f;
                if (BodyTemperature < 36f) regen *= 0.8f;
                if (DietBalanced == false) regen *= 0.85f;
                if (Has(StatusKind.Pain) && !Has(StatusKind.Painkiller)) regen *= 0.8f;
                Stamina = Mathf.Min(max, Stamina + regen * realDt);
            }
            Stamina = Mathf.Min(Stamina, max);
            if (StaminaExhausted && Stamina >= max * StaminaRecover)
                StaminaExhausted = false;
        }

        // ───────────────────────────── Стамина (IStaminaPool) ─────────────────────────────

        public bool TryConsumeStamina(float amount)
        {
            amount *= Perks.StaminaCostMul;
            if (InfiniteStamina)
                return true;
            if (Stamina < amount)
                return false;
            SpendStamina(amount);
            return true;
        }

        public void DrainStamina(float amount)
        {
            amount *= Perks.StaminaCostMul;
            if (!InfiniteStamina)
                SpendStamina(Mathf.Min(amount, Stamina));
        }

        private void SpendStamina(float amount)
        {
            Stamina -= amount;
            _staminaDelay = StaminaRegenDelay;
            if (Stamina <= 0.001f)
            {
                Stamina = 0f;
                StaminaExhausted = true;
            }
        }

        // ───────────────────────────── Урон и лечение ─────────────────────────────

        /// <summary>Урон здоровью. reason — причина для экрана смерти.</summary>
        public void Hurt(float amount, string reason)
        {
            if (amount <= 0f || IsDead)
                return;
            Health -= amount;
            if (Health <= 0f)
            {
                if (God)
                {
                    Health = 1f;
                    return;
                }
                Health = 0f;
                DeathReason = reason;
            }
        }

        /// <summary>Здоровье ставится в значение (аномалия-глитч). 0 — смерть.</summary>
        public void SetHealth(float value, string reason)
        {
            if (IsDead)
                return;
            Health = Mathf.Clamp(value, 0f, MaxHealth);
            if (Health <= 0f)
            {
                if (God)
                {
                    Health = 1f;
                    return;
                }
                DeathReason = reason;
            }
        }

        public void Kill(string reason)
        {
            if (IsDead)
                return;
            Health = 0f;
            DeathReason = reason;
        }

        public Status Add(StatusKind kind, float hours = float.PositiveInfinity, float intensity = 1f)
        {
            var existing = Find(kind);
            if (existing != null)
            {
                existing.Intensity = Mathf.Max(existing.Intensity, intensity);
                existing.Remaining = Mathf.Max(existing.Remaining, hours);
                if (kind == StatusKind.NearDeathTrauma)
                {
                    existing.Stacks++;
                    existing.Remaining = hours;
                }
                return existing;
            }
            // Сильное кровотечение поглощает лёгкое.
            if (kind == StatusKind.BleedingLight && Has(StatusKind.BleedingHeavy))
                return Find(StatusKind.BleedingHeavy);
            if (kind == StatusKind.BleedingHeavy)
                Remove(StatusKind.BleedingLight);
            var status = new Status { Kind = kind, Remaining = hours, Intensity = intensity };
            Statuses.Add(status);
            return status;
        }

        public bool Remove(StatusKind kind)
        {
            for (int i = 0; i < Statuses.Count; i++)
                if (Statuses[i].Kind == kind)
                {
                    Statuses.RemoveAt(i);
                    return true;
                }
            return false;
        }

        /// <summary>Падение: урон, боль, шанс перелома ноги (правила MovementRules).</summary>
        public void ApplyFall(float impactSpeed)
        {
            var severity = MovementRules.ClassifyFall(impactSpeed);
            if (severity == MovementRules.FallSeverity.None)
                return;
            float damage = ((impactSpeed - MovementRules.FallDamageSpeed) * 8f + 5f) * Perks.FallDamageMul;
            Add(StatusKind.Pain, intensity: Mathf.Clamp01(damage / 40f));
            if (severity == MovementRules.FallSeverity.Fracture && Roll(0.7f))
                AddFracture(StatusKind.FractureLeg);
            Hurt(damage, "Разбился при падении");
        }

        /// <summary>Удар тяжёлым (обломок, летящий проп): урон, боль, шанс перелома.</summary>
        public void ApplyImpact(float damage, string reason)
        {
            if (damage <= 1f)
                return;
            Add(StatusKind.Pain, intensity: Mathf.Clamp01(damage / 50f));
            if (damage > 25f && Roll(0.35f))
                AddFracture(Roll(0.5f) ? StatusKind.FractureArm : StatusKind.FractureLeg);
            if (damage > 15f && Roll(0.3f))
                Add(StatusKind.BleedingLight);
            Hurt(damage, reason);
        }

        /// <summary>Порез (стекло, острый металл голыми руками).</summary>
        public void ApplyCut(float damage)
        {
            Add(damage > 12f ? StatusKind.BleedingHeavy : StatusKind.BleedingLight);
            Add(StatusKind.Pain, intensity: 0.3f);
            Hurt(damage, "Истёк кровью");
        }

        private void AddFracture(StatusKind kind)
        {
            var status = Add(kind);
            status.Splinted = false;
            status.Remaining = float.PositiveInfinity;
        }

        /// <summary>Съесть или выпить: калории, вода, рацион; испорченное отравляет.</summary>
        public void Consume(ItemDef item, bool spoiled)
        {
            Calories = Mathf.Min(MaxCalories, Calories + item.Calories * (spoiled ? 0.5f : 1f) * (item.Cooked ? Perks.CookMul : 1f));
            Water = Mathf.Min(MaxWater, Water + item.Water);
            if (item.Fat + item.Protein + item.Carbs > 0f)
                _diet.Add((0f, item.Fat, item.Protein, item.Carbs));
            // Еда немного успокаивает, горячая — заметно.
            if (item.Calories > 0f || item.Cooked)
                Sanity = Mathf.Min(MaxSanity, Sanity + (item.Cooked ? 6f : 2f));
            if (spoiled)
            {
                Add(StatusKind.Poisoning, hours: 1.5f);
                Water = Mathf.Max(0f, Water - 300f);
            }
            else if (item.Water > 0f && Find(StatusKind.Poisoning) is { } poisoning)
            {
                // Вода выводит отраву: каждые 500 мл сокращают отравление на игровой час.
                poisoning.Remaining -= item.Water / 500f;
                if (poisoning.Remaining <= 0f)
                    Remove(StatusKind.Poisoning);
            }
            if (item.Treats != MedicalTreatment.None || item.Heal > 0f)
                Treat(item.Treats, item.Heal + Perks.HealBonus);
        }

        /// <summary>Лекарство: что лечит и сколько здоровья даёт.</summary>
        public void Treat(MedicalTreatment treats, float heal)
        {
            if ((treats & MedicalTreatment.Bleeding) != 0)
            {
                // Бинт: сильное кровотечение становится лёгким, лёгкое останавливается.
                if (Remove(StatusKind.BleedingHeavy))
                    Add(StatusKind.BleedingLight);
                else
                    Remove(StatusKind.BleedingLight);
            }
            if ((treats & MedicalTreatment.Fracture) != 0)
            {
                // Шина — на первый незафиксированный перелом: нога важнее руки.
                var fracture = Unsplinted(StatusKind.FractureLeg) ?? Unsplinted(StatusKind.FractureArm);
                if (fracture != null)
                {
                    fracture.Splinted = true;
                    fracture.Remaining = Perks.SplintHours;
                }
            }
            if ((treats & MedicalTreatment.Pain) != 0)
                Add(StatusKind.Painkiller, hours: 3f);
            if ((treats & MedicalTreatment.Infection) != 0)
                Remove(StatusKind.Infection);
            if ((treats & MedicalTreatment.Poisoning) != 0)
                Remove(StatusKind.Poisoning);
            Health = Mathf.Min(MaxHealth, Health + heal);
        }

        private Status Unsplinted(StatusKind kind)
        {
            foreach (var status in Statuses)
                if (status.Kind == kind && !status.Splinted)
                    return status;
            return null;
        }

        /// <summary>Поможет ли лекарство сейчас (иначе его не тратим). Null — поможет, иначе — почему нет.</summary>
        public string WhyUseless(ItemDef item)
        {
            if (item.Category != ItemCategory.Medicine)
                return null;
            var treats = item.Treats;
            bool useful = item.Heal > 0f && Health < MaxHealth - 1f;
            useful |= (treats & MedicalTreatment.Bleeding) != 0 && (Has(StatusKind.BleedingLight) || Has(StatusKind.BleedingHeavy));
            useful |= (treats & MedicalTreatment.Fracture) != 0 && (Unsplinted(StatusKind.FractureLeg) != null || Unsplinted(StatusKind.FractureArm) != null);
            useful |= (treats & MedicalTreatment.Pain) != 0 && Has(StatusKind.Pain);
            useful |= (treats & MedicalTreatment.Infection) != 0 && Has(StatusKind.Infection);
            useful |= (treats & MedicalTreatment.Poisoning) != 0 && Has(StatusKind.Poisoning);
            return useful ? null : $"{item.DisplayName}: сейчас не нужно";
        }

        // ───────────────────────────── Смерть и возрождение ─────────────────────────────

        /// <summary>Возрождение: здоровье — до максимума с учётом травмы, нужды — не ниже половины, травмы сняты.</summary>
        public void Respawn()
        {
            Statuses.RemoveAll(s => s.Kind != StatusKind.NearDeathTrauma);
            Add(StatusKind.NearDeathTrauma, hours: TraumaHours);
            Health = MaxHealth;
            Calories = Mathf.Max(Calories, MaxCalories * 0.5f);
            Water = Mathf.Max(Water, MaxWater * 0.5f);
            Rest = Mathf.Max(Rest, 50f);
            Sanity = Mathf.Max(Sanity, 60f);
            BodyTemperature = NormalBodyTemp;
            Stamina = MaxStamina;
            StaminaExhausted = false;
            DeathReason = null;
        }

        // ───────────────────────────── Отладка ─────────────────────────────

        public void SetNeeds(float? calories = null, float? water = null, float? rest = null, float? sanity = null, float? bodyTemperature = null)
        {
            if (calories.HasValue) Calories = Mathf.Clamp(calories.Value, 0f, MaxCalories);
            if (water.HasValue) Water = Mathf.Clamp(water.Value, 0f, MaxWater);
            if (rest.HasValue) Rest = Mathf.Clamp(rest.Value, 0f, 100f);
            if (sanity.HasValue) Sanity = Mathf.Clamp(sanity.Value, 0f, MaxSanity);
            if (bodyTemperature.HasValue) BodyTemperature = Mathf.Clamp(bodyTemperature.Value, 30f, 42f);
        }

        public void SetStamina(float value)
        {
            Stamina = Mathf.Clamp(value, 0f, MaxStamina);
            StaminaExhausted = false;
        }

        /// <summary>Полностью вылечить (чит).</summary>
        public void HealAll()
        {
            Statuses.RemoveAll(s => s.Kind != StatusKind.NearDeathTrauma);
            Health = MaxHealth;
            Calories = MaxCalories;
            Water = MaxWater;
            Rest = 100f;
            Sanity = MaxSanity;
            BodyTemperature = NormalBodyTemp;
            Stamina = MaxStamina;
            StaminaExhausted = false;
        }

        // ───────────────────────────── Движение ─────────────────────────────

        public void ApplyModifiers(ref MovementModifiers m)
        {
            int legs = 0, splintedLegs = 0, arms = 0;
            float pain = 0f;
            foreach (var status in Statuses)
            {
                switch (status.Kind)
                {
                    case StatusKind.FractureLeg:
                        if (status.Splinted) splintedLegs++;
                        else legs++;
                        break;
                    case StatusKind.FractureArm:
                        arms += status.Splinted ? 0 : 1;
                        m.SwayAmount += status.Splinted ? 0.15f : 0f;
                        break;
                    case StatusKind.Pain:
                        pain = Mathf.Max(pain, status.Intensity);
                        break;
                }
            }
            MovementRules.ApplyLegFractures(ref m, legs);
            if (splintedLegs > 0)
            {
                m.SpeedMul *= 0.85f;
                m.CanJump = false;
                m.LimpAmount = Mathf.Max(m.LimpAmount, 0.3f);
            }
            MovementRules.ApplyArmInjury(ref m, arms);
            MovementRules.ApplyPain(ref m, Has(StatusKind.Painkiller) ? pain * 0.2f : pain);

            if (Calories <= 0f) m.CanSprint = false;
            if (Rest <= 0f) m.CanSprint = false;
            else if (Rest < 25f) m.SpeedMul *= 0.92f;
            if (BodyTemperature < 36f) m.SpeedMul *= 0.95f;
            if (BodyTemperature < 35f) m.SwayAmount += 0.3f;
            if (Sanity <= 0f)
            {
                m.SwayAmount += 0.6f;
                m.CanSprint = false;
            }
            else if (Sanity < 20f)
            {
                m.SwayAmount += 0.25f;
            }
        }

        // ───────────────────────────── Состояния для интерфейса ─────────────────────────────

        /// <summary>Все действующие состояния: хранимые и вычисляемые из показателей.</summary>
        public void GetConditions(List<StatusKind> output)
        {
            output.Clear();
            foreach (var status in Statuses)
                output.Add(status.Kind);
            if (Calories <= 0f) output.Add(StatusKind.Starving);
            else if (CaloriesFraction < 0.15f) output.Add(StatusKind.Hungry);
            if (Water <= 0f) output.Add(StatusKind.Dehydrated);
            else if (WaterFraction < 0.15f) output.Add(StatusKind.Thirsty);
            if (Rest <= 0f) output.Add(StatusKind.Exhausted);
            else if (Rest < 25f) output.Add(StatusKind.Tired);
            if (BodyTemperature < 35f) output.Add(StatusKind.Hypothermia);
            else if (BodyTemperature < 36f) output.Add(StatusKind.Cold);
            if (BodyTemperature > 39f) output.Add(StatusKind.Heatstroke);
            else if (BodyTemperature > 37.4f) output.Add(StatusKind.Hot);
            if (DietBalanced == false) output.Add(StatusKind.PoorDiet);
            if (IsWellFed) output.Add(StatusKind.WellFed);
            if (Sanity <= 0f) output.Add(StatusKind.Panic);
            else if (Sanity < 40f) output.Add(StatusKind.Anxious);
        }

        private bool Roll(float chance) => chance > 0f && _random.NextDouble() < chance;
    }
}
