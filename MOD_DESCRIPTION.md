# Mod description

Runecarved adds rarity tiers and per-item modifiers to existing Valheim equipment. This document describes the gameplay systems in detail. For installation and project information, see the [README](README.md).

## Rarity tiers

Rarity limits are **Magic: 1 modifier**, **Rare: 2 modifiers**, and **Epic: 4 modifiers**. Rarity can be changed without losing an item's modifiers as long as the new tier's limit is not exceeded.

## Enemy modifiers

Enabled starred, untamed creatures with combat AI roll unique modifiers, up to one per star, shown beneath their healthbar. Creatures with active modifiers emit red particles. Rolls remain with the creature while it exists. Player summons are excluded, and modifiers are inactive while a creature is tamed. Eligible existing starred creatures receive a roll when they are loaded.

**Fire Infusion**, **Poison Infusion**, **Frost Infusion**, and **Lightning Infusion** each add damage of their respective element equal to 25% of the attack's total damage before blocking and resistances. Each uses the original attack damage, so multiple infusions do not compound.

Enemy-only modifiers:

- **Regrowth:** heals 2% of maximum health per second, including during combat, up to maximum health.
- **Volley:** fires four extra full-damage projectiles in a spread for every projectile fired by an attack.
- **Quickshot:** increases attack projectile speed by 50%.
- **Firetread:** while moving, leaves a 1.5-meter-radius fire patch every 0.25 seconds, using scaled campfire flame visuals. Each patch lasts 8 seconds and hits players once per second for fire damage equal to 10% of the enemy's maximum health before fire resistance and armor. Fire damage is dealt through vanilla burning; if a hit is reduced below vanilla's burning threshold, it deals no damage. Overlapping patches from the same enemy do not stack. Firetread hits are environmental: they do not trigger attack modifiers or enemy difficulty damage scaling.
- **Tarred:** landed hits on players, including fully blocked hits, apply vanilla Tarred for 10 seconds. Hits against an already Tarred player extend the remaining duration to at least 10 seconds without shortening a longer effect.
- **Healer:** while alerted to a living target, every 8 seconds heals itself and allied untamed combat creatures within 5 meters for a total of 30% of its maximum health over 4 seconds, in half-second ticks, using the Greydwarf Shaman's heal visual.
- **Caster:** while alerted to a living player target, fires a Goblin Shaman fireball every 8 seconds. Its base fire damage is 50% of the caster's maximum health before normal damage modifiers.
- **Shielded:** after 10 seconds without damage, gains a Goblin Shaman shield with health equal to 20% of its maximum health. A sound and blue particles signal a 3-second windup; hits absorbed by a shield and damage-over-time ticks reset the timer and cancel the windup.
- **Bulwark:** while alerted to a living target, every 15 seconds shields itself and allied untamed combat creatures within 5 meters. Each shield has health equal to 20% of the caster's maximum health. A sound and blue particles signal a 3-second windup; leaving combat cancels it.
- **Summoner:** while alerted to a living target, every 15 seconds marks spawn positions with evil bone pile particles, then summons zero-star copies of its own enemy type after 2 seconds. Summons have no modifiers or drops, and another cycle cannot start while any summon from the previous cycle is alive.

Volley and Quickshot only roll for prefabs in the `RangedPrefabs` setting under `Enemy Modifiers` in the mod config. Edit the comma-separated prefab IDs to adjust the list; listed enemies still need a projectile attack for these effects to work. `spawn_mod` also rejects these modifiers for prefabs outside the list. Previously saved modifiers remain active if a prefab is later removed from the list.

Enemy shields last up to 40 seconds and do not stack: the shield with the most remaining health is kept. Summoner counts are configured per prefab under `Enemy Modifiers - Summoner`; set a count to zero to disable summoning for that prefab. Saved config values override built-in defaults. If the server cannot confirm that a missing summon was destroyed, the next cycle remains blocked and a warning is logged.

Shielded, Bulwark, and Summoner use the Goblin Shaman's protection cast sound.

The built-in per-prefab defaults for enemies enabled by default are listed in `EnemyModifierSummoner.cs` under `DefaultCounts`. Enemies disabled by default still receive a summon-count config entry with a default of one, so they can be enabled later. Changing a default affects newly generated config entries, not values already saved in a player's config. Counts must be between 0 and 20.

Shared modifiers can also roll on eligible enemies:

- **Fire, Poison, Frost, and Lightning Ward:** 50% less damage from the named element.
- **Blunt, Pierce, and Slash Ironhide:** 75% less damage from the named physical damage type.
- **Fleeting:** 25% faster movement, including swimming and flying; attack speed is unchanged.
- **Armored:** 25% less damage taken, including damage-over-time ticks.
- **Shattering:** successful damaging hits stagger players, including elemental-only hits. Dodged and fully blocked hits do not trigger it.

