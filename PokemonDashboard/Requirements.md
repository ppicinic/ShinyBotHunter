# Pokemon Live Bot Dashboard

## Overview
Create a live Electron dashboard that starts like an exe in React+Typescript. The dashboard should have multiple cards with differing functionality.

## Backend
The backend is located in ShinyBotHunter and is in C#. There will be a REST and websocket API (signalR). Currently only one websocket API is active for encounters.

## Dashboard Usage
The dashboard should be usable by multiple PCs on local LAN at the same time. One use case is for streaming (OBS) so the UI must be stream friendly. The other use case is admin control.

## Cards Overview
The cards should be resizable and movable. They should also snap to each other. Cards should be configurable with a config file on which display and which do not. Multiple phases or games may be played at a time. The Dashboard should have a way to decide what games and views to watch, and to be able to show data from multiple games & phases simultaneously. The following cards required are:

### Phase Card
Shows the current phase. Should show the pokemon needed in that phase. Note that the requirements of a phase could be a pokemon or a specific form of a Pokemon. It should show all the encounter numbers of the pokemon in that phase/overall and how many are caught out of how many required.

### Recent Pokemon Card
Shows the most recently seen Pokemon and their stats (including ShinyValue) in order.

### Admin Card
This should be the Admin control. This will probably need multiple tabs for controlling different actions. We can currently do the following actions.
 - Controlling bots tab
   - Get top level actions that the bot can do. This currently is launching a new bot emulator or special events that emulate multiple bots automatically.
   - Start a top level actions. The get API tells what actions are available and what parameters are required.
 - General Admin Tab / State info
   - Subscribe and unsubscribe to a game+phase
   - update configs. We can use this to update the how many recent pokemon encounters to be able to display
   - Get game metadata. this currently gives information like what games and phases are currently supported.