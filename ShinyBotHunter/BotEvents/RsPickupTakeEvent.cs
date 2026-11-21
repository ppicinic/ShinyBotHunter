using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    // The Ruby and Sapphire Pickup visit after every decision (docs/specs/rs-pickup.md FR-4 to FR-10, emulator rounds 1 and
    // 2 on issue #485): from the field free, START, the start cursor walked to POKéMON, A; the party read with the list
    // taking keys; then B, B back to the field free and the party read again. Every press is a 2-frame hold sent on its
    // gate snapshot and confirmed by RsBattleEvent.PressConfirmedAsync (AGENTS rule 16). Every stop clears the macro and
    // parks in Waiting with the reason logged, as PickupTakeEvent parks.
    public class RsPickupTakeEvent : AbstractBotEvent<bool>
    {
        // The visit's single presses, each a 2-frame hold as RsBattleEvent's.
        internal const string Start = "H.Start.2";
        internal const string A = "H.A.2";
        internal const string B = "H.B.2";
        internal const string Down = "H.Down.2";
        internal const string Up = "H.Up.2";

        // Every key's first change shows within 3 frames of the key being seen (round 2), so a snapshot equal to the gate
        // a second after the hold is a dropped key; the longest wait is 58 frames, the full pocket's message.
        internal static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan Stale = TimeSpan.FromSeconds(3);
        internal static readonly TimeSpan FieldFreeStale = TimeSpan.FromSeconds(5);

        // The start menu's action id for POKéMON (pokeruby start_menu.c, MENU_ACTION_POKEMON).
        private const string PokemonAction = "1";

        private readonly IBot bot;
        private readonly PokemonStateReader reader;
        private readonly PickupTally tally;

        public RsPickupTakeEvent(IBot bot, PokemonStateReader reader, PickupTally tally)
        {
            this.bot = bot;
            this.reader = reader;
            this.tally = tally;
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // The profile's reader is the flee reader; the menus are read only with the training reads, as RsBattleEvent reads them.
            RsPointerService addresses = (RsPointerService)bot.Addresses;
            RsStateReader game = new(bot, addresses, trainingReads: true);
            Func<Task<uint>> readFrame = async () =>
                BitConverter.ToUInt32((await bot.ReadChainBlocksAsync([new(addresses.VBlankCounter, [], 4)], ct: cancellationToken))[0].Bytes, 0);
            string? park = await TakeAsync(state, bot, game, addresses, bot.ClearMacro, () => reader.GetPartyState(bot), readFrame,
                bot.Profile.IsPickupMember!, tally, bot.GameFileName, RetryAfter, Stale, FieldFreeStale, cancellationToken);
            return park is null;
        }

        // The visit. Returns null when every Pickup member is empty-handed afterwards, with the state stack untouched,
        // otherwise the reason the event parked in Waiting. The party is read only on a gate snapshot with no press
        // outstanding (round 2): at the list on cursor 0, and in the free field at the end, through readParty, the
        // validated party read.
        internal static async Task<string?> TakeAsync(
            GameState state, IBot bot, IGameStateReader game, RsPointerService addresses, Action clear, Func<Task<PokemonState>> readParty,
            Func<Task<uint>> readFrame, Func<PokemonData, bool> isPickupMember, PickupTally tally, string gameFileName,
            TimeSpan retryAfter, TimeSpan stale, TimeSpan fieldFreeStale, CancellationToken ct)
        {
            // The start menu takes keys only once its input task's data[0] is 1, which the reader alone reports (round 1).
            bool StartMenu(GameSnapshot x) => x.Screen == GameScreen.FieldTask && x.TaskFunction == addresses.StartMenuInputTask && x.AppState == 0;
            bool List(GameSnapshot x, int cursor) => x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.FieldPartyMenuTask
                && x.AppState == 0 && x.TextPrinter is null && x.PartyMonIndex == cursor;
            Task<(GameSnapshot Confirmed, int Presses)> Press(string macro, string what, string by, GameSnapshot at, Func<GameSnapshot, bool> confirmed)
                => RsBattleEvent.PressConfirmedAsync(bot, game, macro, what, by, at, confirmed, retryAfter, stale, ct, "Pickup");

            Stopwatch trip = Stopwatch.StartNew();
            uint firstFrame;
            try
            {
                // BattleComplete can come before the field is free; START there would not be read.
                GameSnapshot s = await GameStateWait.UntilAsync(game, "the field free before the Pickup visit", x => x.Screen == GameScreen.FieldFree,
                    fieldFreeStale, ct);
                firstFrame = await readFrame();

                (s, _) = await Press(Start, "START", "the start menu taking keys", s, StartMenu);

                // The game remembers the start cursor; it is walked to POKéMON one press at a time, never through the wrap (round 2).
                int pokemon = Array.IndexOf(s.MenuEntries!.Split(','), PokemonAction);
                while (s.Prompt != pokemon)
                {
                    int from = s.Prompt!.Value;
                    (string macro, string name, int next) = from > pokemon ? (Up, "Up", from - 1) : (Down, "Down", from + 1);
                    (s, _) = await Press(macro, $"{name} on the start menu {from} -> {next}", $"start cursor {next}", s,
                        x => StartMenu(x) && x.Prompt == next);
                }

                (s, _) = await Press(A, "A on POKéMON", "the party list taking keys on cursor 0", s, x => List(x, 0));

                // The list takes keys: the party is read here, not before (FR-5).
                PokemonState party = await readParty();
                List<int> slots = [];
                int members = 0;
                for (int i = 0; i < party.PartySize; i++)
                {
                    PokemonData member = party.PartyPokemon[i]!;
                    if (!isPickupMember(member))
                    {
                        continue;
                    }
                    members++;
                    if (member.HeldItem != 0)
                    {
                        slots.Add(i);
                    }
                }
                Console.WriteLine($"[Pickup] held {slots.Count} of {members} pickup members; {(slots.Count > 0 ? "take" : "nothing to take")}");

                (s, _) = await Press(B, "B on the party list", "the start menu taking keys", s, StartMenu);
                (s, _) = await Press(B, "B on the start menu", "the field free", s, x => x.Screen == GameScreen.FieldFree);
                Console.WriteLine($"[Pickup] trip took {await readFrame() - firstFrame} frames, {trip.ElapsedMilliseconds} ms");
            }
            catch (GameStateTimeoutException ex)
            {
                return Park(state, clear, $"Pickup: {ex.Message}");
            }

            PokemonState after = await readParty();
            for (int i = 0; i < after.PartySize; i++)
            {
                PokemonData member = after.PartyPokemon[i]!;
                if (isPickupMember(member) && member.HeldItem != 0)
                {
                    return Park(state, clear, $"Pickup: {member.Name} still holds {ItemName(member.HeldItem)} after the take; check the bag and the Pokémon menu.");
                }
            }
            return null;
        }

        private static string ItemName(ushort gen3Item) => PickupTally.ItemName(PickupTally.FromGen3(gen3Item));

        private static string Park(GameState state, Action clear, string reason)
        {
            clear();
            Console.WriteLine($"[Pickup] {reason}");
            state.SwapState(EventState.Waiting);
            return reason;
        }
    }
}