Wards and Ironhide multiply the damage remaining after vanilla resistance and armor; Armored multiplies damage at the same stage. These reductions multiply together. A one-star enemy randomly receives one modifier, and a two-star enemy receives two distinct modifiers from the enemy and shared pools. All currently have equal roll weights. Existing rolls are preserved, and eligible creatures gain missing modifiers when the number of stars or the modifier pool increases.

New non-boss modifiers are restricted to the enemy prefabs listed in `EnemyModifierAllowlist.cs`, grouped by biome. The initial list matches the normal rune-drop enemy table, including entries with zero rune-drop weight, and each prefab is enabled by default. Change its `true` or `false` value in that source list to set the default for new configs; existing config entries keep their saved values. In `BepInEx/config/com.izzemi.izzemismod.cfg`, each `Enemy Modifiers - <biome>` section has a `true`/`false` setting per prefab. Unlisted non-boss prefabs cannot receive new modifiers. Disabling a prefab prevents new rolls and `spawn_mod` use but keeps previously assigned modifiers active; excluded enemies do not gain additional modifiers until re-enabled. Tamed creatures and player summons remain ineligible. In multiplayer, use the same settings on all peers: the enemy's network owner controls new rolls.

## Boss modifiers

Untamed bosses with combat AI roll one shared modifier and one boss modifier, independently of stars and the enemy allowlist. Their saved rolls remain unchanged, and their names appear beneath the boss healthbar. Bosses do not receive the regular enemy modifier particles.

**Guarded:** while alerted to a living target, every 30 seconds the boss summons enough guards to have up to three alive. Evil bone pile particles mark each spawn location for 2 seconds. Guards spawn with one star below 50% boss health and two stars below 25%; otherwise they have no stars. Starred guards roll modifiers under the normal enemy allowlist, and guards drop no loot or runes. Greylings and drakes are disabled in that allowlist by default. Existing guards retain their stars when the boss loses more health. If the server cannot confirm a missing guard was destroyed, its slot stays occupied.

Guard types are defined in `BossModifierSummons.cs`. When a boss has multiple options, each guard is chosen independently:

| Boss | Guards |
| --- | --- |
| Eikthyr | Greylings |
| The Elder | Greydwarfs |
| Bonemass | Draugr or Draugr ranged |
| Moder | Drakes or wolves |
| Yagluth | Fulings or Fuling archers |
| The Queen | Seekers |
| Fader | Charred marksmen or Charred melee |

**Sentry:** while alerted to a living target, every 60 seconds the boss summons one tough enemy if its previous sentry is gone. It uses the same two-second warning, health thresholds, modifier allowlist, loot restriction, and server confirmation as Guarded. Sentry types are defined alongside the guards:

| Boss | Sentry |
| --- | --- |
| Eikthyr | Greydwarf Brute |
| The Elder | Troll |
| Bonemass | Abomination |
| Moder | Stone Golem |
| Yagluth | Fuling Berserker |
| The Queen | Seeker Soldier |
| Fader | Fallen Valkyrie |

**Frenzy:** below 40% health, the boss moves 25% faster and deals 25% more attack damage, with a red healing-mead swirl while active. The bonus ends if its health rises to 40% or more. Movement bonuses multiply with Fleeting; attack speed is unchanged.

**Scaled:** the boss takes 50% less damage until a direct melee hit actually reduces its health, with a purple eitr-mead swirl while protected. That hit is reduced too; protection returns 15 seconds after the most recent damaging melee hit. Ranged attacks, damage-over-time ticks, dodged hits, and fully absorbed hits do not reset the timer. Melee hits that only apply delayed fire, poison, or spirit damage do not reset it unless they also deal immediate damage. The reduction also applies to damage-over-time ticks while active and multiplies with shared damage reductions.

**Sundering:** landed boss hits apply Sundered to players for 15 seconds, reducing their total body armor by 50%, including on the triggering hit. This includes fully blocked or shield-absorbed hits; dodged hits do not apply it. Further hits refresh the duration rather than stacking the reduction.

The Sundered icon is currently a placeholder. To replace it, add a square PNG with transparency at `IzzemisMod/Assets/Icons/Sundered.png` and rebuild the mod; 64×64 pixels matches the existing icons.

