<p align="center">
  <img src="logo.png" width="256" alt="SoloBuffs logo">
</p>

# SoloBuffs

A V Rising server mod that provides configurable stat buffs to solo players, helping balance the game for lone wolves and players whose clan members are offline.

Based on [SoloTweaker](https://github.com/Chimll/SoloTweaker) by Chimll, with the native buff engine and timer scheduling adapted from Thiaz's SoloTweaker fork.

## What's Different from SoloTweaker

- **No HookDOTS required** - removed the dependency entirely
- **Proper buff system** (from Thiaz's fork) - uses V Rising's native `ModifyUnitStatBuff_DOTS` system instead of directly modifying stat components. Stats now persist correctly without needing to reapply every frame
- **Smart scheduling** (based on Thiaz's fork) - only runs when needed (every 10 seconds or when a threshold timer expires), not every frame
- **Shared EntityQuery** - created once and reused, not recreated for every player on every scan
- **Smarter anti-exploit** - checks online AND recently online members, handles leaving and immediately creating a new clan
- **Visual buff** - glow effect on solo players
- **Global on/off switch** - admins can enable or disable the entire system live
- **Improved status command** - shows exactly why buffs are or aren't active, and when they will become available
- **Live config reload** - update values without restarting the server

## Features

- **Automatic Solo Detection** - buffs apply and remove automatically based on clan status
- **Configurable Stat Boosts** - customize every stat and whether it scales with gear or is a flat value
- **Clan Offline Threshold** - optional timer before buffs activate when clan members go offline
- **Anti-Exploit Protection** - prevents players from bypassing timers by:
  - Leaving and rejoining a clan
  - Leaving a clan and immediately creating a new one
  - Having clan members temporarily log off during raids or boss fights
- **Smart Leave Detection** - if you were already solo in your clan (all members offline past threshold), leaving does not penalize you - you keep your buffs
- **Visual Buff** - glowing visual effect on solo players
- **Per-Player Opt In/Out** - players can disable buffs for themselves

## Stat Buffs

All values and modification types are configurable. Defaults:

| Stat | Default | Type |
|------|---------|------|
| Physical Damage | +10% | Multiply |
| Spell Damage | +10% | Multiply |
| Attack Speed | +10% | Multiply |
| Max Health | +10% | Multiply |
| Movement Speed | +0.20 | Flat Add only |
| Critical Chance | +10% | Multiply |
| Critical Damage | +10% | Multiply |
| Physical Lifesteal | +10% | Add (flat) |
| Spell Lifesteal | +10% | Add (flat) |
| Physical Resistance | 0% | Add (flat) |
| Spell Resistance | 0% | Add (flat) |
| Resource Yield | +10% | Multiply |

**Multiply** - scales with your gear. +10% on 100 power = 110. +10% on 200 power = 220.
**Add** - flat value added directly. Always +X regardless of gear. Best for stats that start at 0 like lifesteal.

### Movement Speed Note

Movement speed is intentionally tuned to scale less with higher item bonuses for balance. To do that, components of the visual buff were used. Players with no speed items receive a slightly larger relative boost, while players with high speed builds receive a smaller one - keeping the gap between builds reasonable. If you wish to disable movement speed buff entirely, it is best to get rid of the visual buff as well.

| Setup | No Buff | With Buff (0.20) |
|-------|---------|-----------------|
| No items | 4.0 | 4.2 |
| 8% bonus | 4.3 | 4.4 |
| 14% bonus | 4.6 | 4.6 |
| 22% bonus (cap) | 4.9 | 4.9 |

**Important:** Do not set `MoveSpeedValue` below `0.20` or the buff will act as a nerf for players.

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for V Rising
2. Install [VampireCommandFramework](https://github.com/decaprime/VampireCommandFramework)
3. Download `SoloBuffs.dll`
4. Place it in your `BepInEx/plugins` folder
5. Start the server - config file is auto-generated on first launch

## Configuration

Edit `BepInEx/config/SoloBuffs.cfg` after first run:

```ini
[1. Combat Stats]
PhysicalDamageValue = 0.1
PhysicalDamageType = 0       # 0 = Multiply, 1 = Add
SpellDamageValue = 0.1
SpellDamageType = 0
AttackSpeedValue = 0.1
AttackSpeedType = 0
CritChanceValue = 0.1
CritChanceType = 0
CritDamageValue = 0.1
CritDamageType = 0

[2. Survivability]
HealthValue = 0.1
HealthType = 0
PhysicalLeechValue = 0.1
PhysicalLeechType = 1
SpellLeechValue = 0.1
SpellLeechType = 1
PhysicalResistanceValue = 0  # Disabled by default
PhysicalResistanceType = 1
SpellResistanceValue = 0     # Disabled by default
SpellResistanceType = 1

[3. Mobility & Utility]
# Movement speed bonus has diminishing returns for higher speed builds. 0.2 gives capped movement speed bonus as if no solo buffs were applied. Do not set below 0.20 or it will nerf players.
MoveSpeedValue = 0.2
ResourceYieldValue = 0.1
ResourceYieldType = 0

[4. Clan Settings]
ClanOfflineThresholdMinutes = 30
```

### Clan Offline Threshold

Controls how long clan members must be offline before you're considered solo:

- **0** - instant, buffs apply as soon as you're the only online member
- **30** - buffs activate 30 minutes after the last clan member goes offline
- **60** - buffs activate 1 hour after the last clan member goes offline

The timer also applies when:
- A clan member goes offline
- A clan member leaves the clan
- You leave a clan that had online or recently online members
- You create a new clan after leaving one with active members

**Exception:** If you were already solo in your clan (all members had been offline past the threshold), leaving the clan does not start a penalty timer - you keep your buffs.

## Commands

### Player Commands

| Command | Description |
|---------|-------------|
| `.solo status` | Shows your current solo status, reason, and when buffs will apply |
| `.solo on` | Re-enable Solo Buffs for yourself |
| `.solo off` | Disable Solo Buffs for yourself |

### Admin Commands

| Command | Description |
|---------|-------------|
| `.solobuffs on` | Turn the Solo Buffs system on globally and reset all timers |
| `.solobuffs off` | Turn the Solo Buffs system off globally - removes all active buffs |
| `.solo reload` | Reload config from disk without restarting the server |
| `.solo debug` | Show debug info for your character |

## Removing the Mod Safely

1. Use `.solobuffs off` to remove all active buffs for online players
2. Stop the server
3. Remove `SoloBuffs.dll` from plugins

## Building from Source

1. Clone the repository
2. Create an `interop` folder one level above the project folder and copy all files from your server's `BepInEx\interop\` into it
3. Create a `core` folder one level above the project folder and copy all files from your server's `BepInEx\core\` into it
4. Open `SoloBuffs.csproj` in Visual Studio
5. Build with **Ctrl+Shift+B** - the dll will be at `bin\Debug\net6.0\SoloBuffs.dll`

```
ParentFolder\
  SoloBuffs\    <- the cloned repo
  interop\      <- from BepInEx\interop
  core\         <- from BepInEx\core
```

## Compatibility

- **V Rising** 1.0 and later
- **BepInEx** IL2CPP version for V Rising
- **VampireCommandFramework** required
- **Server-side only** - no client mod required
- **No HookDOTS** - does not require any additional dependencies

## As an Admin

Do not listen to those who complain that the buff is rather OP. You can refer to this [video](https://www.youtube.com/watch?v=kSC3CmBmgtM) here for comparison.
The only thing that might be worth tweaking or disabling is the Critical Damage value and nothing else. Therefore test and see it for yourself.

## Known Issues

Player stats get messed up if they receive SoloBuffs while under any other buff, such as .god or .buff commands. To fix, turn off SoloBuffs and gear down entirely including passives, replenish your blood then gear back up.

## Credits

- [Chimll](https://github.com/Chimll/SoloTweaker) - original SoloTweaker mod this is based on
- [Thiaz](https://github.com/Tolky) - SoloTweaker fork: native `ModifyUnitStatBuff_DOTS` buff engine, stat carrier buff, buff refresh after reboot, and timer-based scheduling
- [jorjibosco](https://github.com/jorjibosco) - rewrite, performance improvements, anti-exploit system, visual buff, and extended feature set

## License

MIT License - feel free to modify and redistribute.