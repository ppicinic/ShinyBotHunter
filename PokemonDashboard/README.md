# Pokemon Dashboard

A live Electron dashboard for Pokemon shiny hunting, built with React + TypeScript. Connects to the ShinyBotHunter C# backend via SignalR WebSocket.

---

## Requirements

- Node.js 18+
- ShinyBotHunter backend running (port 5001)
- Pokemon sprites on local disk

---

## Setup

### 1. Install dependencies

```bash
cd PokemonDashboard
npm install
```

### 2. Configure `config.json`

Edit `PokemonDashboard/config.json` before launching:

```json
{
  "backendUrl": "http://192.168.1.100:5001",
  "spritesRootPath": "D:/Sprites",
  "cards": {
    "phaseCard": { "enabled": true },
    "recentPokemonCard": { "enabled": false },
    "adminCard": { "enabled": false }
  }
}
```

| Field | Description |
|---|---|
| `backendUrl` | URL of the ShinyBotHunter backend (include port 5001) |
| `spritesRootPath` | Root path of your local sprite folder |
| `cards.*.enabled` | Show or hide each card type |

### Sprite directory structure

The dashboard expects sprites organized as follows under `spritesRootPath`:

```
D:/Sprites/
  Default/          ← normal male/genderless sprites (e.g. chikorita.png)
  Shiny/            ← shiny male/genderless sprites
  Female_Default/   ← normal female sprites (for species with unique female forms)
  Female_Shiny/     ← shiny female sprites
```

Sprite filenames match the `formCode` field from the backend (PokeAPI slugs, e.g. `chikorita.png`).
For female-form Pokemon the backend appends `;f` to the formCode — the dashboard strips this suffix and routes to the `Female_*` directories automatically.

---

## Development

```bash
npm run dev
```

Launches Electron with hot-reload for the renderer process.

---

## Production Build

```bash
npm run build
```

Compiles all processes to `out/`.

---

## Package as Windows .exe

```bash
npm run dist
```

Produces a Windows NSIS installer in `release/`. The `config.json` is bundled alongside the executable as an extra resource — edit it before running the installer or after installation.

---

## Architecture

### Cards

| Card | Description |
|---|---|
| **Phase Card** | Shows encounter counts, shiny counts, and caught/required progress for each Pokemon in the active phase. One card per active subscription. |
| **Recent Pokemon Card** | Shows the most recently encountered Pokemon with full stats (IVs, ShinyValue, nature, etc.). |
| **Admin Card** | Tabbed admin panel. Dashboard Config tab manages active game+phase subscriptions via REST API. Bot Control, Milestone Editor and Window Layout tabs, plus Dev Mode when the backend enables macro recording. |

### One window per card

Each card opens as its own OS window: Admin and Recent Encounters at startup when enabled in `config.json`, and one Phase window per active subscription. Move and resize them like any window; positions and sizes are saved to `window-layout.json` in the Electron userData folder and restored on next launch.

### SignalR API

| Direction | Method | Description |
|---|---|---|
| Client → Server | `SubscribeToGamePhase(game, phase)` | Subscribe to encounter updates for a game+phase |
| Server → Client | `RecieveEncounters` | Fired after each encounter batch |
| Server → Client | `ReceiveDashboardConfig` | Fired on connect and when admin updates active subscriptions |

---

## LAN / OBS Usage

- Set `backendUrl` to the LAN IP of the machine running ShinyBotHunter
- Multiple dashboards can connect simultaneously
- For OBS: use a Window Capture source per card window you want to show
