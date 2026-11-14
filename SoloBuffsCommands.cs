using ProjectM;
using ProjectM.Network;
using ProjectM.Shared;
using Unity.Entities;
using VampireCommandFramework;

namespace SoloBuffs
{
    [CommandGroup("solo")]
    internal static class SoloBuffsCommands
    {
        [Command("status", description: "Shows your Solo Buffs status.")]
        public static void SoloStatus(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) { ctx.Reply("Server world not ready yet."); return; }

            EntityManager em = serverWorld.EntityManager;

            if (!SoloBuffsLogic.TryGetStatusForUser(em, ctx.Event.SenderUserEntity,
                    out bool isSolo, out bool hasSoloBuff, out string reasonLine, out string timingLine))
            {
                ctx.Reply("Could not resolve your status.");
                return;
            }

            string soloStr = isSolo ? "<color=#ffffff>Solo</color>" : "<color=#ffffff>Not Solo</color>";
            string buffStr = hasSoloBuff ? "<color=#00FF00>Active</color>" : "<color=#F54927>Inactive</color>";
            ctx.Reply($"<color=#00FFBB>Solo Buffs Status:</color> {soloStr} | {buffStr}\n{reasonLine}\n{timingLine}");
        }

        [Command("on", description: "Enable Solo Buffs for yourself.")]
        public static void SoloOn(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) { ctx.Reply("Server world not ready yet."); return; }

            EntityManager em = serverWorld.EntityManager;
            SoloBuffsLogic.SetUserOptIn(em, ctx.Event.SenderUserEntity);
            SoloBuffsLogic.UpdateBuffForUser(em, ctx.Event.SenderUserEntity);
            ctx.Reply("Solo Buffs are <color=#00FFBB>enabled</color> - applied automatically when you are eligible.");
        }

        [Command("off", description: "Disable Solo Buffs for yourself.")]
        public static void SoloOff(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) { ctx.Reply("Server world not ready yet."); return; }

            EntityManager em = serverWorld.EntityManager;
            SoloBuffsLogic.SetUserOptOut(em, ctx.Event.SenderUserEntity);
            ctx.Reply("Solo Buffs are <color=#F54927>disabled</color> and will not be applied.");
        }

        [Command("reload", description: "Reload Solo Buffs config from disk.", adminOnly: true)]
        public static void ReloadConfig(ChatCommandContext ctx)
        {
            Plugin.ReloadConfig();
            ctx.Reply("Config reloaded from disk.");
        }

        [Command("debug", description: "Show debug info.", adminOnly: true)]
        public static void SoloDebug(ChatCommandContext ctx)
        {
            var serverWorld = SoloBuffsLogic.GetServerWorld();
            if (serverWorld == null || !serverWorld.IsCreated) { ctx.Reply("Server world not ready yet."); return; }

            EntityManager em = serverWorld.EntityManager;
            var user = em.GetComponentData<User>(ctx.Event.SenderUserEntity);
            var character = user.LocalCharacter._Entity;

            if (character == Entity.Null || !em.Exists(character)) { ctx.Reply("No character found."); return; }

            string msg = "Debug Info:\n";
            msg += $"Global Enabled: {SoloBuffsLogic.GlobalEnabled}\n";
            msg += $"Visual Buff GUID: {Plugin.VisualBuffGUID}\n";
            msg += $"Has Stat Buff: {BuffService.HasBuff(character)}\n";

            if (em.HasComponent<LifeLeech>(character))
            {
                var leech = em.GetComponentData<LifeLeech>(character);
                msg += $"LifeLeech: Phys={leech.PhysicalLifeLeechFactor._Value} Spell={leech.SpellLifeLeechFactor._Value} Primary={leech.PrimaryLeechFactor._Value}\n";
            }
            else msg += "LifeLeech: NOT FOUND\n";

            if (em.HasComponent<VampireSpecificAttributes>(character))
            {
                var attrs = em.GetComponentData<VampireSpecificAttributes>(character);
                msg += $"CritChance: Phys={attrs.PhysicalCriticalStrikeChance._Value} Spell={attrs.SpellCriticalStrikeChance._Value}\n";
                msg += $"CritDamage: Phys={attrs.PhysicalCriticalStrikeDamage._Value} Spell={attrs.SpellCriticalStrikeDamage._Value}\n";
                msg += $"ResourceYield: {attrs.ResourceYieldModifier._Value}\n";
            }
            else msg += "VampireSpecificAttributes: NOT FOUND\n";

            ctx.Reply(msg);
        }
    }

    [CommandGroup("solobuffs")]
    internal static class SoloBuffsGlobalCommands
    {
        [Command("on", description: "Globally turn on the Solo Buffs system and reset all timers.", adminOnly: true)]
        public static void GlobalOn(ChatCommandContext ctx)
        {
            if (SoloBuffsLogic.GlobalEnabled)
            {
                ctx.Reply("Solo Buffs system is already <color=#00FFBB>on</color>.");
                return;
            }
            SoloBuffsLogic.GlobalEnabled = true;
            SoloBuffsLogic.ResetAllTimers();
            SoloBuffsLogic.UpdateSoloBuffs();
            ctx.Reply("Solo Buffs system is now <color=#00FFBB>on</color> - all timers reset.");
        }

        [Command("off", description: "Globally turn off the Solo Buffs system.", adminOnly: true)]
        public static void GlobalOff(ChatCommandContext ctx)
        {
            if (!SoloBuffsLogic.GlobalEnabled)
            {
                ctx.Reply("Solo Buffs system is already <color=#F54927>off</color>.");
                return;
            }
            SoloBuffsLogic.GlobalEnabled = false;
            SoloBuffsLogic.ClearAllBuffs();
            ctx.Reply("Solo Buffs system is now <color=#F54927>off</color> - all buffs removed.");
        }
    }
}