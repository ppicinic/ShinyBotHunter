import type { Appearance } from '../../../shared/windowContracts'

// ============================================================
// Static App Config (loaded from config.json via IPC)
// ============================================================

export interface AppConfig {
  backendUrl: string
  apiUrl: string
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
  followsHunt?: boolean
}

export interface DashboardConfig {
  // C# GamePhase[] — each has game (GameFlavor enum) and phase (string)
  gamePhases: ActiveSubscription[]
  recentEncounterCount: number
  // null until the backend's first push
  appearance: Appearance | null
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
  // A location id ("34-land"), or null for none
  location: string | null
  // The game files it is offered on; empty is every file
  gameFiles: string[]
  // Hidden from the launch form only
  archived: boolean
}

// Returned by GET /api/phaseinfo/{game}/{phase}[/{gameFileName}]
// Matches the response of GetPhaseInfo in PhaseMetadataController.cs; only the fields the card reads
export interface PhaseInfoResponse {
  phaseInfo: PhaseInfo
  data: GameEncounterResponse
  // False when no hunt runs on the file
  live: boolean
  // The time of day the file was last seen in; null without a file or when never seen since the backend started
  timeOfDay: PlanTime | null
  // Null for a milestone with no location, or a file whose ROM version is not known
  location: MilestoneLocation | null
  // The location the editor offers to attach a milestone with none to; null when it has one or there is no suggestion
  suggestedLocation: string | null
}

// The location block of PhaseInfoResponse: one row per phaseInfo.pokemon row, in the same order
export interface MilestoneLocation {
  rows: {
    key: string
    // The only version the species is met in here; null when both
    version: RomVersion | null
    // When the species can be met here, in Morning, Day, Night order; empty when not at all
    times: PlanTime[]
    // The expected share now; null when there are no shares
    share: number | null
    hiddenNow: boolean
  }[]
  // The names of the rows hidden now, by the next time they can be met
  hiddenUntil: Partial<Record<PlanTime, string[]>>
}