**Binding:** while alerted to a living target, every 30 seconds the boss selects a random living player within 100 meters. A message warns the selected player, and orange particles follow them for 5 seconds before vines wrap around them and apply the vanilla Immobilized effect for up to 5 seconds. The bound player or other players can attack the binding in front of the player to release them early. Its health is 100 for Eikthyr, 200 for The Elder, 300 for Bonemass, 400 for Moder, 500 for Yagluth, 600 for The Queen, 700 for Fader, and 800 for Frozen King.

## Modifier pools and value ranges

Modifier values are not fixed: each one rolls within a range, in steps. Ranges are shown as weakest to strongest. Modifiers marked *fixed* do not roll. Commands default to the strongest value and can exceed the range.

| Modifier | Range / effect | Equipment |
| --- | --- | --- |
| `base_damage` | +10% to +30% all damage types. | Weapons, bows, crossbows |
| `fire_damage`, `frost_damage`, `lightning_damage`, `poison_damage`, `spirit_damage` | +5 to +15 flat damage of that type. | Weapons, bows, crossbows |
| `stamina_cost` | −5% to −15% attack stamina. | Weapons |
| `movement_penalty` | Cancels 50% to 100% of the item's negative movement penalty, in 5% steps. | Weapons |
| `block_armour` | +10% to +30% block power. | Weapons, shields, magic weapons |
| `parry_bonus` | +0.25x to +0.75x, in 0.05 steps. | Weapons, non-tower shields |
| `backstab_bonus` | +0.5x to +1.5x, in 0.5 steps. | Weapons |
| `adrenaline_gain` | +1 to +2 on hit. | Weapons |
| `projectile_speed` | +25% to +100%, in 5% steps. | Bows, crossbows |
| `ammo_save_chance` | 25% to 50% chance not to consume ammo. | Bows, crossbows |
| `extra_projectile_chance` | 25% to 50% chance to fire 2 extra projectiles, angled 10° left and right. | Bows, crossbows |
| `bow_draw_stamina` | −1 to −3 draw stamina. | Bows |
| `crossbow_reload` | −25% reload time (fixed). | Crossbows |
| `shield_movespeed` | +3% to +7% movement speed. | Shields |
| `shield_block_stamina` | −50% block/parry stamina (fixed). | Shields |
| `parry_adrenaline` | +3 to +7 on a successful parry. | Non-tower shields |
| `parry_damage` | Deals blunt damage to the attacker equal to 1x to 2x your block armour, in 0.1 steps. | Non-tower shields |
| `parry_health` | +5 to +10 health on parry. | Non-tower shields |
| `parry_stamina` | +10 to +20 stamina on parry. | Non-tower shields |
| `base_damage` | +10% to +30%. | Magic weapons |
| `block_armour` | +10% to +30%. | Magic weapons |
| `movement_penalty` | As for weapons. | Magic weapons |
| `eitr_cost` | −10% to −20% eitr per attack. | Magic weapons |
| `eitr_regen` | +15% to +30% eitr regeneration while held. | Magic weapons |
| `shield_on_use` | 10% to 20% chance to gain the Staff of Protection shield for 20 seconds when used; only the user is shielded. | Magic weapons |
| `aoe_radius` | +25% to +50% area radius. | Magic weapons with an area effect |
| `magic_extra_projectile` | 10% to 25% chance of an extra projectile. | Elemental magic weapons that fire projectiles |
| `summon_roots` | 10% to 20% chance to also summon friendly roots from the Staff of the Wild. | Elemental magic weapons |
| `blood_health_cost` | −10% to −30% health cost. | Blood magic weapons |
| `blood_health_to_stamina` | 10% to 30% of the health cost is paid with stamina instead. | Blood magic weapons |
| `low_health_effect` | +25% to +50% damage and shield strength while below 50% health. | Blood magic weapons |
| `shield_ally_heal` | +20 to +40 health to nearby players and tamed creatures or summons when the shield is cast; never the user. | Staff of Protection |
| `summon_max_count` | +1 to +2 maximum summons. | Summoning weapons (such as Dead Raiser) |
| `extra_summon_chance` | 15% to 30% chance to summon one extra minion. | Summoning weapons (such as Dead Raiser) |
| `extra_summon_stars` | 25% to 50% chance to add 1–2 stars to summons, up to 3 stars. | Summoning weapons (such as Dead Raiser) |
| `armour_flat` | +2 to +6 armour. | Armour |
| `armour_percent` | +10% to +30% armour; result rounded up to a whole number. | Armour |
| `movement_penalty` | Cancels 50% to 100% of the negative movement penalty. | Armour with a negative movement penalty |
| `armour_movespeed` | +2% to +4% movement speed. | Armour |
| `fire_resistance`, `frost_resistance`, `lightning_resistance`, `poison_resistance` | +25% resistance to the respective damage type (fixed). | Armour |
| `health_regen` | +5% to +15%. | Armour |
| `stamina_regen` | +5% to +15%. | Armour |
| `max_health` | +2 to +5. | Armour |
| `adrenaline_on_hit` | +1 to +4 when damaged. | Armour |
| `dodge_stamina` | −5% to −15% dodge roll stamina. | Armour |
| `thorns` | Deals 20% to 40% of your total body armour as pierce damage to melee attackers. | Armour |
| `skill_<name>` | +10 to +20 to one skill: axes, blood_magic, bows, clubs, crossbows, elemental_magic, fists, knives, polearms, spears, or swords. | Armour |

