// ============================================================
// Static App Config (loaded from config.json via IPC)
// ============================================================

export interface AppConfig {
  backendUrl: string
  apiUrl: string
  spritesRootPath: string
  /** How often to poll the encounter rate endpoint, in milliseconds. Default: 60000 (1 min) */
  encounterRatePollingIntervalMs: number
  cards: {
    phaseCard: { enabled: boolean }
    recentPokemonCard: { enabled: boolean }
    adminCard: { enabled: boolean }
  }
}

// ============================================================
// Dynamic Dashboard Config (received via SignalR ReceiveDashboardConfig)
// Matches C# DashboardConfig in IElectronClient.cs
// ============================================================

export interface ActiveSubscription {
  game: string
  phase: string
  gameName?: string | null
}

export interface DashboardConfig {
  // C# GamePhase[] — each has game (GameFlavor enum) and phase (string)
  gamePhases: ActiveSubscription[]
  recentEncounterCount: number
}

// ============================================================
// Encounter data (received via SignalR RecieveEncounters)
// Matches C# GameEncounterResponse in PokemonPersistenceService.cs
// ============================================================

export interface PokemonCounts {
  encounters: number
  shinyEncounters: number
  shiniesCaught: number
}

export interface PhaseEncounterResponse {
  allPokemonCounts: PokemonCounts
  // C# Dictionary<string, PokemonCounts> — keyed by form code, so each form counts separately
  pokemonEncounters: Record<string, PokemonCounts>
  // Number of pokemon encountered since the last shiny seen for this milestone
  phaseCount: number
}

export interface CaughtPokemon {
  pokemon: PokemonData
  game: string
  id: string
  phase: string
  gameFileName: string
}

// C# GameEncounterResponseBase — base encounter data (game-level or game-file-level)
export interface GameEncounterResponseBase {
  allPokemonCounts: PokemonCounts
  // C# Dictionary<string, PokemonCounts> — keyed by form code, so each form counts separately
  pokemonEncounters: Record<string, PokemonCounts>
  // C# Dictionary<string, PhaseEncounterResponse> — JSON keys are phase name strings
  phaseEncounters: Record<string, PhaseEncounterResponse>
  // C# CaughtPokemon[] — caught shinies for this scope
  caughtPokemons: CaughtPokemon[]
}

export interface GameEncounterResponse extends GameEncounterResponseBase {
  // C# PokemonData[] — the most recent encounters for this game
  latestEncounters: PokemonData[]
  // C# Dictionary<string, GameEncounterResponseBase> — per-game-file encounter data
  gameSpecificResponse: Record<string, GameEncounterResponseBase>
}

// ============================================================
// Individual Pokemon data (received in latestEncounters array)
// Matches C# PokemonData in Pokemon/PokemonData.cs
// ============================================================

export interface PokemonData {
  pid: number
  checksum: number
  species: number
  gender: number
  formCode: string
  name: string
  formIndex: number
  heldItem: number
  otId: number
  tid: number
  sid: number
  ability: number
  move1: number
  move2: number
  move3: number
  move4: number
  ivs: number
  nickname: string
  otName: string
  nature: number
  abilitySlot: number
  level: number
  hp: number
  maxHp: number
  attack: number
  defense: number
  speed: number
  spAttack: number
  spDefense: number
  // Computed fields (C# computed properties serialized to JSON)
  ivHp: number
  ivAtk: number
  ivDef: number
  ivSpe: number
  ivSpAtk: number
  ivSpDef: number
  shinyValue: number
  isShiny: boolean
}

export interface RecentEncounterEntry {
  pokemon: PokemonData
  game: string
  phase: string
  timestamp: string
}

// ============================================================
// Phase info (fetched from REST API /api/phaseinfo/{game}/{phase})
// Matches C# PhaseInfo / PhasePokemonInfo
// ============================================================

export interface PhasePokemonInfo {
  name: string
  speciesId: number
  amount: number
  formCode: string
  requireSpecificForm: boolean
  // Derived server-side: lets a male form be told apart from a genderless one, which the form code
  // alone cannot express (only ";f" is ever appended).
  hasFemaleForm: boolean
}

export interface PhaseInfo {
  gameName: string
  name: string
  pokemon: PhasePokemonInfo[]
  isStatic: boolean
}

// Wrapper returned by GET /api/phaseinfo/{game}/{phase}
// Matches C# WrappedPokemonPhase record in PhaseMetadataController.cs
export interface WrappedPokemonPhase {
  phaseInfo: PhaseInfo
  data: GameEncounterResponse
}

// ============================================================
// Subscription state (internal frontend state)
// ============================================================

export interface SubscriptionState {
  game: string
  phase: string
  gameName: string | null
  encounterData: GameEncounterResponse | null
  phaseInfo: PhaseInfo | null
}

// ============================================================
// Admin metadata (fetched from REST API GET /api/metadata)
// Matches C# AdminMetadata / GameMetadata in AdminController.cs
// ============================================================

