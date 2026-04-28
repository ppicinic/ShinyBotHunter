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
  // C# Dictionary<int, PokemonCounts> — JSON keys are numbers
  pokemonEncounters: Record<number, PokemonCounts>
}

export interface GameEncounterResponse {
  allPokemonCounts: PokemonCounts
  // C# Dictionary<int, PokemonCounts> — JSON keys are numbers
  pokemonEncounters: Record<number, PokemonCounts>
  // C# Dictionary<string, PhaseEncounterResponse> — JSON keys are phase name strings
  phaseEncounters: Record<string, PhaseEncounterResponse>
  // C# PokemonData[] — the most recent encounters for this game
  latestEncounters: PokemonData[]
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
}

export interface PhaseInfo {
  gameName: string
  name: string
  pokemon: PhasePokemonInfo[]
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
  encounterData: GameEncounterResponse | null
  phaseInfo: PhaseInfo | null
}

// ============================================================
// Admin metadata (fetched from REST API GET /api/metadata)
// Matches C# AdminMetadata / GameMetadata in AdminController.cs
// ============================================================

export interface GameMetadata {
  phases: string[]
}

export interface AdminMetadata {
  // key = game name (e.g. "HGSS")
  games: Record<string, GameMetadata>
}

// ============================================================
// Bot control types (fetched from REST API GET /api/getTopLevelActions)
// Matches C# EventArgumentOptions / NewEventArguments in BotService.cs
// Note: the API returns an array of EventArgumentOptions
// ============================================================

export interface EventArgumentOptions {
  name: string
  requiredParameters?: string[]
}

export interface NewEventArguments {
  eventName: string
  gameName?: string
}

// Bot dashboard state returned by GET /api/bot/getDashboardState,
// POST /api/startTopLevelAction, and POST /api/bot/cancelAction.
// Maps emulator position (int) -> display name (string, e.g. "Bot 0" or event name)
// JSON keys are numbers serialized as strings by System.Text.Json
export type BotDashboardState = Record<number, string>