Resistance modifiers are separate by damage type. For each type, the strongest resistance on equipped armour applies; resistance does not stack across pieces. Movement-penalty reduction cannot turn an item's penalty into a movement bonus. Stored values are never clamped to the ranges above, so values beyond them (from commands or special runes) are kept when loading.

## Console commands

Open the in-game console and use the commands below. `set_rarity`, `modifier`, `remove_modifier`, and `spawn_mod` only work when cheats are enabled (the vanilla `devcommands`).

| Command | Usage | Effect |
| --- | --- | --- |
| `set_rarity` | `set_rarity <magic\|rare\|epic\|0> [slot]` | Set rarity. Use `0` to clear rarity and all modifiers from the item. |
| `modifier` | `modifier <modifier_name> [value] [slot]` | Add the named modifier at its strongest value, or at `value` if given. `value` uses tooltip units (`25` means 25% for percentage modifiers) and may exceed the normal range. The item must already have a rarity. |
| `remove_modifier` | `remove_modifier <modifier_name> [slot]` | Remove that modifier from the item. |
| `spawn_mod` | `spawn_mod <prefab> <modifier_id> [modifier_id]` | Spawn one eligible enemy with exactly the specified distinct modifiers and one star per modifier. |
| `dump_item_data` | `dump_item_data` | Print local-player inventory item data for troubleshooting. |

Examples:

```text
set_rarity rare bow
modifier bow_draw_stamina bow
modifier fire_damage 20 weapon
modifier crossbow_reload crossbow
modifier fire_resistance chest
remove_modifier fire_resistance chest
set_rarity 0 chest
spawn_mod Greydwarf fire_infusion poison_infusion
spawn_mod Eikthyr
spawn_mod Eikthyr guarded
spawn_mod Eikthyr fire_ward
```

The optional slot targets the equipped item. Supported slots are `weapon`, `bow`, `crossbow`, `magic`, `shield`, `armour`, `helmet`, `chest`, `legs`, and `cape`. Use `armour` only when exactly one armour piece is equipped. If the slot is omitted, the command runs only when exactly one supported item is equipped; otherwise, specify a slot. Modifier compatibility is checked for the selected item, and parry modifiers are unavailable on tower shields.

For `spawn_mod`, modifier IDs include `fire_infusion`, `poison_infusion`, `frost_infusion`, `lightning_infusion`, `fire_ward`, `poison_ward`, `frost_ward`, `lightning_ward`, `blunt_ironhide`, `pierce_ironhide`, `slash_ironhide`, `fleeting`, `armored`, `shattering`, `regrowth`, `volley`, `quickshot`, `firetread`, `tarred`, `healer`, `caster`, `shielded`, `bulwark`, and `summoner`. Non-boss enemies require one or two distinct modifiers and have one star per modifier. For a boss, omit modifiers to roll one shared and one boss modifier, or specify exactly the modifiers wanted: at most one shared and one boss modifier. Boss modifier IDs are `guarded`, `binding`, `sentry`, `scaled`, `frenzy`, and `sundering`; enemy-only modifiers are rejected for bosses, and boss modifiers are rejected for non-bosses. Bosses saved with the former `sunder` ID retain Sundering. The command does not change a boss's level. Use Tab to complete prefabs and modifier IDs. The command uses vanilla's single-object spawn position and marks the creature as cheat-spawned.

## Runes

