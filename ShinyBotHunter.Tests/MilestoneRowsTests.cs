using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.BotEvents;
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
        private const int Spinarak = 167, Unown = 201;
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
        private static CaughtPokemon Caught(string name, int species, string gameFileName, bool shiny = true) => new()
        {
            Pokemon = new PokemonData { Name = name, Species = (ushort)species, FormCode = name.ToLowerInvariant(), PID = shiny ? 0u : 0x00080000u },
            Phase = "1-4 Route 30",
            GameFileName = gameFileName
        };

        private static PhasePokemonInfo Row(string name, int species, int amount = 1)
            => new() { Name = name, SpeciesId = species, Amount = amount, FormCode = name.ToLowerInvariant() };

        // Every species of both versions' Route 30, in HeartGold's slot order then SoulSilver's new ones, and Growlithe, which
        // neither has there.
        private static PhaseInfo Route30Milestone(string? location = "34-land") => new()
        {
            Name = "1-4 Route 30",
            Location = location,
            Pokemon =
            [
                Row("Caterpie", Caterpie), Row("Metapod", Metapod), Row("Spinarak", Spinarak), Row("Ledyba", Ledyba), Row("Weedle", Weedle),
                Row("Kakuna", Kakuna), Row("Pidgey", Pidgey), Row("Rattata", Rattata), Row("Hoothoot", Hoothoot), Row("Growlithe", Growlithe)
            ]
        };

        private const int Growlithe = 58;

        [Fact]
        public void Counted_NoLocation_EveryRow()
        {
            Assert.Equal(20, ProductionPhases.Count);
            Assert.All(ProductionPhases, p => Assert.All(new[] { RomVersion.HeartGold, RomVersion.SoulSilver },
                v => Assert.Equal(p.Pokemon, MilestoneRows.Counted(p, Hgss, v))));
        }

        [Theory]
        [InlineData(RomVersion.HeartGold, new[] { Weedle, Kakuna, Ledyba })]
        [InlineData(RomVersion.SoulSilver, new[] { Caterpie, Metapod, Spinarak })]
        public void Counted_Route30_LeavesOutTheOtherVersionsSpecies(RomVersion version, int[] leftOut)
        {
            var phase = Route30Milestone();

            var counted = MilestoneRows.Counted(phase, Hgss, version);

            Assert.Equal(phase.Pokemon.Where(r => !leftOut.Contains(r.SpeciesId)), counted);
            Assert.Contains(counted, r => r.SpeciesId == Pidgey);
        }

        [Fact]
        public void Counted_VersionUnknown_EveryRow()
        {
            var phase = Route30Milestone();

            Assert.Equal(phase.Pokemon, MilestoneRows.Counted(phase, Hgss, null));
            Assert.Equal(phase.Pokemon, MilestoneRows.Counted(phase, null, RomVersion.HeartGold));
        }

        [Theory]
        [InlineData(RomVersion.HeartGold)]
        [InlineData(RomVersion.SoulSilver)]
        public void Counted_HandAddedRow_Kept(RomVersion version)
        {
            Assert.Contains(MilestoneRows.Counted(Route30Milestone(), Hgss, version), r => r.SpeciesId == Growlithe);
        }

        [Fact]
        public void Wanted_RowsStillShort()
        {
            PhasePokemonInfo[] rows = [Row("Pidgey", Pidgey, 1), Row("Rattata", Rattata, 0), Row("Hoothoot", Hoothoot, 1)];

            Assert.Equal([Pidgey, Hoothoot], MilestoneRows.Wanted(rows, []));
            Assert.Equal([Hoothoot], MilestoneRows.Wanted(rows, [Caught("Pidgey", Pidgey, File)]));
            Assert.Equal([Pidgey, Hoothoot], MilestoneRows.Wanted(rows, [Caught("Pidgey", Pidgey, File, shiny: false)]));
            // The caller passes the file's catches, as PhaseTarget and the picker do.
            var otherFile = new[] { Caught("Pidgey", Pidgey, "Pokemon HeartGold") };
            Assert.Equal([Pidgey, Hoothoot], MilestoneRows.Wanted(rows, otherFile.Where(c => c.GameFileName == File)));
        }

        [Fact]
        public void IsPhaseComplete_OtherVersionRowDoesNotHoldItBack()
        {
            var phase = new PhaseInfo { Name = "1-4 Route 30", Location = "34-land", Pokemon = [Row("Weedle", Weedle), Row("Pidgey", Pidgey)] };
            CaughtPokemon[] caught = [Caught("Pidgey", Pidgey, File)];

            Assert.True(OrchestratorEvent.IsPhaseComplete(MilestoneRows.Counted(phase, Hgss, RomVersion.HeartGold), caught));
            Assert.False(OrchestratorEvent.IsPhaseComplete(MilestoneRows.Counted(phase, Hgss, RomVersion.SoulSilver), caught));
        }

        [Fact]
        public void RowName_UnownLetterRowsNameTheLetter()
        {
            var metadata = Metadata();
            var letters = Enumerable.Range(0, 28).Select(i => metadata.RowName(Unown, metadata.GetFormCode(Unown, 0, i))).ToList();

            Assert.Equal(Enumerable.Range('A', 26).Select(c => $"Unown ({(char)c})").Concat(["Unown (!)", "Unown (?)"]), letters);
        }

        [Fact]
        public void RowName_AnyFormUnownIsThePlainName()
        {
            Assert.Equal("Unown", Metadata().RowName(Unown, null));
        }

        [Theory]
        [InlineData("unown")]
        [InlineData("unown-")]
        [InlineData("u")]
        [InlineData("pidgey")]
        public void RowName_UnownCodeWithoutALetterIsThePlainName(string formCode)
        {
            Assert.Equal("Unown", Metadata().RowName(Unown, formCode));
        }

        [Fact]
        public void RowName_OtherSpeciesArePlain()
        {
            var metadata = Metadata();

            Assert.Equal(["Pidgey", "Ledyba", "Ledyba", "Giratina"],
                new[] { metadata.RowName(Pidgey, null), metadata.RowName(Ledyba, "ledyba;f"), metadata.RowName(Ledyba, "ledyba"), metadata.RowName(487, "giratina-origin") });
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

        private const int Wurmple = 265, Zigzagoon = 263, Poochyena = 261;

        // Sapphire's Route 101 land, as RsRatesTests has it.
        private static readonly Milestone Route101 = new([16], Modality.Land, true,
        [
            new(0, Wurmple, 2, 2, 20, SlotCondition.None), new(1, Zigzagoon, 2, 2, 20, SlotCondition.None),
            new(2, Wurmple, 2, 2, 10, SlotCondition.None), new(3, Wurmple, 3, 3, 10, SlotCondition.None),
            new(4, Zigzagoon, 3, 3, 10, SlotCondition.None), new(5, Zigzagoon, 3, 3, 10, SlotCondition.None),
            new(6, Wurmple, 3, 3, 5, SlotCondition.None), new(7, Zigzagoon, 3, 3, 5, SlotCondition.None),
            new(8, Poochyena, 2, 2, 4, SlotCondition.None), new(9, Poochyena, 2, 2, 4, SlotCondition.None),
            new(10, Poochyena, 3, 3, 1, SlotCondition.None), new(11, Poochyena, 3, 3, 1, SlotCondition.None)
        ], []);

        // RS as the decision reads it (FR-26a): no clock, the Repel counter, and the lead's level as the Repel level.
        private static (RateInputs, bool) RsSeen(int repelLevel, bool live) => (new RateInputs(null, null, 100, repelLevel, 0, repelLevel, false), live);

        [Fact]
        public void Shares_NeverSeen_UntimedSlots_PlainShares()
        {
            var shares = MilestoneRows.Shares(Route101, RsRates.Compute, null)!.Value;

            Assert.Equal(1, shares.Survive, 6);
            Assert.Equal(0.45, shares.Species[Wurmple].Share, 6);
            Assert.Equal(0.45, shares.Species[Zigzagoon].Share, 6);
            Assert.Equal(0.10, shares.Species[Poochyena].Share, 6);
        }

        // A Repel at level 3 cancels the level 2 slots: 42 of 100 rolls survive.
        [Fact]
        public void Shares_RsLive_NoClock_RepelCounts()
        {
            var shares = MilestoneRows.Shares(Route101, RsRates.Compute, RsSeen(3, live: true))!.Value;

            Assert.Equal(0.42, shares.Survive, 6);
            Assert.Equal(15.0 / 42, shares.Species[Wurmple].Share, 6);
            Assert.Equal(25.0 / 42, shares.Species[Zigzagoon].Share, 6);
            Assert.Equal(2.0 / 42, shares.Species[Poochyena].Share, 6);
            Assert.All(shares.Species.Values, s => Assert.Null(s.Reason));
        }

        // Not live, the Repel is not counted and no hour is needed: the plain shares.
        [Fact]
        public void Shares_RsNotLive_NoClock_PlainShares()
        {
            var shares = MilestoneRows.Shares(Route101, RsRates.Compute, RsSeen(3, live: false))!.Value;

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

        private const int Staryu = 120, Corsola = 222;

        private static Milestone SuperRod38HeartGold => Hgss[RomVersion.HeartGold].Single(m => m.Id == "38-superrod");

        [Fact]
        public void Shares_SuperRodNight_ReplacedSpeciesSaysMorningAndDayOnly()
        {
            var shares = MilestoneRows.Shares(SuperRod38HeartGold, HgssRates.Compute, Seen(21))!.Value;

            Assert.Equal((0.0, "morning and day only"), shares.Species[Corsola]);
            Assert.Equal((0.3, (string?)null), (Math.Round(shares.Species[Staryu].Share, 6), shares.Species[Staryu].Reason));
        }

        [Fact]
        public void Shares_RodWithRepel_NeverBlockedByRepel()
        {
            var shares = MilestoneRows.Shares(SuperRod38HeartGold, HgssRates.Compute, Seen(21, repelLevel: 50))!.Value;

            Assert.Equal((0.0, "morning and day only"), shares.Species[Corsola]);
            Assert.Equal(1, shares.Survive, 6);
        }

        [Fact]
        public void Shares_FormRowsShareTheSpecies()
        {
            var shares = MilestoneRows.Shares(Hgss[RomVersion.SoulSilver].Single(m => m.Id == "315-land"), HgssRates.Compute, Seen(12))!.Value;

            Assert.Equal([Unown], shares.Species.Keys);
            Assert.Equal((1.0, (string?)null), shares.Species[Unown]);
        }
    }
}
