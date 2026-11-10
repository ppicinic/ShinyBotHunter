# Research: a livelier dashboard (animated sprites, themes)

Date: 2026-11-10. Spike branch: `spike/dashboard-alive-prototype` (cut from `main` at c281baa; the prototype, the scripts and the mocks; not for merge). Spec: [dashboard-alive](../specs/dashboard-alive.md).

## What was proved

1. **No single source animates all nine generations; two together do.** Both are folders of the PokeAPI sprites repository the bot already downloads from, so the names are the ids the fetcher already has.
2. **Female sprites survive through generation 5, and beyond where one exists.** Every species with a gender difference in generations 1 to 5 has its own animated female sprite, normal and shiny.
3. **A still sprite can be given a DS-style movement in CSS alone**, so the few Pokemon with no animation still move.
4. **Themes need no change to the components.** The colours the components already use (`slate-*`, `indigo-*`, `white`, the three `card-*`) are remapped to CSS variables in the Tailwind config; a theme is one block of variables. Midnight (today's navy) renders from the same values as before.
5. **A theme change reaches every window with no IPC.** All dashboard windows share one `localStorage`; the `storage` event fires in the other windows. Seen on the built app: changing the selector in the Admin window recoloured the Recent window.

## The sprite sources

Counted on 2026-11-10 from the repository tree (`PokeAPI/sprites`, `master`), against the 1,395 form names in `D:/Sprites/Default` and the 147 in `Female_Default`.

| Source (folder under `sprites/pokemon/`) | Look | Files | National dex 1 to 649 | 650 to 1025 | Female folder |
|---|---|---|---|---|---|
| `versions/generation-v/black-white/animated/` | Black/White pixel sprites, moving. About 40 to 100 px, 2 to 20 KB each | 1,173 front, 1,168 shiny | all 649 | 219 of 376 (fan-made, patchy) | 106 (96 in gen 1 to 5), 98 shiny |
| `other/showdown/` | Pokemon Showdown's animated model renders. About 40 to 200 px, 20 to 200 KB each | 1,476 front, 1,476 shiny | all | 362 of 376 | 102 (96 in gen 1 to 5), 102 shiny |

Missing from Showdown's set: 990 to 995 (Iron Treads to Iron Thorns), 1006, 1008, 1010, 1017 (Ogerpon), 1022 to 1025. Showdown's own site was checked for them and has none either (`ani/ironboulder.gif`, `ani/ogerpon.gif`, `ani/pecharunt.gif`: 404).

The tier the spike used: **generations 1 to 5 take Black/White first, then Showdown; generations 6 to 9 take Showdown first, then Black/White.** Mixing the two inside generations 6 to 9 by "whichever exists" would put pixel sprites next to model renders at random; the generation split keeps each game's Pokemon in one look.

Result over the local folders, after the download:

| Folder | Local stills | Animated found | From Black/White | From Showdown | Left still |
|---|---|---|---|---|---|
| Default | 1,395 | 1,307 | 918 (both folders: 1,836) | 531 (both: 1,062) | 88 |
| Shiny | 1,395 | 1,307 | | | 88 |
| Female_Default | 147 | 142 | | | 5 |
| Female_Shiny | 147 | 142 | | | 5 |

2,898 GIFs, 208 MB, no failed download.

The 88 left still: 63 Alcremie cream-and-sweet combinations, and 25 others (`arceus-unknown`, the six missing Iron Pokemon, Ogerpon and its masks, Pecharunt, `koraidon-apex-build`, `miraidon-ultimate-mode`, `gimmighoul-chest`, a few default-form names such as `flabebe-red` that the spike's name matching missed). The real fetcher has each form's sprite URLs from the API and will not miss the last kind.

### Females

- Generations 1 to 5: all 96 species with a gender difference have a female GIF in both sources, normal and shiny. None is lost.
- The other names in `Female_Default` are female-only species (Chansey, Nidoqueen, Miltank, Froslass and so on), whose ordinary sprite is the female one. The spike copied the ordinary animation for those.
- Left still: Eevee's female (a generation 8 tail difference, no animation anywhere) and Ogerpon's four. Copying the male animation for a species that has a gender difference would show the wrong sprite, so those stay on the still female picture. The fetcher tells the two cases apart by the species' `gender_rate` (8 is female-only).

## Pixel art only (after the owner's review)

The owner does not want Showdown's model renders, so the question became how far pixel animation alone reaches. Two pixel sources were counted: the PokeAPI Black/White animated folder above, and Showdown's own `gen5ani` folder (its directory listing, matched through Showdown's `pokedex.json`). Showdown's is a subset: it adds nothing the PokeAPI folder lacks.

Forms in `D:/Sprites/Default`, by the generation of the species:

| Generation | Forms | Pixel animation | None |
|---|---|---|---|
| 1 | 202 | 185 | 17: Hisui, Galar and Paldea forms, the cap and cosplay Pikachu |
| 2 | 134 | 131 | 3: Hisui forms |
| 3 | 145 | 145 | 0 |
| 4 | 146 | 144 | 2: `arceus-unknown`, `palkia-origin` |
| 5 | 188 | 182 | 6: Hisui and Galar forms |
| 6 | 137 | 82 | 55 |
| 7 | 127 | 110 | 17 |
| 8 | 177 | 74 | 103 (63 are Alcremie variants) |
| 9 | 139 | 29 | 110 |

By species (no forms): every species of generations 1 to 5 has a pixel animation; 157 of generations 6 to 9 have none (18, 14, 30 and 95). No source found has them: they would have to be drawn.

What this means: every Pokemon obtainable in HGSS and RS, the games the bot hunts, animates, with its female sprite. Past generation 5 it is about half, and the rest move by CSS on their still pixel sprite. The stills are complete (the local folders have all 1,395), so nothing is ever blank.

The Showdown GIFs the spike downloaded for generations 6 to 9 are still in `D:/Sprites/Animated`; the build's fetcher will not fetch them, and the folder should be cleared and refilled when it lands.

## Closing the gaps (second review)

The owner accepted Showdown's sprites for generations 6 to 9 on condition that coverage is complete. Starting from the first tier's 88 forms left still, `fill.py` did two things:

- 26 files (13 forms, normal and shiny) copied from the base form, for forms that are the default form under its own name or look the same (`flabebe-red`, `floette-red`, `florges-red`, `furfrou-natural`, `vivillon-meadow`, `gimmighoul-chest`, `sinistea-phony`, `poltchageist-counterfeit`, `sinistcha-unremarkable`, `koraidon-apex-build`, `miraidon-ultimate-mode`, `arceus-unknown`, `maushold-family-of-three`). The real fetcher gets these from the form's own Pokemon, not from a list.
- 126 files downloaded from Showdown's own `sprites/ani` and `ani-shiny` folders, which hold more than the PokeAPI mirror: the 62 Alcremie cream-and-sweet variants and `furfrou-la-reine`. The plain Alcremie covers the 63rd.

Result: 1,384 of 1,395 in `Default` and in `Shiny`, 224 MB in all. The 11 left have no animation in the PokeAPI repository (either folder) or on Showdown's site (`ani`, `gen5ani`): `iron-treads`, `iron-bundle`, `iron-jugulis`, `iron-leaves`, `iron-boulder`, `iron-crown`, `ogerpon` and its three masks, `pecharunt`. They keep the still sprite with the CSS movement. Females: 142 of 147; the five left are Eevee and the four Ogerpon.

## The last 11 (Wikidex)

A search for the 11 turned up Wikidex (the Spanish wiki), which hosts clips of the Scarlet/Violet battle models with a transparent ground, normal and shiny ("variocolor"), found through its MediaWiki API (`list=allimages`, `prop=imageinfo`):

| Form | Wikidex file (normal; the shiny adds `variocolor`) | Kind |
|---|---|---|
| `iron-treads`, `iron-bundle`, `iron-jugulis`, `iron-leaves` | `Ferrodada EP`, `Ferrosaco EP`, `Ferrocuello EP`, `Ferroverdor EP` | GIF, 190 to 485 px, 1 to 3 MB |
| `iron-boulder`, `iron-crown` | `Ferromole EP`, `Ferrotesta EP` | WebM with alpha |
| `ogerpon` and the three masks | `Ogerpon máscara turquesa / fuente / horno / cimiento EP` | WebM with alpha |
| `pecharunt` | `Pecharunt (abierto) EP`, the open-shell pose, for both (owner's choice; the closed-shell clip has no shiny) | WebM with alpha |

`convert.py` crops each to the Pokemon (ffmpeg `alphaextract,cropdetect` over every frame), scales to a height picked to sit with Showdown's sprites (60 to 110 px), 20 frames a second, and writes a transparent GIF of 86 to 783 KB. The WebM alpha needs the `libvpx-vp9` decoder (`-c:v libvpx-vp9` before the input). `sheet.png` in the run folder's `wikidex/` shows all 22.

Also there: `Eevee EpEc hembra` (the Sword/Shield female model). Not used: Eevee is generation 1 and shows as a pixel sprite, and a model render for the female alone would not match; the female stays on its still pixel sprite.

Result: 1,395 of 1,395 in `Default` and `Shiny`; 146 of 147 in the female folders (Eevee). Not found anywhere else tried: Showdown (`ani`, `gen5ani`), the PokeAPI repository, pokemondb's sprite folders, projectpokemon's.

## Theme mocks

dashboard-theme-mocks.html (`docs/research/dashboard-alive-spike/dashboard-theme-mocks.html` on the `spike/dashboard-alive-prototype` branch), one self-contained page (sprites embedded), nine tiles, each a mocked window with two encounter rows, a milestone table and the accent controls:

- Midnight, for reference;
- HeartGold SoulSilver: H1 gold and silver drift (metal header), H2 Ho-Oh and Lugia (red-gold into sea blue), H3 Johto dusk (near-black, two glows circling);
- Ruby Sapphire Emerald: R1 Hoenn trio drift (all three colours behind dark glass), R2 Emerald (greens, gold accent), R3 land and sea (magma glow and sea glow trading places);
- Christmas: C1 pine and candy cane, C2 Christmas night with string lights; both with three-layer snow that shows no grid.

Second round, dashboard-theme-mocks-2.html (`docs/research/dashboard-alive-spike/dashboard-theme-mocks-2.html` on the `spike/dashboard-alive-prototype` branch), after the owner liked H1, H3, C1 and C2 and asked for Ruby and Sapphire without emerald: S1 ruby and sapphire drift (H1's treatment, cut-gem header), S2 Hoenn dusk (H3's treatment, two glows), S3 Groudon and Kyogre (magma into abyss blue), S4 Ruby alone, S5 Sapphire alone.

The gradients are wider than the window and move by `background-position` over 30 to 45 s.

## The backend's dashboard config

`AdminService` holds the subscriptions and the recent count in memory and pushes them over SignalR; nothing saves them, so a backend restart starts empty with a count of 10. A theme kept there needs a save to last, which the spec adds.

## Movement

The DS games move a two-frame sprite with a canned motion per species. The spike does the same in CSS with stepped timing (`steps()`), so it reads as sprite movement rather than a smooth web tween:

- **Entry**, on every sprite when it appears: drops in, squashes on landing, settles (0.55 s, six steps). A new encounter row therefore lands.
- **Idle**, on still sprites only: one of four loops (hop, squash, sway, bob), picked by a hash of the form name so a Pokemon always moves the same way. Animated GIFs get no idle loop; they already move.
- **Shiny**: two stars twinkle in turn over the sprite.
- `prefers-reduced-motion` turns all of it off.

Sprites stand on the bottom edge of their box at their own size and only shrink to fit. The Black/White GIFs are cropped to the Pokemon (Pikachu is 50 x 46), where the stills are 96 x 96 with padding; shown at their own size they come out the size the stills did. Stretching them to the box, as the stills are today, would make Joltik as large as Wailord.

## Themes

Every colour in the renderer goes through one of three routes, counted on `main`:

- `assets/index.css`: 17 distinct hex values;
- Tailwind's `slate-*` (about 190 uses), `indigo-*` (about 45), `white` (59) across 16 component files;
- the three `card-*` colours in `tailwind.config.js`.

Rewriting the class names to semantic ones touches all 16 files. Remapping the palettes in the Tailwind config (`slate` to a neutral ramp of variables, `indigo` to an accent ramp, `white` to an ink variable) touches none, and keeps opacity modifiers such as `bg-card-border/30` working because the variables hold RGB triplets.

Status colours (`red-*`, `green-*`, `amber-*`, the shiny gold) are not themed: they carry meaning and are tuned for a dark ground. That is why all four spike themes are dark. A light theme would need each of those checked.

The spike's four themes: Midnight (today's), HeartGold SoulSilver (lacquer brown, gold accent, silver text, gold-to-silver header), Ruby Sapphire Emerald (emerald ground, ruby accent, ruby-to-sapphire header), Christmas (pine ground, candy-stripe red header, gold second accent, falling snow as a fixed overlay that takes no clicks).

Rough edges seen in the screenshots, for the build to fix: white button text on the HGSS gold accent is low in contrast; the snow repeats on a visible 200 px grid.

## What the spike changed (prototype, not for merge as is)

`PokemonDashboard` only: `tailwind.config.js`, `assets/index.css`, `utils/sprites.ts`, `utils/theme.ts` (new), `components/PokemonSprite.tsx` (new), `RecentPokemonCard.tsx`, `PhaseCard.tsx`, `admin/DashboardConfigTab.tsx`, `main.tsx`. Typecheck and build pass. No package added.

On this PC: `D:/Sprites/Animated/` was created (four folders, 2,898 GIFs, 208 MB). Nothing existing was changed; deleting the folder undoes it.

## Evidence

`docs/research/dashboard-alive-spike/` on the prototype branch holds the scripts and the two mock pages. The screenshots and recordings are in `ShinyBotHunter.Qa/dashboard/qa-runs/20261110-dashboard-alive-spike/` in the spike worktree on the owner's PC (not committed):

- `cov.py`, `fetch.py`, `report.json`: the coverage count and the download, with the list left still;
- `demo2.js`: launches the built dashboard with its own user-data folder and feeds it ten sample encounters over the SignalR IPC channels (no backend, no emulator);
- `<theme>-recent.png`, `<theme>-admin.png` for the four themes;
- `demo3.js` and `milestone/`: the same with a milestone window (ten rows: pinned male and female Pikachu, a female Meganium and Unfezant, a Showdown sprite, a still one), fed by a stand-in for `/api/phaseinfo`; `<theme>-milestone.png`, `<theme>-recent.png` and three window recordings;
- two `.webm` recordings (the Recent window shows the entry movement, the GIFs, the still fallback moving, and the four themes in turn).

The sample run's sprite sources, read from the page: `Animated/Female_Default/meganium.gif`, `Animated/Default/wailord.gif`, `Animated/Default/sprigatito.gif` (Showdown), `Shiny/ogerpon.png` and `Default/iron-boulder.png` (still fallback, with the sway and hop loops).
