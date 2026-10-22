# Decisions: game profile

Decisions about how per-game knowledge is organised in the backend. Project-wide decisions are in [global.md](global.md). Design: [game profile spec](../specs/game-profile.md).

---

## The profile composes; it does not behave

Status: Accepted
Date: 2026-10-21

### Context
Plan rows 5.3 to 5.3e describe a game profile as "one object per flavor that is the only place game knowledge lives". Taken literally that is a god class: addresses, parsing, battle state, capabilities, maps and macros in one type with six reasons to change.

### Decision
`GameProfile` is a sealed record of references and two factory delegates, with one constructor assertion (a declared capability has its parts present) and no other code. The parts keep one job each: `IPointerService` holds addresses and sizes; `IPokemonParser` and `IBattlePokemonParser` decode byte blocks; `GameCapabilities` is a record of booleans; `IOverworldReader` reads map, position and facing; `IGameStateReader` says where the game is; `MapService`, `GraphService` and `PathfindingService` own map data per profile. Code reaches any of them only through the profile.

### Consequences
- Adding a game is one `Create` method that composes existing part types, plus whichever parts are new.
- A part can be replaced (the state reader is, in the spec's P9) by one line in the profile.
- The profile cannot grow behaviour; a method that does work belongs on a part.

---

## Pokémon parsing and garbage-memory retry stay in ApiHawk

Status: Accepted
Date: 2026-10-21

### Context
ApiHawk's validated party read (opcode 0x04) decrypts, checksums and range-checks every slot on the frame thread and retries one attempt per frame, because reads during an overlay swap return believable garbage. Moving that to the backend would trade one round trip for many and lose the per-frame retry. The pointer spike did not find a better way to detect or retry garbage.

### Decision
The validated party read, the pointer-chain opcode, the two validators and the `M.` command's coordinate decode stay in ApiHawk unchanged. The backend's new game-state reader reads only small integer blocks with plain reads, range-checks pointers itself, and takes a consistency re-read instead of asking ApiHawk for a single-frame multi-block read. Nothing in the profile work changes ApiHawk.

### Consequences
- ApiHawk's protocol does not change for this epic; the fork is not rebuilt.
- If a later spike shows a better garbage-retry strategy, that is a new decision here, not a side effect of a profile task.
- If the state reader's five reads prove too slow (spec NFR-1), the task stops and the owner decides; a one-frame multi-block opcode is one option, not a fallback a coder may take on their own.

---

## The state reader lands behind an interface, legacy implementation first

Status: Accepted
Date: 2026-10-21

### Context
Replacing the bot's three observed battle bytes with the verified pointer chain is the one change in the cleanup that alters what the bot does in the game. Doing it in the same PR that rewrites every wait loop would make a regression impossible to attribute.

### Decision
`IGameStateReader` arrives with `HgssLegacyStateReader` in the game profile work, which reproduces today's classification from today's bytes, and every event moves to the interface with no behaviour change. The chain-based `HgssFieldSystemStateReader` is a separate follow-up spec ([hgss-state-reader.md](../specs/hgss-state-reader.md)) with its own QA gate: it lands unselected with unit tests, then one PR selects it in `HgssProfile`, deletes the legacy reader and rewrites the wait loops. The interface has one implementation until that follow-up; it is also the test seam for the battle events, which is why it is accepted now.

### Consequences
- Rollback of the pointer change is a one-line revert.
- The legacy reader is deleted in the PR that proves the chain reader; it is not kept as a fallback.
- Every HGSS memory read is a row in the follow-up spec's read table and a commented member of the address table (symbol, root or offset, observed or decomp-only, proving scenario); a PR that adds a read without both does not merge.
- RS has no state reader until its chain is mapped; its profile declares no wild-encounter capability, so no event asks it for one.

---

## RS is a wrapper profile until its symbols are mapped

Status: Accepted
Date: 2026-10-21

### Context
Current hunts run on HGSS revision 0 and RS revisions 1.1 and 1.2. The RS address inventory exists only as the pointer service and a few inline constants; no RS decomp pass has been done (plan row 6.1).

### Decision
The RS profile is composed from today's `RsPointerService` and `Gen3PokemonParser`, declares only `HasSoftResetStarterHunt`, and has no overworld or state reader. The inline RS constants move into `RsPointerService`; nothing else RS-specific changes in this epic.

### Consequences
- RS keeps working exactly as today, and the dashboard stops offering it HGSS-only actions.
- Row 6.1 (pokeruby symbols) is the prerequisite for any RS state reader or revision table, and is scheduled by the owner.

---

## A multi-block chain read opcode for the state reader

Status: Accepted
Date: 2026-10-22

### Context
The decision above kept the state reader on plain reads with a consistency re-read, and made a one-frame multi-block opcode the owner's option if NFR-1 (a snapshot under 50 ms) was missed. S1's measurement (PR #142) missed it structurally: `MemoryServer` drains its request queue once per frame, so each dependent read costs one emulator frame, and a five-read battle-menu snapshot measured 79 ms median with the request count already at the spec's minimum.

### Decision
ApiHawk gains opcode `0x05`: one request names any number of pointer chains (up to the limits in the [spec](../specs/hgss-state-reader.md#the-single-frame-read)) and gets every block from the same frame, with a per-block status instead of a 502 for a null or out-of-range pointer. It takes a retry count, defaulting to 0, that retries the whole request on later frames when a pointer was out of range, so a future caller that meets garbage can ask for a frame-by-frame retry without a protocol change. The existing opcodes, the validated party read and the pointer-chain read are unchanged. The state reader issues one such request per snapshot and caches nothing.

### Consequences
- The "no ApiHawk change" non-goal of the state reader spec is narrowed to this one opcode; `BotServer.dll` is redeployed to the QA install by the orchestrator and to the hunting install by the owner.
- A snapshot is consistent by construction; the reader's root cache, strike counter and re-read go away.
- The 50 ms budget becomes one frame plus the HTTP round trip.
