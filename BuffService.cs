using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Entities;

namespace SoloBuffs
{
    internal static class BuffService
    {
        private static readonly PrefabGUID CarrierBuff = new PrefabGUID(1774716596);
        private static PrefabGUID VisualBuff => new PrefabGUID(Plugin.VisualBuffGUID);

        public static bool ApplyBuff(Entity user, Entity character)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return false;

            var em = serverWorld.EntityManager;

            // Don't rebuild the stat buffer every scan, it desyncs stats
            Entity existingBuff = default;
            if (BuffUtility.TryGetBuff<EntityManager>(em, character, CarrierBuff, out existingBuff))
            {
                // Buffer can come back empty after a restart
                if (!em.HasComponent<ModifyUnitStatBuff_DOTS>(existingBuff) ||
                    em.GetBuffer<ModifyUnitStatBuff_DOTS>(existingBuff).Length == 0)
                {
                    Plugin.Instance?.Log.LogInfo("[SoloBuffs] Existing buff had an empty stat buffer (likely post-reboot) - repopulating.");
                    ConfigurePermanentBuff(em, existingBuff);
                    PopulateStatBuffer(em, existingBuff);
                }

                ApplyVisualBuff(user, character, em);
                return true;
            }

            if (em.HasComponent<Health>(character))
            {
                var h = em.GetComponentData<Health>(character);
                Plugin.Instance?.Log.LogInfo($"[SoloBuffs] HP before ApplyBuff (new): current={h.Value} max={h.MaxHealth}");
            }

            var debugEvents = serverWorld.GetExistingSystemManaged<DebugEventsSystem>();
            var applyEvent = new ApplyBuffDebugEvent { BuffPrefabGUID = CarrierBuff };
            var fromChar = new FromCharacter { User = user, Character = character };
            debugEvents.ApplyBuff(fromChar, applyEvent);

            Entity buffEntity = default;
            if (!BuffUtility.TryGetBuff<EntityManager>(em, character, CarrierBuff, out buffEntity))
                return false;

            ConfigurePermanentBuff(em, buffEntity);
            PopulateStatBuffer(em, buffEntity);
            ApplyVisualBuff(user, character, em);

            if (em.HasComponent<Health>(character))
            {
                var h = em.GetComponentData<Health>(character);
                Plugin.Instance?.Log.LogInfo($"[SoloBuffs] HP after ApplyBuff (new): current={h.Value} max={h.MaxHealth}");
            }

            return true;
        }

        // Only for config reload, not the periodic scan
        public static void RefreshBuff(Entity character)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return;

            var em = serverWorld.EntityManager;
            if (!BuffUtility.TryGetBuff<EntityManager>(em, character, CarrierBuff, out Entity buffEntity))
                return;

