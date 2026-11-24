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
    // taking keys; per held slot Down to it, A, Down to ITEM, A, Down, A on TAKE and A at the message, with the party read
    // again on the list; then B, B back to the field free and the party read again. Every press is a 2-frame hold sent on its
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

        // The Pokémon submenu's action id for ITEM (pokeruby pokemon_menu.c, POKEMENU_ITEM).
        private const string ItemAction = "2";

        // The menu window's state that waits for a button (round 1 item 6).
        private const int MessageWaits = 5;

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
        // outstanding (round 2): at the list on cursor 0, on the list after each take message, and in the free field at the
        // end, through readParty, the validated party read. The trip's frames and ms are counted from the field free.
        internal static async Task<string?> TakeAsync(
            GameState state, IBot bot, IGameStateReader game, RsPointerService addresses, Action clear, Func<Task<PokemonState>> readParty,
            Func<Task<uint>> readFrame, Func<PokemonData, bool> isPickupMember, PickupTally tally, string gameFileName,
            TimeSpan retryAfter, TimeSpan stale, TimeSpan fieldFreeStale, CancellationToken ct)
        {
            // The start menu takes keys only once its input task's data[0] is 1, which the reader alone reports (round 1).
            bool StartMenu(GameSnapshot x) => x.Screen == GameScreen.FieldTask && x.TaskFunction == addresses.StartMenuInputTask && x.AppState == 0;
            bool List(GameSnapshot x, int cursor) => x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.FieldPartyMenuTask
                && x.AppState == 0 && x.TextPrinter is null && x.PartyMonIndex == cursor;
            bool PokemonMenu(GameSnapshot x, int prompt) => x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.PokemonMenuTask
                && x.AppState == 0 && x.Prompt == prompt;
            bool ItemMenu(GameSnapshot x, int prompt) => x.Screen == GameScreen.OtherApp && x.TaskFunction == addresses.PokemonItemMenuTask
                && x.Prompt == prompt;
            Task<(GameSnapshot Confirmed, int Presses)> Press(string macro, string what, string by, GameSnapshot at, Func<GameSnapshot, bool> confirmed)
                => RsBattleEvent.PressConfirmedAsync(bot, game, macro, what, by, at, confirmed, retryAfter, stale, ct, "Pickup");

            try
            {
                // BattleComplete can come before the field is free; START there would not be read.
                GameSnapshot s = await GameStateWait.UntilAsync(game, "the field free before the Pickup visit", x => x.Screen == GameScreen.FieldFree,
                    fieldFreeStale, ct);
                uint firstFrame = await readFrame();
                Stopwatch trip = Stopwatch.StartNew();

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

                int cursor = 0;
                foreach (int i in slots)
                {
                    PokemonData member = party.PartyPokemon[i]!;
                    int n = i + 1;
                    // Mail is not visited (the spec's non-goal); left held, it is the still-holds stop below.
                    if (ItemIdIsMail(member.HeldItem))
                    {
                        Console.WriteLine($"[Pickup] slot {n} ({member.Name}) holds mail; not visited");
                        continue;
                    }

                    // Down never skips a slot, egg or fainted, and the walk stops at the slot, never reaching CANCEL.
                    while (cursor < i)
                    {
                        int from = cursor;
                        (s, _) = await Press(Down, $"Down on the party list {from} -> {from + 1}", $"the party cursor on {from + 1}", s,
                            x => List(x, from + 1));
                        cursor++;
                    }

                    // The menu shows 3 lag frames after the A (round 2), far inside the retry window; no party read until the list is back.
                    (s, _) = await Press(A, $"A on cursor {i}", $"slot {n}'s Pokémon menu", s, x => PokemonMenu(x, 0) && x.MenuEntries is not null);
                    string entries = s.MenuEntries!;
                    // Field moves come first in this menu, so ITEM is found where it is.
                    int item = Array.IndexOf(entries.Split(','), ItemAction);
                    if (item < 0)
                    {
                        Console.WriteLine($"[Pickup] slot {n} ({member.Name}) menu [{entries}]: no ITEM; left");
                        (s, _) = await Press(B, $"B on slot {n}'s menu", $"the party list taking keys on cursor {i}", s, x => List(x, i));
                        continue;
                    }
                    Console.WriteLine($"[Pickup] slot {n} ({member.Name}) menu [{entries}]: ITEM at position {item}");
                    while (s.Prompt < item)
                    {
                        int from = s.Prompt!.Value;
                        (s, _) = await Press(Down, $"Down on slot {n}'s menu {from} -> {from + 1}", $"the menu cursor on {from + 1}", s,
                            x => PokemonMenu(x, from + 1));
                    }
                    (s, _) = await Press(A, "A on ITEM", $"slot {n}'s ITEM submenu", s, x => ItemMenu(x, 0));
                    (s, _) = await Press(Down, "Down on GIVE", "the submenu cursor on TAKE", s, x => ItemMenu(x, 1));
                    // A full pocket prints its own message through the same follow-up, so it waits the same way (up to 58 frames).
                    (s, _) = await Press(A, "A on TAKE", $"slot {n}'s take message waiting for a button", s,
                        x => x.Screen == GameScreen.OtherApp && x.TaskFunction is null && x.TextPrinter == MessageWaits);
                    (s, _) = await Press(A, $"A at slot {n}'s take message", $"the party list taking keys on cursor {i}", s, x => List(x, i));

                    // A full pocket looks the same as a take to the snapshot: only the party tells them apart, so the take
                    // is recorded once the slot reads empty, and a Resume after the stop does not count it twice.
                    PokemonData now = (await readParty()).PartyPokemon[i]!;
                    if (now.HeldItem == 0)
                    {
                        Console.WriteLine($"[Pickup] took {ItemName(member.HeldItem)} (Gen 3 item {member.HeldItem}) from slot {n} ({member.Name})");
                        await tally.RecordAsync(gameFileName, state.Milestone, PickupTally.FromGen3(member.HeldItem));
                    }
                    else
                    {
                        Console.WriteLine($"[Pickup] slot {n} ({member.Name}) still holds {ItemName(now.HeldItem)} (Gen 3 item {now.HeldItem}) after TAKE; left for the stop");
                    }
                }

                (s, _) = await Press(B, "B on the party list", "the start menu taking keys", s, StartMenu);
                await Press(B, "B on the start menu", "the field free", s, x => x.Screen == GameScreen.FieldFree);
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

        // ItemIsMail, pokeruby: ITEM_ORANGE_MAIL (121) to ITEM_RETRO_MAIL (132).
        private static bool ItemIdIsMail(ushort item) => item is >= 121 and <= 132;

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