export interface GameMetadata {
  // Full PhaseInfo objects — includes name, gameName, pokemon, isStatic
  phases: PhaseInfo[]
}

export interface AdminMetadata {
  // key = game name (e.g. "HGSS")
  games: Record<string, GameMetadata>
  // Backend ApplicationSettings.EnableMacroRecording — gates the dev-mode recorder tab.
  macroRecordingEnabled: boolean
}

// ============================================================
// Detected ROMs (GET /api/getAllGames, POST /api/refreshGames)
// Matches C# GameRomInfo in AdminController.cs
// ============================================================

export interface GameRomInfo {
  // The ROM's base filename. This is the value to post back as GameName — it says which
  // file an emulator opens, and the bot copies a ROM per instance so each gets its own save.
  handle: string
  // Everything below is read from the ROM header, not the filename.
  console: string
  flavor: string | null
  gameName: string
  gameCode: string
  language: string
  revision: number
  displayName: string
  revisionSupport: string
}

/**
 * Label for a ROM in a picker. Leads with the handle because that is what identifies the
 * file, and per-instance copies share a display name — "Pokemon SoulSilver" and
 * "Pokemon SoulSilver2" are the same game and would otherwise be indistinguishable.
 */
export function romLabel(rom: GameRomInfo): string {
  return `${rom.handle} — ${rom.gameName} rev${rom.revision}`
}

// ============================================================
// Macro recorder types (dev mode)
// Matches C# MacroRecordResult / MacroRecordStatus in MacroRecordingService.cs
// ============================================================

export interface MacroRecordResult {
  yaml: string
  frameCount: number
  stepCount: number
}

export interface MacroRecordStatus {
  recording: boolean
  runCount: number
  frameCount: number
}

// Matches C# LocationInfo in LocationService.cs.
// mapId is the raw game map id — the value Pathfind and the M. warp guard use.
// botMapId is the shared internal layout id, null when the raw id is not in the lookup table.
export interface LocationInfo {
  mapId: number
  botMapId: number | null
  internalName: string | null
  x: number
  y: number
  facing: number
  facingName: string
}

// ============================================================
// Bot control types (fetched from REST API GET /api/getTopLevelActions)
// Matches C# EventArgumentOptions / NewEventArguments in BotService.cs
// Note: the API returns an array of EventArgumentOptions
// ============================================================

export type ConditionType = 'Eq' | 'Neq'

// Matches C# TrainType enum in GameState.cs
export type TrainType = 'None' | 'Standard' | 'Switch'

export interface Condition {
  // C# enums serialize as integers by default; accept both string and number
  conditionType: ConditionType | number
  name: string
  value: string
}

export interface ConditionalParameter {
  condition: Condition
  name: string
  hideIfConditionNotMet: boolean
}

export interface EventArgumentOptions {
  name: string
  requiredParameters?: string[]
  optionalParameters?: string[]
  conditionalParameters?: ConditionalParameter[]
  // Present on actions that take a list of legs. Its fields are rendered once per leg.
  legParameters?: EventArgumentOptions
}

// Matches C# NewEventLegArguments in BotService.cs -- one leg of a chained action.
export interface NewEventLegArguments {
  phaseName?: string
  targetType?: string
  // C# EncounterType enum, sent as its integer value (Spin=0, Run=1)
  encounterType?: number
  xRange?: number
  yRange?: number
  // C# TrainType enum, sent as its integer value (None=0, Standard=1, Switch=2)
  trainType?: number
  targetLevel?: number
  // 1-based party slot of the battler to swap to, Switch train mode only
  switchSlot?: number
}

export interface NewEventArguments {
  eventName: string
  gameName?: string
  // C# TrainType enum, sent as its integer value (None=0, Standard=1, Switch=2)
  trainType?: number
  targetLevel?: number
  // 1-based party slot of the battler to swap to, Switch train mode only
  switchSlot?: number
  // Chained actions only; the flat fields above carry a single encounter.
  legs?: NewEventLegArguments[]
}

// Matches C# PartySlotInfo record in BotService.cs (GET /api/bot/getParty/{pos})
export interface PartySlotInfo {
  slot: number
  name: string
  level: number
  hp: number
  maxHp: number
  isShiny: boolean
}

// Matches C# EmulatorState record in BotService.cs
export interface EmulatorState {
  name: string
  eventState: number // 0=NotCreated, 1=NotStarted, 2=Running, 3=Waiting, 4=Complete
  gameFlavor: number // C# GameFlavor enum — may serialize as int or string name
}

// Bot dashboard state returned by GET /api/bot/getDashboardState,
// POST /api/startTopLevelAction, and POST /api/bot/cancelAction.
// Maps emulator position (int) -> EmulatorState
// JSON keys are numbers serialized as strings by System.Text.Json
export type BotDashboardState = Record<number, EmulatorState>
