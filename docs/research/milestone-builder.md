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
| 315-land | 315, 490, 491, 492 | Ruins of Alph (underground hall), (underground hall, Sinjoh event), (hall entrance, Sinjoh event) and (underground hall, Sinjoh event 2) |
| 332-land | 332 to 339, 341 | Bell Tower |
| 459-surf, 459-goodrod, 459-superrod | 459, 462, 463, 464 | Mt. Silver Cave (upper mountainside), (Moltres chamber), 2F and 3F |

**Land written "cave" on an overworld or forest map:** Ilex Forest (117) only. Its encounter tiles are not kind 3 in the committed map data; B1 (#517) finds them, and the name corrects itself when the data does.