Runes are used from the rune window, opened with the **Runes** button under the inventory (the hotkey is temporarily disabled). Pick an item and a rune, then apply: a 3-second progress bar runs and can be cancelled by clicking again. The rune is consumed unless the vanilla `nocost` cheat is on. Runes have no crafting recipe; they drop from enemies (see [Rune drops](#rune-drops)), appear in some loot chests, can be purchased from Hildir, or can be spawned with the console, e.g. `spawn IzzemisMod_RuneAsgard`. Ammo (arrows and bolts) cannot receive rarity, modifiers, or runes.

| Rune | Effect |
| --- | --- |
| Asgard | Removes one random modifier and adds one new random modifier. |
| Midgard | Upgrades a normal item to Magic and adds one random modifier. |
| Vanaheim | Upgrades a Magic item to Rare and adds one random modifier. |
| Alfheim | Upgrades a Rare item to Epic and adds one random modifier. |
| Jotunheim | Adds a modifier to an Epic item, up to 4. |
| Muspelheim | Destroys a Magic or higher item and grants the matching rune (Magic: Midgard, Rare: Vanaheim, Epic: Alfheim). |
| Niflheim | Rerolls the values of all modifiers on an item. |
| Hel | Epic items only. One of four equal outcomes: destroys the item, removes a modifier, adds a bonus 5th modifier, or raises all non-fixed modifier values by 20% (may exceed normal maximums; fixed values such as resistances are unchanged). The item is then "Touched by Hel": only Nidavellir and Muspelheim can be used on it. It can still be upgraded at crafting stations, keeping its rarity and modifiers. |
| Nidavellir | Destroys an item with modifiers and grants a Rune of Valheim holding one of its modifiers at its exact value. |
| Valheim | Only obtained from Nidavellir or the three late-game bosses (stack size 1). Applies its modifier to an item of the same type as the sacrificed item that has a rarity, a free modifier slot, and does not already have that modifier. |

### Hildir's rune shop

Each purchase gives one rune. Quest offers unlock independently when the corresponding chest is returned to Hildir, including chests returned before installing this version.

| Requirement | Runes | Price each |
| --- | --- | --- |
| None | Asgard, Midgard | 500 coins |
| Smouldering Tomb chest | Vanaheim, Muspelheim | 1,000 coins |
| Howling Cavern chest | Alfheim, Niflheim | 1,500 coins |
| Sealed Tower chest | Jotunheim, Nidavellir | 2,000 coins |

Hel and Valheim are not sold.

### Chest loot

Newly generated loot chests in the Black Forest, Swamp, Mountain, Plains, Mistlands and Ashlands have a 25% chance of extra runes. Each rune is selected independently using the same biome-specific rune weights as enemy drops. Meadows, Ocean and Deep North chests have no rune loot; Valheim runes are excluded. The enemy rune drop-rate modifier does not affect chest loot.

The **Chest Rune Rate** world modifier is saved per world and defaults to 1x, including in existing worlds. It sets the number of runes in a successful chest: 1x = 1 rune, 2x = 2 runes, 3x = 3 runes, and 4x = 4 runes. The chance remains 25% at every setting. It does not affect enemy or boss drops.

Existing generated chests and player-built chests are unchanged. If a chest runs out of room for runes, its existing contents are preserved.

## Rune drops

Runes drop from a fixed list of enemies (any enemy not listed drops no runes). Enemies are grouped by biome. Each group has a base chance per rune, in percent per kill, and every enemy has a multiplier applied to its group's chances. Rune of Valheim never drops from normal enemies. The tables are in `RuneDrops.cs` (`BaseChances` and `Enemies`).

- **Unlock biomes:** Asgard and Midgard drop from the Black Forest, Vanaheim and Muspelheim from the Swamp, Alfheim and Niflheim from the Mountain, Jotunheim and Nidavellir from the Plains, and Hel from the Mistlands. Each keeps dropping in later biomes. Nothing drops in the Meadows.
- **Stars:** the chance is multiplied once per star (1 star = x3, 2 stars = x9 by default).
- **Bosses** ignore the tables and drop guaranteed runes, one per player within 200 m: Eikthyr drops Midgard, The Elder Vanaheim, Bonemass Alfheim, and Moder Jotunheim. Yagluth, The Queen, and Fader each drop a Rune of Valheim per player, rolled separately, with a random item type and a random modifier at its maximum value.

The **Rune Drop Rate** world modifier appears in the world settings screen when Runecarved is installed. It is saved per world, so existing worlds default to 1x until changed. It affects normal-enemy rune chances; boss drops remain guaranteed and are not affected.

Rune items glow in the world and have a colored loot beam with matching rising particles. They play a short ward activation sound when dropped.

Rune drop chance is multiplied by the selected rate:

| Setting | Multiplier |
| --- | --- |
| 1x | 1x |
| 3x | 3x |
| 5x | 5x |
| 10x | 10x |
| 20x | 20x |

Configuration (`BepInEx/config/com.izzemi.izzemismod.cfg`, section `Rune Drops`):

| Option | Default | Effect |
| --- | --- | --- |
| `StarMultiplier` | 3 | Chance multiplier per enemy star. 1 disables the star bonus. |
