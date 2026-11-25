using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Games;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class MilestoneRowsTests
    {
        private const int Caterpie = 10, Metapod = 11, Weedle = 13, Kakuna = 14, Pidgey = 16, Rattata = 19, Hoothoot = 163, Ledyba = 165;
        private const int Spinarak = 167, Unown = 201, Linoone = 264, Whismur = 293, Bidoof = 399, Buizel = 418;
        private const string File = "Pokemon SoulSilver";

        // The species with a female form in the families on Route 30 (production's PokemonMetadata, read-only export, 2026-11-25).
        private static readonly HashSet<int> HasFemaleForm = [19, 20, 165, 166, 399, 400, 418, 419];

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));

        // Production's 20 phase documents as stored (read-only export, 2026-11-25).
        private static readonly List<PhaseInfo> ProductionPhases = BsonSerializer.Deserialize<List<PhaseInfo>>(
            System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

        private static PokemonMetadataService Metadata()
        {
            var metadata = new PokemonMetadataService(null!);
            for (ushort id = 1; id <= 493; id++)
            {
                string name = SpeciesName.GetSpeciesName(id, (int)LanguageID.English);
                metadata.Seed(new PokemonMetadata { Id = id, PokemonName = name, PokeApiName = name.ToLowerInvariant(), HasFemaleForm = HasFemaleForm.Contains(id) });
            }
            return metadata;
        }

        private static Milestone Route30(RomVersion version) => Hgss[version].Single(m => m.Id == "34-land");

        // TID 0, SID 0 and PID 0 give shiny value 0; PID 0x00080000 gives 8, not shiny.
        private static CaughtPokemon Caught(int species, string formCode, string gameFileName, string phase = "34-land", bool shiny = true) => new()
        {
            Pokemon = new PokemonData { Species = (ushort)species, FormCode = formCode, PID = shiny ? 0u : 0x00080000u },
            Phase = phase,
            GameFileName = gameFileName
        };

        // Today's phase rows, keyed as the phase card keys them, with the species resolved as PhaseInfoService.InitAsync does.
        private static List<(int Species, string Key, string? FormCode, int Amount)> PhaseRows(string name, PokemonMetadataService metadata)
            => ProductionPhases.Single(p => p.GameName == "HGSS" && p.Name == name).Pokemon.Select(p =>
            {
                var species = p.SpeciesId != 0 ? metadata.GetPokemonMetadata(p.SpeciesId) : metadata.GetPokemonMetadata(p.Name);
                string? formCode = p.RequireSpecificForm ? (string.IsNullOrEmpty(p.FormCode) ? species.PokeApiName : p.FormCode) : null;
                return (species.Id, formCode ?? CacheLoadRequest.SpeciesKey(species.Id), formCode, p.Amount);
            }).ToList();

        [Theory]
        [InlineData("1-4 Route 30", RomVersion.SoulSilver, GoalPreset.LivingDex)]
        [InlineData("1-4b Route 30", RomVersion.SoulSilver, GoalPreset.FemaleForms)]
        [InlineData("1-4c Route 30", RomVersion.HeartGold, GoalPreset.VersionExclusives)]
        public void Route30_ThreeProductionGoals_GiveTodaysRows(string phase, RomVersion version, GoalPreset preset)
        {
            var metadata = Metadata();
            var phaseRows = PhaseRows(phase, metadata);
            // The goal: the phase's amounts, then the preset for every species they do not name.
            var goal = new GameFile
            {
                Id = File,
                Entries = GoalPresets.Apply(phaseRows.Select(r => new GoalEntry(r.Species, r.FormCode, r.Amount)).ToList(),
                    GoalPresets.Entries(preset, version, Hgss, metadata))
            };

            var rows = MilestoneRows.Build(Route30(version), goal, []);

            Assert.All(phaseRows, p => Assert.Equal(p.Amount, rows.Single(r => r.Key == p.Key).Wanted));
            var extras = rows.Where(r => phaseRows.All(p => p.Key != r.Key)).Select(r => (r.Key, r.Wanted)).ToList();
            (string, int)[] expected = phase switch
            {
                "1-4 Route 30" => [("#14", 3), ("#293", 3), ("#264", 2), ("#418", 2), ("#399", 2)],
                "1-4b Route 30" => [("#293", 0), ("#264", 0), ("buizel;f", 2), ("buizel", 0), ("bidoof;f", 2), ("bidoof", 0)],
                // Metapod, like Caterpie and Spinarak, is in no SoulSilver table.
                _ => [("#16", 0), ("#11", 1), ("#19", 0), ("#163", 0), ("#293", 0), ("#264", 0), ("#418", 0), ("#399", 0)]
            };
            Assert.Equal(expected, extras);
        }

        [Fact]
        public void RuinsOfAlph_UnownLettersAreRows()
        {
            var goal = new GameFile { Id = File, Entries = GoalPresets.Entries(GoalPreset.LivingDex, RomVersion.SoulSilver, Hgss, Metadata()).ToList() };

            var rows = MilestoneRows.Build(Hgss[RomVersion.SoulSilver].Single(m => m.Id == "315-land"), goal, []);

            Assert.Equal(28, rows.Count);
            Assert.All(rows, r => Assert.Equal((Unown, 1, true), (r.Species, r.Wanted, r.IsTarget)));
            Assert.Equal(["unown-a", "unown-question"], new[] { rows[0].Key, rows[27].Key });
        }

        [Fact]
        public void SpeciesWithoutEntry_IsRowAtZero_NeverTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 3)] };

            var rows = MilestoneRows.Build(Route30(RomVersion.SoulSilver), goal, [Caught(Weedle, "weedle", File)]);

            var weedle = rows.Single(r => r.Species == Weedle);
            Assert.Equal(("#13", 0, 1, false, false), (weedle.Key, weedle.Wanted, weedle.Caught, weedle.IsTarget, weedle.Complete));
            Assert.Equal([Ledyba, Weedle, Kakuna, Pidgey, Hoothoot, Rattata, Whismur, Linoone, Buizel, Bidoof], rows.Select(r => r.Species));
        }

        [Fact]
        public void CompleteRow_IsShownComplete_NotTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 2)] };

            var rows = MilestoneRows.Build(Route30(RomVersion.SoulSilver), goal, [Caught(Pidgey, "pidgey", File), Caught(Pidgey, "pidgey", File)]);

            var pidgey = rows.Single(r => r.Species == Pidgey);
            Assert.Equal((2, 2, true, false), (pidgey.Wanted, pidgey.Caught, pidgey.Complete, pidgey.IsTarget));
        }

        [Fact]
        public void HiddenRow_IsNotTarget_StillARow()
        {
            var goal = new GameFile
            {
                Id = File,
                Entries = [new GoalEntry(Rattata, "rattata;f", 2), new GoalEntry(Rattata, "rattata", 1)],
                Hidden = [new HiddenRow("34-land", "rattata")]
            };

            var rows = MilestoneRows.Build(Route30(RomVersion.SoulSilver), goal, []);

            Assert.Equal([("rattata;f", false, true), ("rattata", true, false)],
                rows.Where(r => r.Species == Rattata).Select(r => (r.Key, r.Hidden, r.IsTarget)));
        }

        [Fact]
        public void Caught_IsGlobalPerFile()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 3), new GoalEntry(Rattata, "rattata;f", 2), new GoalEntry(Rattata, "rattata", 1)] };
            CaughtPokemon[] caught =
            [
                Caught(Pidgey, "pidgey", File, phase: "1-2 Route 29"),
                Caught(Pidgey, "pidgey", "Pokemon HeartGold"),
                Caught(Pidgey, "pidgey", File, shiny: false),
                Caught(Rattata, "rattata;f", File, phase: "1-3 Route 46")
            ];

            var rows = MilestoneRows.Build(Route30(RomVersion.SoulSilver), goal, caught);

            Assert.Equal(1, rows.Single(r => r.Species == Pidgey).Caught);
            Assert.Equal([("rattata;f", 1), ("rattata", 0)], rows.Where(r => r.Species == Rattata).Select(r => (r.Key, r.Caught)));
        }

        [Fact]
        public void Milestone_CompleteWhenNoRowIsTarget()
        {
            var goal = new GameFile
            {
                Id = File,
                Entries = [new GoalEntry(Caterpie, null, 1), new GoalEntry(Spinarak, null, 1), new GoalEntry(Metapod, null, 0)],
                Hidden = [new HiddenRow("34-land", "#167")]
            };
            var route30 = Route30(RomVersion.HeartGold);

            Assert.False(MilestoneRows.Build(route30, goal, []).All(r => !r.IsTarget));
            Assert.True(MilestoneRows.Build(route30, goal, [Caught(Caterpie, "caterpie", File)]).All(r => !r.IsTarget));
        }

        private const int Ekans = 23, Zubat = 41, Bellsprout = 69, Mareep = 179, Hoppip = 187, Wooper = 194, Azurill = 298;

        private static Milestone Route32SoulSilver => Hgss[RomVersion.SoulSilver].Single(m => m.Id == "36-land");

        // A live hunt's inputs at the hour, Repel level and lead; weekday and Repel steps do not change a share.
        private static (RateInputs, bool) Seen(int hour, int? repelLevel = null, Ability lead = Ability.None, bool live = true)
            => (new RateInputs(1, hour, repelLevel is null ? 0 : 100, repelLevel, (byte)lead, 20, false), live);

        [Fact]
        public void Shares_SoulSilverRoute32NightRepel6()
        {
            var shares = MilestoneRows.Shares(Route32SoulSilver, HgssRates.Compute, Seen(21, repelLevel: 6))!.Value;

            Assert.Equal(0.65, shares.Survive, 6);
            Assert.Equal(35.0 / 65, shares.Species[Wooper].Share, 6);
            Assert.Equal(20.0 / 65, shares.Species[Bellsprout].Share, 6);
            Assert.Equal(10.0 / 65, shares.Species[Mareep].Share, 6);
            Assert.Null(shares.Species[Wooper].Reason);
            Assert.Equal((0.0, "blocked by Repel"), shares.Species[Ekans]);
            Assert.Equal((0.0, "blocked by Repel"), shares.Species[Zubat]);
            Assert.Equal((0.0, "morning and day only"), shares.Species[Hoppip]);
        }

        [Fact]
        public void Shares_HeartGoldRoute30Morning_NightSpeciesSayNightOnly()
        {
            var shares = MilestoneRows.Shares(Route30(RomVersion.HeartGold), HgssRates.Compute, Seen(6))!.Value;

            Assert.Equal(0.5, shares.Species[Caterpie].Share, 6);
            Assert.Equal(0.4, shares.Species[Pidgey].Share, 6);
            Assert.Equal(0.1, shares.Species[Metapod].Share, 6);
            Assert.All(new[] { Rattata, Spinarak, Hoothoot }, s => Assert.Equal((0.0, "night only"), shares.Species[s]));
            Assert.Equal(1, shares.Survive, 6);
        }

        [Fact]
        public void Shares_NotLive_LastSeenHourNoRepelNoLead()
        {
            var shares = MilestoneRows.Shares(Route32SoulSilver, HgssRates.Compute, Seen(21, repelLevel: 6, lead: Ability.KeenEye, live: false))!.Value;

            var (expected, survive) = HgssRates.Compute(Route32SoulSilver, SlotCondition.Night, null, 0, 1, false);
            Assert.Equal(survive, shares.Survive, 6);
            Assert.All(expected, e => Assert.Equal(e.Value, shares.Species[e.Key].Share, 6));
            Assert.Null(shares.Species[Ekans].Reason);
        }

        [Fact]
        public void Shares_NeverSeen_TimedSlots_Null()
        {
            Assert.Null(MilestoneRows.Shares(Route32SoulSilver, HgssRates.Compute, null));
        }

        [Fact]
        public void Shares_NeverSeen_UntimedSlots_PlainShares()
        {
            const int Wurmple = 265, Zigzagoon = 263, Poochyena = 261;
            // Sapphire's Route 101 land, as RsRatesTests has it.
            var route101 = new Milestone([16], Modality.Land, true,
            [
                new(0, Wurmple, 2, 2, 20, SlotCondition.None), new(1, Zigzagoon, 2, 2, 20, SlotCondition.None),
                new(2, Wurmple, 2, 2, 10, SlotCondition.None), new(3, Wurmple, 3, 3, 10, SlotCondition.None),
                new(4, Zigzagoon, 3, 3, 10, SlotCondition.None), new(5, Zigzagoon, 3, 3, 10, SlotCondition.None),
                new(6, Wurmple, 3, 3, 5, SlotCondition.None), new(7, Zigzagoon, 3, 3, 5, SlotCondition.None),
                new(8, Poochyena, 2, 2, 4, SlotCondition.None), new(9, Poochyena, 2, 2, 4, SlotCondition.None),
                new(10, Poochyena, 3, 3, 1, SlotCondition.None), new(11, Poochyena, 3, 3, 1, SlotCondition.None)
            ], []);

            var shares = MilestoneRows.Shares(route101, RsRates.Compute, null)!.Value;

            Assert.Equal(1, shares.Survive, 6);
            Assert.Equal(0.45, shares.Species[Wurmple].Share, 6);
            Assert.Equal(0.45, shares.Species[Zigzagoon].Share, 6);
            Assert.Equal(0.10, shares.Species[Poochyena].Share, 6);
        }

        [Fact]
        public void Shares_NoRateRule_Null()
        {
            Assert.Null(MilestoneRows.Shares(Route32SoulSilver, null, Seen(21)));
        }

        [Fact]
        public void Shares_Headbutt_Null()
        {
            Assert.Null(MilestoneRows.Shares(Hgss[RomVersion.SoulSilver].Single(m => m.Id == "74-headbutt"), HgssRates.Compute, Seen(12)));
        }

        [Fact]
        public void Shares_SoundOnlySpecies_NeedsHoennSound()
        {
            Slot[] night = [new(0, Wooper, 6, 6, 100, SlotCondition.Night)];
            Slot[] sounds = [new(1, Azurill, 6, 6, 100, SlotCondition.HoennSound)];
            var milestone = new Milestone([32], Modality.Land, true, [.. night, .. sounds], []);

            var shares = MilestoneRows.Shares(milestone, HgssRates.Compute, Seen(21))!.Value;

            Assert.Equal((0.0, "needs Hoenn Sound"), shares.Species[Azurill]);
            Assert.Equal((1.0, (string?)null), shares.Species[Wooper]);
        }

        [Fact]
        public void Shares_FormRowsShareTheSpecies()
        {
            var shares = MilestoneRows.Shares(Hgss[RomVersion.SoulSilver].Single(m => m.Id == "315-land"), HgssRates.Compute, Seen(12))!.Value;

            Assert.Equal([Unown], shares.Species.Keys);
            Assert.Equal((1.0, (string?)null), shares.Species[Unown]);
        }

        [Fact]
        public void PutGoal_RejectsAnyFormAndFormEntriesForOneSpecies()
        {
            Assert.Null(PhaseMetadataController.GoalError([new GoalEntry(Rattata, "rattata;f", 2), new GoalEntry(Rattata, "rattata", 0), new GoalEntry(Pidgey, null, 3)]));
            Assert.NotNull(PhaseMetadataController.GoalError([new GoalEntry(Rattata, null, 2), new GoalEntry(Rattata, "rattata;f", 2)]));
            Assert.NotNull(PhaseMetadataController.GoalError([new GoalEntry(Pidgey, null, 3), new GoalEntry(Pidgey, null, 1)]));
            Assert.NotNull(PhaseMetadataController.GoalError([new GoalEntry(Pidgey, null, -1)]));
        }
    }
}
