# Spike: milestones built from the game's data

Run 2026-11-24 on `main` at `57c2eef` (v0.1.3). Companion to the [time mover spike](time-mover.md); the two feed one spec, [hunt plans and the milestone builder](../specs/hunt-plans-and-milestone-builder.md). Nothing here is implemented.

Sources: the `pret/pokeheartgold` decomp (fetched for the spike; the old checkout at `C:\Code\pokeheartgold` is gone), the HeartGold ROM, the committed map data under `ShinyBotHunter/Data/HGSS`, and HeartGold rev 0 on the emulator for the three things marked "run".

## What the questions were, and the answers

| Question | Answer |
|---|---|
| Can every milestone be generated, per version, with every modality? | Yes. 139 maps have a wild table; they give 466 milestones per version (grass 111, surf 72, each rod 71, headbutt 60, rock smash 10). |
| Do HeartGold and SoulSilver differ? | Yes, slot by slot, and the data carries both. Route 30 morning: HeartGold Caterpie 50, Pidgey 40, Metapod 10; SoulSilver Weedle 50, Ledyba 30, Kakuna 10, Pidgey 10. |
| Can a hunting tile be picked from the map data? | Yes for grass and surf: all 110 grass and 66 surf milestones with map data got a straight run of three tiles or more. One was run on the emulator. |
| Can the expected share per species be computed, including Repel and the lead's ability? | Yes. The whole rule is one source file, and the two values it needs beyond what the bot reads today were found in memory. |
| Do the existing milestone names map onto the generated ones? | Yes, 15 of production's 20; the other five are not wild locations (starters, Togepi, Jirachi, training). Table below, for review. |

## The data

All from the decomp, and each is also in the ROM:

| Data | Decomp file | What it gives |
|---|---|---|
| Wild tables | `files/fielddata/encountdata/gs_enc_data.json`, 142 entries (the ROM's `a/0/3/7`, 196 bytes each, read directly in the time mover spike) | Per map: grass (a rate, 12 slots, each with one level and a species for morning, day and night), two Hoenn Sound and two Sinnoh Sound species, surf and rock smash and three rods (slots with a level range), the night-fishing species, the swarm species. A slot's species or level is either one value or one per version. |
| Headbutt | `files/arc/headbutt.json` | Per map: common, rare and secret tables (6 slots each, level range) and the tree tiles. Species can differ per version (`gold`, `silver`). |
| Map to table | `src/data/map_headers.h` (`wildEncounterBank`, `mapsec`, `mapType`, `matrixId`) | Which of the 540 maps uses which table, and its location. |
| Location names | `files/msgdata/msg/msg_0279.json`, indexed by `mapsec` | "Route 30", "Dark Cave", "SLOWPOKE Well". |
| Which map owns a tile | `files/fielddata/mapmatrix/map_matrix_NNNN.bin` | Outdoor maps share one matrix of 32 x 32 tile cells; cell (17, 9) to (17, 11) is Route 30. The bot's tile grid uses the same coordinates. |

91 locations; 19 have more than one map (cave floors, the two halves of a route). Their names need a part the game does not supply ("Dark Cave" is two maps with different tables: Geodude 30, Zubat 30, Graveler 20, Wobbuffet 15, Golbat 5 on one side; Geodude 60, Zubat 39, Dunsparce 1 on the other). Four locations have floors that share a table and can be one milestone: Bell Tower (9 maps, 1 table), Ice Path (4, 1), Sprout Tower (2, 1), Ruins of Alph (5, 2).

## How the game picks a wild Pokémon

From `src/field/encounter_check.c`. This is the rule the expected rates use.

**Slot weights** (percent, in slot order):

| Modality | Weights |
|---|---|
| Grass, cave floor | 20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1 |
| Surf | 60, 30, 5, 4, 1 |
| Any rod | 40, 30, 15, 10, 5 |
| Rock smash | 80, 20 |
| Headbutt | 50, 15, 15, 10, 5, 5 |

**Which species list:** morning, day (which includes evening) or night, from the live clock (`GF_RTC_GetTimeOfDay`, the address in the time mover spike). With Hoenn Sound or Sinnoh Sound on the radio, slots 2 and 3 become the first sound species and slots 4 and 5 the second: 20 % each. At night, the Good Rod's slot 3 and the Super Rod's slot 1 become the night-fishing species. A swarm replaces grass slots 0 and 1.

**The level:** a grass slot has one level. Water and headbutt slots roll within their range.

**After the slot is chosen, the encounter can be cancelled:**

- **Repel:** cancelled when the Repel counter is not zero and the level of the first party member that has not fainted is greater than the wild level. Equal levels are not cancelled.
- **Keen Eye or Intimidate on the lead:** when the lead is above level 5 and the wild level is at most the lead's level minus 5, cancelled half the time.

A cancelled encounter is no encounter, not a reroll, so the share of each surviving species rises and the encounters per hour fall.

**Other lead effects in the same file:** Static and Magnet Pull (half the time, a random slot of an Electric or Steel species, when the table has some but not only those); Hustle, Vital Spirit and Pressure (half the time, the highest-level slot of the same species; for a range, the top of it); Illuminate, Arena Trap and No Guard double the rate; Stench, White Smoke and Quick Feet halve it; Sticky Hold and Suction Cups double the fishing rate; Cleanse Tag or Pure Incense held cuts it to two thirds; the Black and White Flute; March and Lullaby on the radio. An egg in the lead slot turns every ability effect off.

**Unown:** the letter is chosen from the letters the solved puzzles unlock, and an Unown encounter is cancelled when no puzzle is solved.

Worked examples (SoulSilver, the hunts production runs today):

| Hunt | Plain | With Repel |
|---|---|---|
| Route 32 at night | Wooper 35, Ekans 30, Bellsprout 20, Mareep 10, Zubat 5 | Lead at level 6: Wooper 53.8, Bellsprout 30.8, Mareep 15.4; 65 % of encounters survive |
| Route 32 by day | Bellsprout 30, Ekans 30, Mareep 20, Hoppip 10, Rattata 10 | Lead at level 6: Bellsprout 46.2, Mareep 30.8, Hoppip 15.4, Rattata 7.7; 65 % survive |
| Route 30 in the morning | Weedle 50, Ledyba 30, Kakuna 10, Pidgey 10 | Lead at level 4: Weedle 50, Kakuna 25, Pidgey 25; Ledyba is gone, 40 % survive |

The third row is the point of showing rates: a Repel that looks helpful removes the target.

### Repel, corrected (2026-11-24, read while building the rate computation)

Two things above are wrong about Repel, read from `pokeheartgold` at `8946577`:

- **Repel applies to land and surf only.** The Repel flag is set only in `FieldSystem_PerformLandOrSurfEncounterCheck` (`src/field/encounter_check.c:239-242`). The fishing, rock smash and headbutt checks call `EncounterGenState_Init`, which clears it (`:1384`), and never set it. On those modalities a Repel cancels nothing.
- **The Repel level skips eggs as well as fainted members.** It is the level of the first party member that is neither fainted nor an egg: `GetFirstAliveMonInParty_CrashIfNone` checks `MonNotFaintedOrEgg` (`src/script_pokemon_util.c:109-123`). It is not necessarily the lead.

The worked examples are land, so they stand.

## What the bot has to read

| Value | Where | Status |
|---|---|---|
| Time of day, weekday | `0x021D1058` (time mover spike) | Run |
| Repel steps left | `RoamerSaveData.repelSteps`, save data + `0x6919` (the roamer section at save data + `0x68B4`, field at + `0x65`) | Run: written to 200, read 193 after seven steps and 186 after seven more |
| Any save section's address | The section table at save data + `0x23014`: 16 bytes per section (id, size, offset, checksum, slot); a section's data is at save data + `0x10` + offset. Section 21 is the roamers, 2 the party, 0 the clock. | Run for sections 0 and 21 |
| Lead's ability, level, egg flag, held item | The party record the bot already decrypts | Existing |
| Flute played | Same struct as the Repel counter, + `0x67` | From the struct, not run |
| Radio sound playing | `SndRadio_GetSeqNo()`, a static | Not found |
| Unown puzzles solved | Four save flags | Not found |

**Repel on the emulator.** With the counter written to 250 and a level 9 lead in Route 32's grass (wild levels 4 and 6), about 250 running steps gave no encounter. At zero the game put "Repel's effect wore off..." on screen and the player could not move until A. The hunt as built would park there.

## Anchors

For each grass or surf milestone: take the tiles of the right kind inside the cells the map owns (grass is tile 3, water tile 4, and for a map with a grass table but no grass tiles, the floor), find the longest straight run, prefer left and right, then up and down, and fall back to one tile for a spin. The Run encounter paces an equal distance either side of its start, so the anchor is the middle of the run and the range is half its length.

| Result | Count |
|---|---|
| Grass, a run on grass tiles | 50 |
| Grass table on a floor (caves, towers), a run | 60 |
| Surf, a run | 66 |
| Surf table with no water in the map data | 5 |
| No map data for the map | 1 map |

Run on the emulator: Route 30's generated run is the column x = 546, y = 298 to 310, so the anchor is (546, 304) with a range of 6. The navigator walked there from Route 32 and the Run encounter paced `M.Up.298; M.Down.310` for 29 encounters without leaving the line: Caterpie 14, Pidgey 13, Metapod 2 against the day table's 35, 50 and 15.

What the generated anchors do not yet account for, and must before they are trusted:

- **Reachability.** The run is chosen from tiles alone. A cave "run" of 64 tiles along row 62 of Dark Cave is likely not somewhere the player can stand; each anchor needs the pathfinder to confirm a route to it.
- **Trainers and signs.** A corridor in a trainer's line of sight or under a sign was not excluded.
- **Distance to a Pokémon Center.** Not scored; the pathfinder's `FindClosestPokemonCenter` gives it.
- **Surf.** The player has to be surfing already, or the anchor is a shore tile and a facing.
- **Headbutt.** An anchor is a tile and a facing next to a tree, and the tree's table depends on the trainer ID's last digit ([headbutt research](headbutt.md)), so it is per game file.
- **Fishing and rock smash.** Not attempted: a shore tile and a facing; a rock.

### Generated land anchors (2026-11-24)

`--generatemilestoneshgss` (#517) now writes an `Anchor` (game map, tile, x range, y range) on each land milestone, the same in both versions. Surf, fishing, rock smash and headbutt anchors are not generated.

- **The tiles.** Grass (kind 3) for a milestone with grass, floor (kind 0) otherwise, on the milestone's maps and on the overworld only in the cells the map owns. Taken out: every trainer's sight (an object of type other than 0 sees `param0` tiles, along its facing for movement 0 and 14 to 17, otherwise in all four directions from every tile of its `xRange`/`yRange` box; beaten and hidden trainers count and walls do not block sight), and the tile below every sign (bg type 1), where an Up press opens it. Both are read from the decomp's `files/fielddata/eventdata/zone_event/NNN_<internal name>.json`, since the map data keeps neither.
- **The rule.** The maximal runs are grouped by length, longest first, left and right before up and down at equal length. The first group of 3 or more with a run whose middle tile reaches a Pokémon Center (`FindClosestPokemonCenter`) and is reached back from it (`FindNodePath`) wins; inside it the cheapest to the Pokémon Center, then the first in row-major order. The anchor is the tile `s + (L-1)/2` with range `(L-1)/2`, so an even run leaves one end tile unpaced. Under 3, a spin on the cheapest reached tile. No HM first; with every HM only when nothing is reached without one.
- **Route 30** is (547, 304), up-down, range 6: columns 546 and 547 are both clear from y 298 to 310, and 547 is cheaper to the Pokémon Center (166 against 171). The spike's (546, 304) above was the first column its loop visited. **Route 32** is (458, 339), up-down, range 7.
- **Ilex Forest.** Its encounter tiles are behaviour 8, cave floor, on 934 of its 958 reachable floor tiles (tallied at B1's plan against the ROM permissions `--generatemapdatahgss` extracts); the other 24 are behaviour 0 and give nothing. The map data writes both as kind 0, so Ilex is chosen from the floor as a cave is, and its name "Ilex Forest · cave" stands. Viridian Forest's encounters are on grass (kind 3), and its anchor, (27, 58) left-right range 7, is on grass.

**Counts** (90 land milestones): run left-right 47, run up-down 24, spin 0, none 19.

**No anchor**, all because the committed `graph.bin` does not connect the map to a Pokémon Center in both directions, with every HM:

| Milestone | What the graph does |
|---|---|
| 155-land Sprout Tower, 332-land Bell Tower | From the floor it reaches only the tower's own floors, never the town. |
| 177-land Slowpoke Well B1F, 181-land B2F | B1F reaches Azalea's overworld but no Pokémon Center; B2F reaches only B1F. |
| 238-land Ice Path B2F and B3F | Reaches only B1F. |
| 459, 460, 462, 463, 464-land Mt. Silver Cave | Reach only other Mt. Silver Cave maps. |
| 450-land Cerulean Cave 2F | No floor tile walks to any of the map's six graph nodes. |
| 217-land Burned Tower B1F | Reaches a Pokémon Center, but no route back: from Burned Tower 1F's node at (15, 26) the graph walks nowhere but out through its warp. |
| 252-land Mt. Mortar B1F | The same, at Mt. Mortar (1F back) (57, 81). |
| 451-land Cerulean Cave B1F | The same, at Cerulean Cave 1F (49, 42). |
| 452-land Rock Tunnel B1F | The same, at Rock Tunnel 1F (46, 10). |
| 454, 455, 456-land Seafoam Islands B2F, B3F, B4F | The same, one floor up each: B1F (45, 21), B2F (9, 22), B3F (4, 24). |
| 486-land Whirl Islands B3F | The same, at Whirl Islands B1F (29, 53). |

**The file:** 1,203,576 bytes of JSON, 72,720 gzipped (from 70,208). Generation took 1 min 45 s on the owner's machine. Apart from the anchors, the milestones and slots are unchanged.

### Generated headbutt stands (2026-11-25)

`--generatemilestoneshgss` (#539) now writes, on each tree of a headbutt milestone, a stand: an `Anchor` (game map, tile, ranges 0), the `Facing` toward the tree and the stand's route `Cost` to a Pokémon Center. The same in both versions. The milestone's own `Anchor` stays null; which tree a file hunts depends on its trainer ID, so it is worked out per file when asked (`HeadbuttTrees.AnchorFor`) and never stored. Nothing calls it at Start yet: that is the follow-up B1h-b.

- **The stand rule.** For each tile T of the tree and each direction d, the tile one step back from T against d, facing d. Kept only when T is `MAP_Blocked` in the map data (the hunt's turn press toward it then only turns), the stand is no tree's tile, it is on the milestone's map (on the overworld, in a cell the map owns, since the game looks the tree up in the table of the map the player stands in), it is a free or grass tile (`MAP_Free` or `MAP_Grass`: not water, since headbutt while surfing is not run, and not a Cut or Rock Smash tile), and it is out of every trainer's sight and off the tile below every sign, as for land. The cheapest stand that reaches a Pokémon Center and is reached back from it with no HM wins, the first in tile then Up, Down, Left, Right order at equal cost. As for RS land anchors (B1r), a stand behind an HM obstacle is not one; there is no fallback (B1h-c, #572). FR-35's reach check walks with no HM too.
- **The tree kind.** `Headbutt_GetTreeType_Regular` (`src/field/headbutt.c`): `trainerId % 10` picks the row of `sRareTreeLUT_1` to `_5Plus` by the number of regular trees, the tree's index (mod 5 for five or more) the column; secret trees are always secret. The trainer ID is the full 32-bit `profile->id`, visible ID in the low half: read from the game's source, not observed on the emulator. The bot reads only the visible half today.
- **The file's tree.** The kinds whose slots hold a species still a target on the file (every kind when none is), then the tree of such a kind with the cheapest stand, the first in list order at equal cost.
- **Azalea Town** (`74-headbutt`): tree 0's stand is (426, 464) facing Down, the spike's own tile. On the owner's `HG-Headbutt` save (15613 / 35374, so 2318286077, digit 7) Heracross is on the common trees 0, 3, 5, 8, 10 and 13, and the cheapest is tree 3 from (407, 456) facing Up, cost 71. The visible half alone (digit 3) would pick tree 14 from (412, 476) facing Down, cost 81.

**Counts:** 686 of the 872 trees have a stand; 55 of the 60 headbutt milestones have at least one. The 186 without:
- Route 13's trees 4, 5 and 6, whose tiles the map data marks free, not blocked.
- 26 blocked trees with no stand that passed even the with-every-HM rule (Route 4 6, National Park 5 secret, Cherrygrove City 4 secret, Mt. Silver Cave lower mountainside 3, Route 32 2, Routes 15, 25, 39 and 45 1 each, Route 38 1 and 1 secret).
- 157 trees whose stand B1h (#539) chose with every HM, dropped by the no-HM rule (B1h-c, #572): 26 on water, one on a Cut tile (Route 13 tree 10), one on a Rock Smash tile (Route 32 tree 22, the cost-92 stand), and 129 on free or grass tiles with no route to a Pokémon Center and back without an HM (115 reach no Pokémon Center, 14 reach one but are not reached back). By milestone: Route 1 20, Pallet Town 17, Route 27 13, Route 2 (east) 11, Violet City 9, Lake of Rage 9, Route 42 6, Celadon City 6, Route 2 5, Route 12 5, Route 30 5, Route 46 5, Pewter City 5, Route 47 5 (4 of them secret), Route 34 4, Route 35 4, Route 32 4, Mt. Silver 4, Route 28 3, Route 43 3, Route 44 3, Route 21 3, Route 45 2, Route 13 2, Route 22, Route 26, Route 31 and Route 16 (east) 1 each.

Five headbutt milestones have no stand and are hunted where the bot stands: `9-headbutt` Route 1, `47-headbutt` Route 45, `49-headbutt` Pallet Town, `93-headbutt` Route 21 and `460-headbutt` Mt. Silver Cave. On 30 further (milestone, last digit, kind) combinations a kind that had a stand has none now (Route 42, Mt. Silver, Route 47's secret trees, Route 2 (east)); `HeadbuttTrees.AnchorFor` gives null there. Route 1 and Pallet Town have no round trip to a Pokémon Center without an HM in the committed graph, though Viridian City is on foot in the game; the graph is not changed in this feature.

**The file:** 1,334,134 bytes of JSON, 86,229 gzipped (B1h-c, #572; B1h's was 1,348,604 and 88,788). Generation took 1 min 53 s. Apart from the trees' `Anchor`, `Facing` and `Cost`, every milestone, slot, tree tile and land anchor is unchanged (398 per version, the same ids).

## The existing names

Production's 20 phases, read (not written) on 2026-11-24. The suffix is the game file: none for `Pokemon SoulSilver` (living dex), `b` for `Pokemon SoulSilver2` (females), `c` for `Pokemon HeartGold` (version exclusives). The number is the owner's order.

**For review.** "Proposed name" is location, then the part of it where a location has more than one table, then the modality.

| Existing | Game file | Proposed milestone | Note |
|---|---|---|---|
| 1-2 Route 29 | SoulSilver | Route 29 · grass | |
| 1-2c Route 29 | HeartGold | Route 29 · grass | |
| 1-3 Route 46 | SoulSilver | Route 46 · grass | |
| 1-3b Route 46 | SoulSilver2 | Route 46 · grass | |
| 1-4 Route 30 | SoulSilver | Route 30 · grass | |
| 1-4b Route 30 | SoulSilver2 | Route 30 · grass | |
| 1-4c Route 30 | HeartGold | Route 30 · grass | |
| 1-6 Dark Cave | SoulSilver | Dark Cave (Route 31 side) · cave | The map with Dunsparce, map 176; the decomp names it `MAP_DARK_CAVE_ROUTE_31_SIDE`. |
| 1-6b Dark Cave | SoulSilver2 | Dark Cave (Route 31 side) · cave | |
| 1-8 Route 32 | SoulSilver | Route 32 · grass | |
| 1-8b Route 32 | SoulSilver2 | Route 32 · grass | |
| 1-9 Sprout Tower | SoulSilver | Sprout Tower · floor | Both floors share one table. |
| 1-10 Ruins of Alph | SoulSilver | Ruins of Alph (underground hall) · cave | Map 315, the Unown hall; the outside is a separate milestone (Natu, Smeargle). |
| 2-2c Slowpoke Well | HeartGold | Slowpoke Well B1F · cave | Two floors, two tables (B2F adds Golbat); which one is hunted is not in the data. |
| 2-3c Azalea Town Headbutt | HeartGold | Azalea Town · headbutt | |
| 0c Training | HeartGold | none | No targets; stays as it is. |
| 1-1 Starters (HGSS), 1-1 Starters (RS) | | none | Static. |
| 2-1c Togepi | HeartGold | none | Egg. |
| 1-2 WISHMKR Jirachi | Sapphire | none | Static. |

Three game files hunting Route 30 are three phases today and would be one milestone seen through three goals. The amounts in production already encode the goal: Ledyba 2 and Zubat (female) 2 are one to keep and one to evolve; Aipom 6 on the version-exclusive file.

## Ruby and Sapphire

From the `pokeruby` decomp (the checkout at `D:\Code\_spike_pokeruby\pokeruby`). The anchors at the end were run; nothing else in this section was.

**The data.** `src/data/wild_encounters.json` has 194 entries: 97 maps, each once for Ruby and once for Sapphire. That gives 283 milestones per version: land 68, surf 54, each rod 52, rock smash 5. The tables differ between the versions on 30 maps (Route 114's land table: Ruby has Seedot 30, Zangoose 19, Nuzleaf 10 where Sapphire has Lotad 30, Seviper 19, Lombre 10). All 97 maps match a map in the bot's committed RS map data by name, so each has an id the bot reads (map group x 256 + map number: Route 101 is 16, Meteor Falls 1F 1R is 6144).

**The rule**, from `src/wild_encounter.c`:

| Modality | Weights |
|---|---|
| Land | 20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1 |
| Surf, rock smash | 60, 30, 5, 4, 1 |
| Old Rod | 70, 30 |
| Good Rod | 60, 20, 20 |
| Super Rod | 40, 40, 15, 4, 1 |

- There is no time of day and no radio. One species list per modality.
- Every slot, land included, has a level range and rolls within it.
- Repel: with the counter above zero, a land, surf or rock smash encounter is cancelled when the wild level is below the level of the first party member that has HP and is not an egg. Equal levels are not cancelled, as on HGSS. Fishing does not check Repel.
- The only lead abilities in the file are Stench (half the rate) and Illuminate (double). Neither changes which species appears. There is no Keen Eye, Intimidate, Static or Magnet Pull rule; those came with Emerald.
- Feebas: on Route 119, a rod on one of six tiles chosen from the trendy phrase gives Feebas half the time. Not a table slot.
- A mass outbreak replaces the land roll half the time on its route; treated as swarms are on HGSS.

**The Repel counter** is save variable `0x4021` (`VAR_REPEL_STEP_COUNT`), `u16`, in `gSaveBlock1.vars` at `+ 0x1340`: `gSaveBlock1 + 0x1382`. Computed from the decomp's struct, not read on the emulator.

**What differs for the feature:** no conditions on slots, so no plans and no rows dimming by the clock; the share needs each slot's level range against the Repel level, since land levels are ranges; no forms in the wild tables (no Unown; a female is the same row with a gender).

**Anchors.** The owner's rule for these games is a spin on the land tile nearest a Pokémon Center. A throwaway test ran `RouteSearch` from every grass tile of a map to any Center's counter with no HM and kept the cheapest:

| Map | Grass tiles | Anchor | Cost | Center | Time |
|---|---|---|---|---|---|
| Route 101 | 86 | (4, 2) | 37 | Oldale Town | 308 ms |
| Route 102 | 134 | (4, 7) | 38 | Petalburg City | 185 ms |
| Route 103 | 90 | (12, 15) | 54 | Oldale Town | 134 ms |
| Route 104 | 124 | (2, 11) | 133 | Rustboro City | 396 ms |
| Route 110 | 204 | (14, 97) | 54 | Slateport City | 259 ms |
| Route 111 | 0 | none | | | |

Every grass tile on the five routes had a route. Route 111 has a land table but no grass-kind tile in the committed data (its encounters are on sand), so the tile rule needs more than "kind 3" there; caves were not tried.

Run on Sapphire (a copy of `rs.qa-route101`, 2026-11-24): the navigator walked from (14, 4) to the generated anchor (4, 2) in 109 frames and the Spin hunt gave 44 encounters there: Wurmple 23, Zigzagoon 16, Poochyena 5, against the table's 45, 45 and 10.

### The generated file (2026-11-24)

`--generatemilestoners` (#512) run against `pret/pokeruby` at `5784633` wrote `ShinyBotHunter/Data/RS/milestones.json.gz` (241,189 bytes of JSON, 8,943 gzipped). Maps of one region map section (`region_map_section` in each `map.json`) are one milestone per modality when their slots for it are identical in both versions, levels included, the same rule as HGSS. The four Safari Zone maps (6656 to 6659) are left out. This supersedes the 283 above, which counted every map on its own.

**Counts, the same in both versions and with the same ids:** 226 milestones: land 46, surf 46, Old Rod 43, Good Rod 43, Super Rod 44, rock smash 4.

**The land word** is from the map's `map_type`, not its tiles: `MAP_TYPE_UNDERGROUND` and `MAP_TYPE_INDOOR` are "cave", every other type "grass". So Route 111 and Route 130 (`MAP_TYPE_6`) are "grass", Mt. Pyre 1F to 6F (indoor) and New Mauville Inside (underground) are "cave", and regenerating the tile kinds does not rename anything.

**Underwater (Route 124) (50) and Underwater (Route 126) (51)** are kept as surf. Their table is `water_mons` with the surf weights, but it is met by diving onto seaweed, not by surfing. They are the only RS source of Clamperl, Chinchou and Relicanth.

**The merged milestones** and their names in `Data/RS/map-names.json`'s `Groups`:

| Id | Maps | Name |
|---|---|---|
| 6144-oldrod, 6144-goodrod | 6144 to 6147 | Meteor Falls |
| 6145-land | 6145, 6146 | Meteor Falls 1F 2R and B1F 1R |
| 6145-surf, 6145-superrod | 6145, 6146, 6147 | Meteor Falls 1F 2R, B1F 1R and B1F 2R |
| 6159-land | 6159, 6160, 6161 | Mt. Pyre 1F, 2F and 3F |
| 6162-land | 6162, 6163, 6164 | Mt. Pyre 4F, 5F and 6F |
| 6171-surf, 6171-oldrod, 6171-goodrod, 6171-superrod | 6171, 6177, 6178 | Seafloor Cavern |
| 6172-land | 6172 to 6179 | Seafloor Cavern |
| 6182-land | 6182 to 6185 | Cave of Origin 1F, B1F, B2F and B3F |
| 6190-land | 6190 to 6193 | Shoal Cave Low Tide Entrance Room, Inner Room, Stairs Room and Lower Room |
| 6190-surf, 6190-oldrod, 6190-goodrod, 6190-superrod | 6190, 6191 | Shoal Cave |
| 6202-surf, 6202-oldrod, 6202-goodrod, 6202-superrod | 6202, 6209 | Abandoned Ship |

Feebas and mass outbreaks are not table slots and are not in the file.

### Generated RS land anchors (2026-11-24)

`--generatemilestoners` (#518) now writes an `Anchor` on each land milestone it can, the same in both versions, always a spin (both ranges 0). Surf, fishing and rock smash anchors are not generated. Apart from the anchors the file is unchanged: 249,543 bytes of JSON, 9,548 gzipped. The anchors took 7.4 s.

- **The tiles.** Kind 3 on the milestone's maps, which since #518 is every land-encounter behaviour, cave floor and Route 111's desert included ([RS research, "The generated data"](rs-navigation-and-training.md#the-generated-data)). Taken out: every trainer's sight, read from each `data/maps/<Name>/map.json`'s `object_events` (`src/trainer_see.c`): a `TRAINER_TYPE_NORMAL` trainer with `MOVEMENT_TYPE_FACE_UP`, `_DOWN`, `_LEFT` or `_RIGHT` sees along that facing, every other trainer (buried ones too) in all four directions from every tile of its `movement_range_x`/`_y` box; `trainer_sight_or_berry_tree_id` tiles in a straight line, beaten trainers counted and walls not modelled. Signs are not taken out: on RS a sign needs an A press, and the spin presses none.
- **The rule.** `RouteSearch` from every tile to the navigator's goal (a Pokémon Center's counter, (7, 4) on a `_PokemonCenter_1F` map), with flags and variables all 0 and no HM, starting at the tile's elevation (15 as 0). In ascending cost, ties to the lowest map and then row-major, the first tile that the counter reaches back wins. No HM fallback: the RS navigator walks no HM route.
- **The spike's five** are reproduced: Route 101 (4, 2) 37, Route 102 (4, 7) 38, Route 103 (12, 15) 54, Route 104 (2, 11) 133, Route 110 (14, 97) 54.
- **Route 111** is (19, 38) on the desert, cost 900, to Fallarbor Town. Its route crosses three live triggers: (10, 36) and (17, 38) set the weather, and (18, 38), live while `VAR_TEMP_3` is 0 as it is on entering the map, turns the player back without the Go-Goggles (`Route111_EventScript_15013A`). The return route from the Pokémon Center crosses (17, 38) and (18, 38) again and a fourth trigger, (9, 37), which also sets the weather. Every desert tile is behind (18, 38), so the anchor is only walked to with the Go-Goggles. The gate is a `checkitem ITEM_GO_GOGGLES` behind `VAR_TEMP_3`, a temporary variable that resets on every map entry (and that the weather triggers at (9, 37) and (10, 36) set back to 0), so a route search with a real game file's flags and variables still crosses it as live.
- **Caves:** Granite Cave 1F (37, 11) 111, B1F (3, 21) 230, B2F (29, 12) 388 and Steven's room (7, 4) 260, all to Dewford Town; Rusturf Tunnel (29, 15) 59; Meteor Falls 1F 1R (27, 17) 271; Fiery Path (26, 3) 339; Victory Road 1F (15, 39) 71 and B1F (9, 3) 227.

**Counts** (46 land milestones): spin 24, none 22.

**No anchor.** A throwaway rerun of the search with every HM, and separately with every flag set, gave the reason:

| Milestone | Why |
|---|---|
| 30-land Route 115, 33-land Route 118, 38-land Route 123 | With flags all 0, every tile needs Surf to reach a Center. Route 118 is reached on foot with every flag set (a flagged object closes the way while its flag is 0); Route 123 then still has no way back. |
| 45-land Route 130 | No kind-3 tile on the map. |
| 6145-land Meteor Falls 1F 2R and B1F 1R, 6147-land B1F 2R | Surf and Waterfall. |
| 6157-land Jagged Pass | All 34 tiles reach a Center, downhill; none is reached back, even with every HM. |
| 6159-land Mt. Pyre 1F to 3F, 6162-land 4F to 6F, 6165-land Exterior, 6166-land Summit | Surf. |
| 6172-land Seafloor Cavern | Not reached even with every HM. |
| 6181-land Cave of Origin Entrance, 6182-land 1F to B3F | Surf, in the data. |
| 6189-land Victory Road B2F | Reaches a Center only with an HM, and is not reached back even with every HM. |
| 6190-land Shoal Cave Low Tide rooms | Surf. |
| 6196-land New Mauville Entrance, 6197-land Inside | Surf. |
| 6223-land Sky Pillar 1F, 6225-land 3F, 6228-land 5F, 6227-land Shoal Cave Low Tide Ice Room | Not reached even with every HM. |

## Not proved

- Ruby and Sapphire: one anchor was run. Their rates, their Repel address and their cave and desert tiles were not.
- SoulSilver was not run; its tables are from the decomp's per-version values. The ROM path of SoulSilver's table file was not read.
- The expected rates were computed, not measured: the two emulator samples (15 and 29 encounters) are too small to confirm a percentage.
- Keen Eye, Intimidate, Static, Magnet Pull and the level abilities were not run.
- The radio sounds: not run, and the playing sound was not found in memory.
- Anchors for caves, surf, fishing, headbutt and rock smash: generated or not as the section says, none run.

## Evidence

`ShinyBotHunter.Qa/dashboard/qa-runs/20261124-milestones-spike/` (gitignored): `build.py` (the milestones, per version), `templates.json`, `anchors.py`, `anchors.json`, `rates.py`, `names.py` and `rs_build.py` (the name tables in the spec's appendix and the Ruby and Sapphire milestones), `backend7.log` (the Route 30 anchor run). The decomp checkout was in the session scratchpad and is not kept.

## The generated file (2026-11-24)

`--generatemilestoneshgss` (#511) run against `pret/pokeheartgold` at `8946577` wrote `ShinyBotHunter/Data/HGSS/milestones.json.gz` (1,186,194 bytes of JSON, 70,208 gzipped). Maps of one location (`mapsec`) are one milestone per modality when their slots for it are identical in both versions, levels included; headbutt never merges. The Safari Zone entrance (357) and the Bug-Catching Contest (487) are left out. This supersedes the 466 above, which counted every map on its own.

**Counts, the same in both versions and with the same ids:** 398 milestones: land 90, surf 63, Old Rod 58, Good Rod 59, Super Rod 59, rock smash 9, headbutt 60. The Old Rod has one fewer than the other rods because all five Mt. Silver Cave maps share one Old Rod table, while their Good and Super Rod tables split 122 from the other four.

**Route 30 land** (weight per species, from the decomp's slots):

| | Morning | Day | Night |
|---|---|---|---|
| HeartGold | Caterpie 50, Pidgey 40, Metapod 10 | Pidgey 50, Caterpie 35, Metapod 15 | Rattata 40, Spinarak 30, Hoothoot 30 |
| SoulSilver | Weedle 50, Ledyba 30, Kakuna 10, Pidgey 10 | Pidgey 50, Weedle 35, Kakuna 15 | Hoothoot 60, Rattata 40 |

**The merged milestones** and their names in `Data/HGSS/map-names.json`'s `Groups`: the location alone when the group holds every map of the location that has the modality, otherwise the members' names joined.

| Id | Maps | Name |
|---|---|---|
| 99-surf, 99-oldrod, 99-goodrod, 99-superrod | 99, 153 | Union Cave 1F and B1F |
| 107-land | 107, 448 | Mt. Moon |
| 119-surf | 119, 252 | Mt. Mortar (1F entrance) and B1F |
| 119-oldrod, 119-goodrod, 119-superrod | 119, 251, 252 | Mt. Mortar |
| 120-land | 120, 237 | Ice Path 1F and B1F |
| 121-land | 121, 242, 243 | Whirl Islands 1F, B1F and B2F |
| 121-oldrod, 121-goodrod, 121-superrod | 121, 243 | Whirl Islands |
| 122-oldrod | 122, 459, 462, 463, 464 | Mt. Silver Cave |
| 123-surf, 123-oldrod, 123-goodrod, 123-superrod | 123, 176 | Dark Cave |
| 124-land | 124, 178, 179 | Victory Road |
| 145-surf, 145-oldrod, 145-goodrod, 145-superrod | 145, 450, 451 | Cerulean Cave |
| 145-rocksmash | 145, 451 | Cerulean Cave |
| 155-land | 155, 156 | Sprout Tower |
| 177-oldrod, 177-goodrod, 177-superrod | 177, 181 | Slowpoke Well |
| 238-land | 238, 239 | Ice Path B2F and B3F |
| 315-land | 315, 490, 491, 492 | Ruins of Alph (underground hall) (the owner's name, an exception to the rule) |
| 332-land | 332 to 339, 341 | Bell Tower |
| 459-surf, 459-goodrod, 459-superrod | 459, 462, 463, 464 | Mt. Silver Cave (upper mountainside), (Moltres chamber), 2F and 3F |

**Land written "cave" on an overworld or forest map:** Ilex Forest (117) only. Its encounter tiles are not kind 3 in the committed map data; B1 (#517) finds them, and the name corrects itself when the data does.

## The clock, the save section table and the Repel counter, read on the emulator (2026-11-24, B4 and B3r research round)

Run on `feature/hunt-plans-milestones` at `b66a79c`, with no backend and no database: EmuHawk was started directly with ApiHawk (`--open-ext-tool-dll=BotServer`, ports 20000 and 20001) and its own config copy (`UseRealTime: false` and an `InitialTime`), on throwaway copies of the ROM and the save, and probed with Python over ApiHawk. Everything below was observed unless it says "from the source".

| Game | ROM | Save used |
|---|---|---|
| HeartGold (USA, rev 0, `IPKE`) | a copy of `HG-Route32.nds` | a copy of the pristine `hgss.qa-route32` (Route 32 grass, map 36, (474, 294)) |
| SoulSilver (USA, rev 0, `IPGE`) | a copy of `Pokemon SoulSilver.nds` | a copy of the owner's old SoulSilver save (Route 30 grass, map 34, (556, 374); six Pokémon, no egg). There is no SoulSilver QA save on this machine. |
| Ruby (USA, rev 2, `AXVE`) | a copy of `RU-Wild.gba` | a copy of the pristine `rs.qa-route101` |
| Sapphire (USA, rev 0, `AXPE`) | a copy of `Pokemon Sapphire.gba` | the same save |

### R1, R2: the live clock (HGSS)

**`0x021D1058` on both versions, with the second copy at `0x021D1074`.** Seven `u32`s:

| Offset | Value | Encoding seen |
|---|---|---|
| + `0x00` | year | years since 2000 (26) |
| + `0x04` | month | 1 to 12 (11, 12) |
| + `0x08` | day | day of the month |
| + `0x0C` | weekday | **0 = Sunday**: 0 on Sunday 2026-11-29, 5 on Friday 2026-12-04, 6 on Saturday 2026-12-05 |
| + `0x10` | hour | **0 to 23**: 19, then 20; 23, then 0 |
| + `0x14` | minute | 0 to 59 |
| + `0x18` | second | 0 to 59 |

- HeartGold, `InitialTime` Sunday 2026-11-29 19:57: `(26, 11, 29, 0, 19, 57, 15)` during the intro, `(26, 11, 29, 0, 19, 59, 27)` still before Continue, `(26, 11, 29, 0, 20, 1, 0)` on the field. The 19:59:59 to 20:00:00 second itself was not sampled on HeartGold.
- SoulSilver, `InitialTime` Friday 2026-12-04 19:58:30: `(26, 12, 4, 5, 19, 59, 59)` then `(26, 12, 4, 5, 20, 0, 0)` on the field. In a wild battle (battle app state 9) it went on, one second per second: `20:01:07, 08, 10, 11, 12`, while the save block copy (save data + `0x20`) stayed at `20:00:56`.
- SoulSilver, `InitialTime` Saturday 2026-12-05 23:59:15, at the intro: `(26, 12, 5, 6, 23, 59, 59)` then `(26, 12, 6, 0, 0, 0, 0)`. The weekday, the day and the hour roll together at midnight.
- The clock runs before a save is loaded (intro and title, where the anchor `[0x021D4158]` reads 0), on the field and in battle. Twelve seconds of wall time were twelve emulated seconds.
- So the block the plan reads, 8 bytes at `0x021D1058 + 0x0C`, is weekday `u32` then hour `u32`, on both versions.

### R3: the save section table (HGSS)

At save data + `0x23014`, save data being `[[0x021D4158] + 0x0C]`. **Identical on HeartGold and SoulSilver** in every id, size and offset.

- **42 entries of 16 bytes** (672 bytes): ids 0 to 41, and **entry n has id n**. The `u32` before the table (save data + `0x23010`) is the save counter. The two slot records follow the table at + `0x232B4`.
- **An entry:** id `u32` at + `0x00`, size `u32` at + `0x04`, **offset `u32` at + `0x08`**, checksum `u16` at + `0x0C`, slot `u16` at + `0x0E` (0 for ids 0 to 40, 1 for id 41, the PC boxes). This is `struct SaveArrayHeader` in the decomp's `include/save.h`.
- **Section 21 (the roamers): size `0x6C`, offset `0x68A4`**, so its data is at save data + `0x10` + `0x68A4` = save data + `0x68B4`, on both versions.
- For reference: section 0 offset `0x0`, size `0x60`; section 2 (the party) offset `0x90`, size `0x5B4`; section 5 offset `0x1234`; section 41 offset `0xF700`, size `0x12300`. The full list is in `hg-run1.log` and `ss-run1.log`.
- **Across an in-game save** (HeartGold; the save counter went 19 to 20): the 672 bytes and the slot records were byte for byte the same.
- **Across a soft reset and Continue** (HeartGold): every id, size, offset and slot was the same. The checksum field changed in 12 entries (0, 1, 4, 5, 7, 10, 14, 16, 19, 21, 34, 41), so the checksum is refreshed on load, not on save. **The save data itself moved**: `0x0227C1F4` before the reset, `0x0227C244` after (the field system moved from `0x022A01DC` to `0x022A022C`). An offset cached once per launch stays good; an absolute address would not.
- **On the title screen and the intro the anchor `[0x021D4158]` is 0** (both versions, and again after the soft reset), so FS + `0x0C` cannot be read at all: the chain ends in a null pointer at its first hop. The table was filled at the first read on the field after Continue.

### R4: the Repel counter (HGSS)

**One byte, `u8`, at the roamer section + `0x65`** (save data + `0x6919`), on both versions.

- It reads 0 with no Repel, and stays 0 over steps.
- Width: with + `0x65` written to 3 and + `0x66` to 1, three steps took + `0x65` to 2, 1, 0, the message came on the third, and + `0x66` still read 1 (HeartGold; the same with 2 and 1 on SoulSilver). A two-byte counter would have read 259 and shown no message. From the source, `RoamerSaveData.repelSteps` is a `u8` with `outbreak` at + `0x64`, `unk_66` at + `0x66` and `flutePlayed` at + `0x67`.
- It goes down by one on each completed step, at the tile change. A step into something solid does not count (SoulSilver, Up from (556, 374): the tile and the counter, at 3, did not change in two tries).

### R5: "Repel's effect wore off..." (HGSS, rule 16)

HeartGold on Route 32, the counter written to a small value and the player stepped or paced in the grass. SoulSilver on Route 30 gave the same script, native and bytes.

| What | Seen |
|---|---|
| Task | `Task_RunScripts`, `0x0203FF45` |
| `env.script` | **2022** (`std_repel_wore_off` in the decomp's `std_script.h`) |
| Context | context 0 only; `ctx.mode` 2 (waiting in a native) |
| `env.printer` (env + `0x05`) | 0, so slot 0, the one the reader reads |
| While the text prints (about 0.5 s) | native `0x021EF349` (the printing native), printer slot 0 live with `pr.state` 0 and the current character moving |
| **While the box waits** | **native `0x02041001`** (`WaitABPressNative`, already in `HgssPointerService`), and **printer slot 0 is gone** (`[0x021D1F74]` reads 0). There is no arrow: no `pr.state`, no cursor. The snapshot is `FieldTask`, script 2022, native `0x02041001`, `TextPrinter` null. |
| **What one A changes** | **`ctx.mode`, the byte at context 0 + `0x01`, goes 2 to 1** about 100 ms after the press was sent (the native word at + `0x04` still reads `0x02041001`, stale), and about a frame later the task manager pointer at FS + `0x10` is 0: the field is free. So the snapshot's `Native` leaves `0x02041001` and the next state is `FieldFree`. On a slower poll one sample between the two showed the script task still set with its environment already freed, which the reader gives as `Transitioning`. Traced at this grain once on each version, with the same result. |
| After one A | Field free, no second box, every time: 25 boxes on HeartGold, 2 on SoulSilver. Free about 170 ms after the press was sent. In the 22 single-step trials it then stayed free for the 1.2 s watched with no input. |
| A held direction | The pacing macro (`RP_M.Up.292…;M.Down.294…`) kept holding its direction for 5 s with the box up: the box stayed, the player did not move, the counter stayed 0. A direction does not close it. With Up held for 20 frames and then A pressed for 3 frames while Up stayed held, the box closed as fast as with A alone, so **a held direction neither drops nor delays the A** (one trial). The player then walked on at once, and a wild battle started on that next step. |
| A battle on the step that ends the Repel | **Not seen: in 27 Repel endings (25 on HeartGold, 22 of them single-step trials in the grass, and 2 on SoulSilver) the message always came, never a battle.** From the source, not observed as a rule: `FieldSystem_ProcessStep` returns as soon as the Repel step event starts its script (`field_control.c:675`), before the wild encounter check further down the same function (`:487`), so that step makes no encounter roll. A battle can start on the first step after the box is closed (seen once). |

The press sent was `H.A.3;W.20`, which is what the backend sends for `T.A;W.20` (`CommandPreprocessor` turns a tap into a hold of `DefaultTapFrames`, 3 by default). A raw `T.A` sent straight to ApiHawk is never applied, so a probe script must send `H.A.3`.

For the plan: the wait before the press is "script 2022 and native `0x02041001`", not an arrow state, and the press is confirmed by the snapshot no longer being "script 2022 with native `0x02041001`" (the mode byte), then `FieldFree`.

### R6: an egg in the party

**Not run.** No HGSS save on this machine has an egg in the party: every `.SaveRAM` in the two BizHawk save folders and the QA saves folder was decoded offline (`scan-saves.log`), and the level 1 Togepi in several of them is hatched. `hgss.qa-togepi-egg` does not exist. From the source: `PokemonData.IsEgg` is bit 30 of the IV word (`PokemonData.cs:98`), and the Primo and Togepi hunts already rely on it.

### B3r: the Repel counter on Ruby and Sapphire

**`gSaveBlock1 + 0x1382` = `0x02026AB6`, a `u16`, on Ruby rev 2 and Sapphire rev 0** (`gSaveBlock1` is `0x02025734`; the script variables start at `0x02026A74` and this is variable `0x4021`).

- It reads 0 with no Repel and stays 0 over steps.
- Width: with the bytes `03 01` written (259 as a `u16`), three steps read 258, 257, 256 and no message came at a low byte of 0 (Ruby; Sapphire the same from 258). So both bytes are the counter.
- Written to 3 (Ruby) or 2 (Sapphire), it went down by one per step, and on the step that reached 0 "REPEL's effect wore off..." came up.
- At the message: `FieldControlsLock` (`0x030006A4`) 1, script context mode (`0x030005B9`) 2, script native (`0x030005BC`) `0x080670E1` on Ruby rev 2 and `0x080670C5` on Sapphire rev 0 once the text has printed (`0x08064CA5` and `0x08064C89` while it prints). One A (`H.A.5`) gave lock 0 and mode 0 within 100 ms and no second box. Seen once per version, so not enough to name a gate.

### Not established

- The 04:00 and 10:00 boundaries, and the HeartGold clock at the exact 20:00 second.
- Whether a battle can ever start on the step that ends the Repel: not in 27 endings, and the source says no.
- R6.
- The Repel message during a navigator walk or while surfing; a Repel that ends on a step that also triggers something else (a trainer's sight, a warp, poison).
- European or other revisions of any of the four ROMs.

### Evidence

`ShinyBotHunter.Qa/dashboard/qa-runs/20261125-b4-research/` (gitignored): `hg-run1.log`, `ss-run1.log`, `ss-run2.log`, `rs-run1.log`, `rs-run2.log` (every read above), `hg-table-*.bin` and `ss-table.bin` (the table before and after the save and the reset), `scan-saves.log`, `hg.py`, `rs.py`, `pk.py`, `mkconfig.py`, `launch.ps1` (the probe, the config copy, the launch), `shots/`.
