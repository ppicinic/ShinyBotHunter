using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    // One Pickup visit after every decision (docs/specs/pickup-mode.md, FR-6 and FR-7): from the field free, X, POKéMON,
    // then, with the slots read at the list, per held slot the slot, ITEM, TAKE and A, then CANCEL back to the field free. Every press is confirmed by the next snapshot on
    // app.state, partyMonIndex or the printer; nothing is retried. Every stop clears the macro and parks in Waiting with
    // the reason logged, as HeadbuttEncounterEvent parks.
    public class PickupTakeEvent : AbstractBotEvent<bool>
    {
        // ABILITY_PICKUP, include/constants/abilities.h.
        internal const byte AbilityPickup = 53;

        private readonly IBot bot;
        private readonly PokemonStateReader reader;
        private readonly PickupConfig config;
        private readonly PickupTally tally;

        public PickupTakeEvent(IBot bot, PokemonStateReader reader, PickupConfig config, PickupTally tally)
        {
            this.bot = bot;
            this.reader = reader;
            this.config = config;
            this.tally = tally;
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            Func<string, Task> run = async macro =>
            {
                bot.RunMacroString(macro);
                await bot.WaitForMacroAsync(cancellationToken);
            };
            string? park = await TakeAsync(state, bot.State, (HgssPointerService)bot.Addresses, config, run, bot.ClearMacro,
                () => reader.GetPartyState(bot), tally, bot.GameFileName, cancellationToken);
            return park is null;
        }

        // The visit. Returns null when every Pickup member is empty-handed afterwards, with the
        // state stack untouched, otherwise the reason the event parked in Waiting.
        internal static async Task<string?> TakeAsync(
            GameState state, IGameStateReader game, HgssPointerService addresses, PickupConfig config, Func<string, Task> run, Action clear, Func<Task<PokemonState>> readParty,
            PickupTally tally, string gameFileName, CancellationToken ct)
        {
            Stopwatch trip = Stopwatch.StartNew();
            try
            {
                // BattleComplete can come on a field task, where the X would be confirmed by the task already running.
                await GameStateWait.UntilAsync(game, "the field free before the Pickup trip", s => s.Screen == GameScreen.FieldFree,
                    Stale(config.FieldFreeStaleMs), ct);

                await run("T.X");
                await GameStateWait.UntilAsync(game, "the start menu after X", s => s.Screen == GameScreen.FieldTask,
                    Stale(config.StartMenuStaleMs), ct);

                await run(Touch(config.PokemonTouch));
                await GameStateWait.UntilAsync(game, "the party menu after POKéMON",
                    s => s.Screen == GameScreen.OtherApp && s.AppState == addresses.PartyMenuList, Stale(config.PartyMenuStaleMs), ct);

                // The list is on screen, with an item icon on each holder's card: the party is read here, not before.
                PokemonState party = await readParty();
                List<int> slots = [];
                int members = 0;
                for (int i = 0; i < party.PartySize; i++)
                {
                    PokemonData member = party.PartyPokemon[i]!;
                    if (member.Ability != AbilityPickup)
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

                TimeSpan step = Stale(config.StepStaleMs);
                foreach (int i in slots)
                {
                    PokemonData member = party.PartyPokemon[i]!;
                    // Mail shows Mail where Item is (non-goal); left held, it is the still-holds stop below.
                    if (ItemIdIsMail(member.HeldItem))
                    {
                        Console.WriteLine($"[Pickup] slot {i + 1} ({member.Name}) holds mail, item {member.HeldItem}; not visited");
                        continue;
                    }

                    await run(Touch(config.SlotTouches[i]));
                    await GameStateWait.UntilAsync(game, $"slot {i + 1}'s context menu",
                        s => s.AppState == addresses.PartyMenuContextMenu && s.PartyMonIndex == i, step, ct);

                    await run(Touch(config.ItemTouch));
                    await GameStateWait.UntilAsync(game, $"slot {i + 1}'s Item menu", s => s.AppState == addresses.PartyMenuItemMenu, step, ct);

                    await run(Touch(config.TakeTouch));
                    await GameStateWait.UntilAsync(game, $"slot {i + 1}'s take message at the arrow",
                        s => s.AppState == addresses.PartyMenuTakeMessage && s.TextPrinter is 2 or 3, step, ct);

                    await run("T.A");
                    await GameStateWait.UntilAsync(game, $"the party list after slot {i + 1}'s take", s => s.AppState == addresses.PartyMenuList, step, ct);
                    Console.WriteLine($"[Pickup] took item {member.HeldItem} from slot {i + 1} ({member.Name})");
                    await tally.RecordAsync(gameFileName, state.Milestone, member.HeldItem);
                }

                await run(Touch(config.CancelTouch));
                await GameStateWait.UntilAsync(game, "the field free after CANCEL", s => s.Screen == GameScreen.FieldFree,
                    Stale(config.FieldFreeStaleMs), ct);
            }
            catch (GameStateTimeoutException ex)
            {
                return Park(state, clear, $"Pickup: {ex.Message}");
            }
            Console.WriteLine($"[Pickup] trip took {trip.ElapsedMilliseconds} ms");

            PokemonState after = await readParty();
            for (int i = 0; i < after.PartySize; i++)
            {
                PokemonData member = after.PartyPokemon[i]!;
                if (member.Ability == AbilityPickup && member.HeldItem != 0)
                {
                    return Park(state, clear, $"Pickup: {member.Name} still holds item {member.HeldItem} after the take; check the Pokémon menu.");
                }
            }
            return null;
        }

        // ItemIdIsMail, pokeheartgold src/item.c: ITEM_GRASS_MAIL (137) to ITEM_BRICK_MAIL (148).
        private static bool ItemIdIsMail(ushort item) => item is >= 137 and <= 148;

        // After the menu input-ready gap.
        private static string Touch(int[] xy) => $"{GameStateWait.MenuWait}S.{xy[0]}.{xy[1]}.3";

        private static TimeSpan Stale(int ms) => TimeSpan.FromMilliseconds(ms);

        private static string Park(GameState state, Action clear, string reason)
        {
            clear();
            Console.WriteLine($"[Pickup] {reason}");
            state.SwapState(EventState.Waiting);
            return reason;
        }
    }
}