            ConfigurePermanentBuff(em, buffEntity);
            PopulateStatBuffer(em, buffEntity);
        }

        public static void RemoveBuff(Entity character)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return;

            var em = serverWorld.EntityManager;

            if (em.HasComponent<Health>(character))
            {
                var h = em.GetComponentData<Health>(character);
                Plugin.Instance?.Log.LogInfo($"[SoloBuffs] HP before RemoveBuff: current={h.Value} max={h.MaxHealth}");
            }

            Entity buffEntity = default;
            if (BuffUtility.TryGetBuff<EntityManager>(em, character, CarrierBuff, out buffEntity))
                DestroyUtility.Destroy(em, buffEntity, DestroyDebugReason.TryRemoveBuff);

            RemoveVisualBuff(character, em);

            if (em.HasComponent<Health>(character))
            {
                var h = em.GetComponentData<Health>(character);
                Plugin.Instance?.Log.LogInfo($"[SoloBuffs] HP after RemoveBuff: current={h.Value} max={h.MaxHealth}");
            }
        }

        public static bool HasBuff(Entity character)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) return false;
            Entity buffEntity = default;
            return BuffUtility.TryGetBuff<EntityManager>(serverWorld.EntityManager, character, CarrierBuff, out buffEntity);
        }

        private static void ApplyVisualBuff(Entity user, Entity character, EntityManager em)
        {
            var visualGuid = VisualBuff;
            Entity existing = default;
            if (BuffUtility.TryGetBuff<EntityManager>(em, character, visualGuid, out existing))
                return;

            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null) return;

            var debugEvents = serverWorld.GetExistingSystemManaged<DebugEventsSystem>();
            var applyEvent = new ApplyBuffDebugEvent { BuffPrefabGUID = visualGuid };
            var fromChar = new FromCharacter { User = user, Character = character };
            debugEvents.ApplyBuff(fromChar, applyEvent);

            Entity visualEntity = default;
            if (BuffUtility.TryGetBuff<EntityManager>(em, character, visualGuid, out visualEntity))
            {
                if (!em.HasComponent<Buff_Persists_Through_Death>(visualEntity))
                    em.AddComponent<Buff_Persists_Through_Death>(visualEntity);

                if (em.HasComponent<LifeTime>(visualEntity))
                {
                    var lifeTime = em.GetComponentData<LifeTime>(visualEntity);
                    lifeTime.Duration = -1f;
                    lifeTime.EndAction = LifeTimeEndAction.None;
                    em.SetComponentData(visualEntity, lifeTime);
                }

                if (em.HasComponent<RemoveBuffOnGameplayEvent>(visualEntity))
                    em.RemoveComponent<RemoveBuffOnGameplayEvent>(visualEntity);
                if (em.HasComponent<RemoveBuffOnGameplayEventEntry>(visualEntity))
                    em.RemoveComponent<RemoveBuffOnGameplayEventEntry>(visualEntity);
            }
        }

        public static void RemoveVisualBuff(Entity character, EntityManager em)
        {
            var visualGuid = VisualBuff;
            Entity visualEntity = default;
            if (BuffUtility.TryGetBuff<EntityManager>(em, character, visualGuid, out visualEntity))
                DestroyUtility.Destroy(em, visualEntity, DestroyDebugReason.TryRemoveBuff);
        }

        private static void ConfigurePermanentBuff(EntityManager em, Entity buffEntity)
        {
            if (em.HasComponent<CreateGameplayEventsOnSpawn>(buffEntity))
                em.RemoveComponent<CreateGameplayEventsOnSpawn>(buffEntity);
            if (em.HasComponent<GameplayEventListeners>(buffEntity))
                em.RemoveComponent<GameplayEventListeners>(buffEntity);
            if (em.HasComponent<RemoveBuffOnGameplayEvent>(buffEntity))
                em.RemoveComponent<RemoveBuffOnGameplayEvent>(buffEntity);
            if (em.HasComponent<RemoveBuffOnGameplayEventEntry>(buffEntity))
                em.RemoveComponent<RemoveBuffOnGameplayEventEntry>(buffEntity);

            if (!em.HasComponent<Buff_Persists_Through_Death>(buffEntity))
                em.AddComponent<Buff_Persists_Through_Death>(buffEntity);

            if (em.HasComponent<LifeTime>(buffEntity))
            {
                var lifeTime = em.GetComponentData<LifeTime>(buffEntity);
                lifeTime.Duration = -1f;
                lifeTime.EndAction = LifeTimeEndAction.None;
                em.SetComponentData(buffEntity, lifeTime);
            }
        }

        private static void PopulateStatBuffer(EntityManager em, Entity buffEntity)
        {
            var buf = em.AddBuffer<ModifyUnitStatBuff_DOTS>(buffEntity);
            buf.Clear();

            AddStat(buf, UnitStatType.AbilityAttackSpeed, Plugin.SoloAttackSpeedPercent!.Value, Plugin.SoloAttackSpeedType!.Value);
            AddStat(buf, UnitStatType.PrimaryAttackSpeed, Plugin.SoloAttackSpeedPercent!.Value, Plugin.SoloAttackSpeedType!.Value);
            AddStat(buf, UnitStatType.PhysicalPower, Plugin.SoloDamagePercent!.Value, Plugin.SoloDamageType!.Value);
            AddStat(buf, UnitStatType.SpellPower, Plugin.SoloSpellDamagePercent!.Value, Plugin.SoloSpellDamageType!.Value);
            AddStat(buf, UnitStatType.PhysicalCriticalStrikeChance, Plugin.SoloCritChancePercent!.Value, Plugin.SoloCritChanceType!.Value);
            AddStat(buf, UnitStatType.SpellCriticalStrikeChance, Plugin.SoloCritChancePercent!.Value, Plugin.SoloCritChanceType!.Value);
            AddStat(buf, UnitStatType.PhysicalCriticalStrikeDamage, Plugin.SoloCritDamagePercent!.Value, Plugin.SoloCritDamageType!.Value);
            AddStat(buf, UnitStatType.SpellCriticalStrikeDamage, Plugin.SoloCritDamagePercent!.Value, Plugin.SoloCritDamageType!.Value);
            AddStat(buf, UnitStatType.MaxHealth, Plugin.SoloHealthPercent!.Value, Plugin.SoloHealthType!.Value);
            AddStat(buf, UnitStatType.SpellLifeLeech, Plugin.SoloSpellLeechPercent!.Value, Plugin.SoloSpellLeechType!.Value);
            AddStat(buf, UnitStatType.PhysicalLifeLeech, Plugin.SoloPhysicalLeechPercent!.Value, Plugin.SoloPhysicalLeechType!.Value);
            AddStat(buf, UnitStatType.PhysicalResistance, Plugin.SoloPhysicalResistancePercent!.Value, Plugin.SoloPhysicalResistanceType!.Value);
            AddStat(buf, UnitStatType.SpellResistance, Plugin.SoloSpellResistancePercent!.Value, Plugin.SoloSpellResistanceType!.Value);
            AddStat(buf, UnitStatType.ResourceYield, Plugin.SoloResourceYieldPercent!.Value, Plugin.SoloResourceYieldType!.Value);

            float msValue = Plugin.SoloMoveSpeedPercent!.Value;
            AddFlat(buf, UnitStatType.MovementSpeed, msValue + 1f);
        }

        private static void AddStat(DynamicBuffer<ModifyUnitStatBuff_DOTS> buf, UnitStatType stat, float value, int type)
        {
            if (value == 0f) return;
            if (type == 1) AddFlat(buf, stat, value);
            else AddMul(buf, stat, 1f + value);
        }

        private static void AddMul(DynamicBuffer<ModifyUnitStatBuff_DOTS> buf, UnitStatType stat, float value)
        {
            buf.Add(new ModifyUnitStatBuff_DOTS
            {
                StatType = stat,
                Value = value,
                ModificationType = ModificationType.Multiply,
                AttributeCapType = AttributeCapType.Uncapped,
                Modifier = 1f,
                Id = ModificationId.NewId(0)
            });
        }

        private static void AddFlat(DynamicBuffer<ModifyUnitStatBuff_DOTS> buf, UnitStatType stat, float value)
        {
            buf.Add(new ModifyUnitStatBuff_DOTS
            {
                StatType = stat,
                Value = value,
                ModificationType = ModificationType.Add,
                AttributeCapType = AttributeCapType.Uncapped,
                Modifier = 1f,
                Id = ModificationId.NewId(0)
            });
        }
    }
}