// A row of GET /api/milestones/{game}/locations/{locationId}/seed: the row to save, plus the only version it is met in
// (null: both) and when it is met, in Morning, Day, Night order
export interface SeedRow extends PhasePokemonInfo {
  version: RomVersion | null
  times: PlanTime[]
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
  location: MilestoneLocation | null
  live: boolean
  timeOfDay: PlanTime | null
  // The endpoint does not know the milestone, so the card says so instead of loading forever
  notFound: boolean
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
// Milestone picker (GET /api/bot/milestones/{pos})
// Matches C# PickerMilestone (MilestonePicker.cs) and BotMilestones (BotDtos.cs)
// ============================================================

export type Modality = 'Land' | 'Surf' | 'OldRod' | 'GoodRod' | 'SuperRod' | 'RockSmash' | 'Headbutt'

// Matches C# RomVersion (RomInfo.cs)
export type RomVersion = 'HeartGold' | 'SoulSilver' | 'Ruby' | 'Sapphire'

// GET /api/milestones/{game}/locations: one entry per location id, by modality then name
export interface LocationOption {
  id: string
  name: string
  modality: Modality
}

// Matches C# Anchor (Milestone.cs): a tile on game map `map`, run left-right xRange or up-down yRange tiles each way, or with both 0 a spin.
export interface Anchor {
  map: number
  x: number
  y: number
  xRange: number
  yRange: number
}

export interface PickerMilestone {
  // A saved milestone's name. Sent as PhaseName at Start.
  name: string
  // Its location's modality on this file's version; null with no location.
  modality: Modality | null
  huntable: boolean
  complete: boolean
  // Where the hunt starts on this file; null is hunted where the bot stands.
  anchor: Anchor | null
  // True when this file stores a replacement, false when the anchor is the generated one.
  anchorSet: boolean
}

export interface BotMilestones {
  gameFileName: string
  // The saved milestones ticked for this file, or for none, and not archived; natural order by name.
  milestones: PickerMilestone[]
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
 * Label for a ROM in a picker: the filename, which is what the user picked it by. The header's
 * game name and revision used to be appended to disambiguate per-instance copies, but the handle
 * already does that -- copies differ only by filename -- so the suffix was noise on every row.
 */
export function romLabel(rom: GameRomInfo): string {
  return rom.handle
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

// Matches C# EncounterType enum
export type EncounterType = 'Spin' | 'Run' | 'Headbutt'

export interface Condition {
  conditionType: ConditionType
  name: string
  value: string
}

export interface ConditionalParameter {
  condition: Condition
  name: string
  hideIfConditionNotMet: boolean
  // Must be filled before the action can start, while its condition holds.
  required?: boolean
}

export interface EventArgumentOptions {
  name: string
  requiredParameters?: string[]
  optionalParameters?: string[]
  conditionalParameters?: ConditionalParameter[]
  // Present on actions that take a list of legs. Its fields are rendered once per leg.
  legParameters?: EventArgumentOptions
  // When present, the legs are only offered while this holds.
  legCondition?: Condition
  // When present, the GameName dropdown offers only the ROMs this admits.
  gameFilter?: GameFilter | null
  // Parameter name to choice to the reason it is unavailable; null when nothing is disabled.
  disabledChoices?: Record<string, Record<string, string>> | null
  // Parameter name to the values it allows, rendered as a dropdown; null when the server offers no list.
  choices?: Record<string, string[]> | null
}

// Matches C# GameFilter in BotDtos.cs. testedOnly admits only ROMs whose revisionSupport is "Tested".
export interface GameFilter {
  flavor: string
  testedOnly: boolean
}

// Matches C# NewEventLegArguments in BotService.cs -- one leg of a chained action.
export interface NewEventLegArguments {
  phaseName?: string
  targetType?: string
  encounterType?: EncounterType
  xRange?: number
  yRange?: number
  trainType?: TrainType
  targetLevel?: number
  // 1-based party slot of the battler to swap to, Switch train mode only
  switchSlot?: number
  // Take the Pickup Pokémon's held items after a won battle; sent only with a train mode
  pickup?: boolean
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
  // C# EventState name, e.g. 'Waiting'
  eventState: string
  gameFlavor: GameFlavor
}

// Matches C# GameFlavor enum in Macro/GameFlavor.cs
export type GameFlavor = 'HGSS' | 'RS'

// Bot dashboard state returned by GET /api/bot/getDashboardState,
// POST /api/startTopLevelAction, and POST /api/bot/cancelAction.
// Maps emulator position (int) -> EmulatorState
// JSON keys are numbers serialized as strings by System.Text.Json
export type BotDashboardState = Record<number, EmulatorState>

// ============================================================
// The preset a milestone's seed is filled by (GET /api/milestones/{game}/locations/{locationId}/seed)
// Matches C# GoalPreset (GoalPresets.cs)
// ============================================================

export type GoalPreset = 'None' | 'LivingDex' | 'FemaleForms' | 'VersionExclusives'

// ============================================================
// Saved hunt plans (GET /api/plans/{game}, PUT, DELETE and POST .../rename on /api/plans/{game}/{name})
// Matches C# PlanRule and HuntPlan (Milestones/HuntPlan.cs)
// ============================================================

// The SlotCondition values a rule uses: morning 4 to 10, day 10 to 20, night 20 to 4.
export type PlanTime = 'Morning' | 'Day' | 'Night'

// C# DayOfWeek as JsonStringEnumConverter writes it.
export type Weekday = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

// An empty times or weekdays matches any.
export interface PlanRule {
  times: PlanTime[]
  weekdays: Weekday[]
  milestone: string
}

export interface HuntPlan {
  game: GameFlavor
  name: string
  default: string
  rules: PlanRule[]
}
