using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using static ShinyBotHunter.BotEvents.WildPokemonEncounterEvent;

namespace ShinyBotHunter.BotEvents
{
    public class OrchestratorEvent : AbstractBotEvent<EventState>
    {
        private bool gameStateSet = false;
        public GameState GameState
        {
            get; set
            {
                if (!gameStateSet)
                {
                    gameStateSet = true;
                    field = value;
                }
            }
        } = new GameState();
        public override EventState EventState => throw new NotImplementedException();
        private EncounterParams encounterParams;
        private IBotEvent<GameState> battleEvent;
        private NavigatorEvent navigatorEvent;
        private PokemonStateReader reader;
        private PokemonMetadataService metadataService;
        private MapService mapService;
        private PokemonPersistenceService persistenceService;
        private PhaseInfoService phaseInfoService;

        public OrchestratorEvent(IServiceProvider provider, EncounterParams encounterParams)
        {
            this.encounterParams = encounterParams;
            battleEvent = provider.GetRequiredService<BattleOrchestratorEvent>();
            navigatorEvent = provider.GetRequiredService<NavigatorEvent>();
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.metadataService = provider.GetRequiredService<PokemonMetadataService>();
            this.mapService = provider.GetRequiredService<MapService>();
            this.persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.phaseInfoService = provider.GetRequiredService<PhaseInfoService>();
        }

        public override async Task<EventState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            GameState = state;
            if(state.State.Count == 1 && state.State.Peek() == EventState.Resume)
            {
                
                await CheckForCatchAsync(state);

                if (state.State.Count > 1)
                {
                    state.State.Pop();
                }
                else { 
                    state.SwapState(EventState.DecideOverworldAction);
                }
            }
            if(state.State.Count > 0 && state.State.Peek() == EventState.Init)
            {
                if(state.StartX == -1 && state.StartY == -1 && state.StartMapId == -1)
                {
                    // TODO set map start point.
                    state.StartMapId = await state.Bot.GetMapId();
                    state.StartX = await state.Bot.GetTrainerXPos();
                    state.StartY = await state.Bot.GetTrainerYPos();
                    await UpdateMilestoneState(state);
                }
                state.SwapState(EventState.DecideOverworldAction);
            }
            while(state.State.Count != 0 && state.State.Peek() != EventState.Complete && state.State.Peek() != EventState.Waiting && !cancellationToken.IsCancellationRequested)
            {
                EventState topState = state.State.Peek();
                Console.WriteLine($"[Orchestrator] State: {topState}");
                if(topState == EventState.DecideOverworldAction)
                {
                    await DecideOverworldAction(state);
                }
                else if(topState == EventState.Encountering)
                {
                    await new RunEncounterEvent(state.Bot, encounterParams).ExecuteWithResultAsync(state, cancellationToken);
                }
                else if(topState == EventState.BattleStart)
                {
                    await battleEvent.ExecuteWithResultAsync(state, cancellationToken);
                }
                else if(topState == EventState.BattleComplete)
                {
                    // Pop BattleComplete and let the previous state resume.
                    // If the stack empties (normal encounter path), restart at DecideOverworldAction.
                    state.State.Pop();
                    if (state.State.Count == 0)
                    {
                        state.State.Push(EventState.DecideOverworldAction);
                    }
                }
                else if(topState == EventState.GoToPokemonCenter
                     || topState == EventState.HealAtPokemonCenter
                     || topState == EventState.ReturnToStart)
                {
                    await navigatorEvent.ExecuteWithResultAsync(state, cancellationToken);
                }
            }
            return state.State.Count == 0 ? EventState.NotStarted : state.State.Peek();
        }

        private async Task CheckForCatchAsync(GameState state)
        {
            bool found = (await reader.GetPokemonState(state.Bot)).Search(state.TargetPokemon!.PokemonData);

            Console.WriteLine($"Found pokemon: {found}");
            if (found)
            {
                // TODO put this in macro yaml
                state.Bot.RunMacroString("S.Save.3;W.5;T.A;W.60;T.A;W.90;T.A;W.1260;T.B;W.60;T.B;W.5");
                await state.Bot.WaitForMacroAsync();
                await persistenceService.AddCaughtPokemonAsync(state.TargetPokemon!);
                await Task.Delay(1000);
                await state.Bot.FlushSave();
            }
            state.TargetPokemon = null;
            await UpdateMilestoneState(state);
        }

        private async Task UpdateMilestoneState(GameState state)
        {
            var phaseInfo = phaseInfoService.GetPhaseInfo(GameFlavor.HGSS, state.Milestone);
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(GameFlavor.HGSS)).Where(x => x.GameFileName == state.Bot.GameFileName).ToList();
            bool complete = true;
            if (phaseInfo is not null)
            {
                foreach (var targetPokemon in phaseInfo.Pokemon)
                {
                    if (targetPokemon.RequireSpecificForm)
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.FormCode == targetPokemon.FormCode).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                    else
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.Name == targetPokemon.Name).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                }
            }
            state.MilestoneComplete = complete;
        }

        private async Task DecideOverworldAction(GameState state)
        {
            var pokeState = await reader.GetPokemonState(state.Bot);
            var lead = pokeState.PartyPokemon[0]!;
            bool training = state.TrainType != TrainType.None;

            // No battle is in flight here, so a switch that aborted mid-encounter can't leak into the next one.
            state.SwitchPerformed = false;

            if (training && !state.TrainingComplete)
            {
                // The trainee always leads, in both standard and switch training.
                if (lead.Level >= state.TargetLevel!.Value)
                {
                    state.TrainingComplete = true;
                    state.OriginalBattleMode = BattleMode.Flee;
                }
            }

            if (state.MilestoneComplete && (!training || state.TrainingComplete))
            {
                state.SwapState(EventState.Complete);
                await state.BotService.RefreshDashboardState();
                return;
            }

            if (state.OriginalBattleMode == BattleMode.Flee || CanFightNextEncounter(state, pokeState, lead))
            {
                state.BattleMode = state.OriginalBattleMode;
                state.SwapState(EventState.Encountering);
            }
            else
            {
                state.BattleMode = BattleMode.Flee;
                state.SwapState(EventState.GoToPokemonCenter);
            }
        }

        /// <summary>
        /// In switch training the lead only has to survive long enough to be sent out -- the battler in
        /// the configured slot is the one that needs HP and PP. A fainted lead is still fatal though:
        /// the game would send out someone else and the trainee would stop banking EXP entirely.
        /// </summary>
        private bool CanFightNextEncounter(GameState state, PokemonState pokeState, PokemonData lead)
        {
            if (state.TrainType != TrainType.Switch)
            {
                return CanBattle(lead);
            }
            int battlerIndex = state.SwitchSlot!.Value - 1;
            var battler = battlerIndex < pokeState.PartySize ? pokeState.PartyPokemon[battlerIndex] : null;
            if (battler is null)
            {
                // The configured slot is empty -- nothing to switch to, so treat it like a spent party.
                return false;
            }
            return lead.HP > 0 && CanBattle(battler);
        }

        private bool CanBattle(PokemonData pokemon)
        {
            if(pokemon.HP < pokemon.MaxHP / 4)
            {
                return false;
            }
            int moveCount = 0;
            foreach(var move in pokemon.Moves)
            {
                if(move.Id > 0 && metadataService.GetPokemonMoveMetadata(move.Id).IsDamageMove) // TODO check for bad moves like False Swipe or recoil
                {
                    moveCount += move.PP;
                }
            }
            return moveCount >= 3;
        }
    }
}
