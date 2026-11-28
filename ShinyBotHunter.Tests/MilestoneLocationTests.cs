using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class MilestoneLocationTests
    {
        private const int Caterpie = 10, Metapod = 11, Weedle = 13, Kakuna = 14, Pidgey = 16, Rattata = 19, Hoothoot = 163, Ledyba = 165;
        private const int Spinarak = 167, Unown = 201, Linoone = 264, Whismur = 293, Bidoof = 399, Buizel = 418;
        private const SlotCondition Morning = SlotCondition.Morning, Day = SlotCondition.Day, Night = SlotCondition.Night;

        // The species with a female form in the families on Route 30 (production's PokemonMetadata, read-only export, 2026-11-25).
        private static readonly HashSet<int> HasFemaleForm = [19, 20, 165, 166, 399, 400, 418, 419];

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));
        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Rs =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "RS"));

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

        // Production's 20 phase documents as stored (read-only export, 2026-11-25).
        private static List<PhaseInfo> ProductionPhases() => BsonSerializer.Deserialize<List<PhaseInfo>>(
            File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

        private static RomVersion[] Versions(RomVersion? version) => version is { } v ? [v] : [RomVersion.HeartGold, RomVersion.SoulSilver];

        private static List<(GoalEntry Entry, RomVersion? Version, SlotCondition[] Times)> Route30(GoalPreset preset, RomVersion? version = null)
            => PhaseInfoService.Seed(Hgss, "34-land", Versions(version), preset, Metadata());

        private static HashSet<int> SlotSpecies(RomVersion version) => Hgss[version].Single(m => m.Id == "34-land").Slots.Select(s => s.Species).ToHashSet();

        [Fact]
        public void StoredMilestones_WithoutNewFields_ReadAsToday()
        {
            var stored = BsonSerializer.Deserialize<List<BsonDocument>>(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

            var phases = ProductionPhases();

            Assert.Equal(20, phases.Count);
            Assert.All(stored, d => Assert.False(d.Contains("Location") || d.Contains("GameFiles") || d.Contains("Archived")));
            Assert.All(phases, p => Assert.Null(p.Location));
            Assert.All(phases, p => Assert.Empty(p.GameFiles));
            Assert.All(phases, p => Assert.False(p.Archived));
            Assert.Equal(stored.Select(d => d["Pokemon"]), phases.Select(p => p.ToBsonDocument()["Pokemon"]));
        }

        [Fact]
        public void Milestone_BsonRoundTrip_KeepsLocationFilesArchived()
        {
            var phase = ProductionPhases().Single(p => p.Name == "1-4 Route 30");
            phase.Location = "34-land";
            phase.GameFiles = ["Pokemon SoulSilver", "Pokemon HeartGold"];
            phase.Archived = true;

            var loaded = BsonSerializer.Deserialize<PhaseInfo>(phase.ToBsonDocument());

            Assert.Equal("34-land", loaded.Location);
            Assert.Equal(["Pokemon SoulSilver", "Pokemon HeartGold"], loaded.GameFiles);
            Assert.True(loaded.Archived);
        }

        [Fact]
        public void WithLocation_AttachThenDetach_ChangesNoRowOrAmount()
        {
            foreach (var phase in ProductionPhases())
            {
                var before = phase.ToBsonDocument();

                var attached = PhaseInfoService.WithLocation(phase, "34-land");
                var detached = PhaseInfoService.WithLocation(attached, null);

                var expected = before.DeepClone().AsBsonDocument;
                expected["Location"] = "34-land";
                Assert.Equal(expected, attached.ToBsonDocument());
                Assert.Equal(before, detached.ToBsonDocument());
                Assert.Equal(before, phase.ToBsonDocument());
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData(RomVersion.HeartGold)]
        [InlineData(RomVersion.SoulSilver)]
        public void Seed_Route30Grass_ByVersionFilter(RomVersion? version)
        {
            var heartGold = SlotSpecies(RomVersion.HeartGold);
            var soulSilver = SlotSpecies(RomVersion.SoulSilver);

            var rows = Route30(GoalPreset.None, version);

            var expected = version switch
            {
                RomVersion.HeartGold => heartGold,
                RomVersion.SoulSilver => soulSilver,
                _ => heartGold.Union(soulSilver).ToHashSet()
            };
            Assert.Equal(expected.Order(), rows.Select(r => r.Entry.Species).Order());
            // Marked against both versions whatever the filter.
            Assert.All(rows, r => Assert.Equal(
                !soulSilver.Contains(r.Entry.Species) ? RomVersion.HeartGold : !heartGold.Contains(r.Entry.Species) ? RomVersion.SoulSilver : (RomVersion?)null,
                r.Version));
            Assert.Equal([Caterpie, Metapod, Spinarak], heartGold.Except(soulSilver).Order());
            Assert.Equal([Weedle, Kakuna, Ledyba], soulSilver.Except(heartGold).Order());
            if (version is { } only)
            {
                var otherOnly = only == RomVersion.HeartGold ? soulSilver.Except(heartGold) : heartGold.Except(soulSilver);
                Assert.DoesNotContain(rows, r => otherOnly.Contains(r.Entry.Species));
            }
        }

        [Fact]
        public void Seed_Route30Grass_InSlotOrder_FirstVersionThenNewOnes()
        {
            Assert.Equal([Pidgey, Caterpie, Metapod, Spinarak, Rattata, Hoothoot, Whismur, Linoone, Buizel, Bidoof, Ledyba, Weedle, Kakuna],
                Route30(GoalPreset.None).Select(r => r.Entry.Species));
        }

        [Fact]
        public void Seed_Route30Grass_Times()
        {
            var rows = Route30(GoalPreset.None);
            SlotCondition[] TimesOf(int species) => rows.Single(r => r.Entry.Species == species).Times;

            Assert.Equal([Night], TimesOf(Hoothoot));
            Assert.Equal([Morning, Day], TimesOf(Pidgey));
            Assert.Equal([Morning], TimesOf(Ledyba));
            Assert.Equal([Morning, Day, Night], TimesOf(Whismur));
        }

        [Fact]
        public void Seed_Route30Grass_None_AllZero()
        {
            var rows = Route30(GoalPreset.None);

            Assert.All(rows, r => Assert.Equal(0, r.Entry.Amount));
            Assert.All(rows, r => Assert.Null(r.Entry.FormCode));
            Assert.Equal(rows.Count, rows.Select(r => r.Entry.Species).Distinct().Count());
        }

        [Fact]
        public void Seed_Route30Grass_LivingDex_LineSizes()
        {
            var rows = Route30(GoalPreset.LivingDex);
            int AmountOf(int species) => rows.Single(r => r.Entry.Species == species && r.Entry.FormCode is null).Entry.Amount;

            Assert.Equal(3, AmountOf(Pidgey));
            Assert.Equal(2, AmountOf(Rattata));
            Assert.Equal(3, AmountOf(Caterpie));
            Assert.Equal(2, AmountOf(Hoothoot));
            Assert.Equal(2, AmountOf(Ledyba));
        }

        [Fact]
        public void Seed_Route30Grass_FemaleForms_FemaleAtFamilyCount_MaleAtZero()
        {
            var rows = Route30(GoalPreset.FemaleForms);
            List<GoalEntry> EntriesOf(int species) => rows.Where(r => r.Entry.Species == species).Select(r => r.Entry).ToList();

            Assert.Equal([new GoalEntry(Rattata, "rattata;f", 2), new GoalEntry(Rattata, "rattata", 0)], EntriesOf(Rattata));
            Assert.Equal([new GoalEntry(Ledyba, "ledyba;f", 2), new GoalEntry(Ledyba, "ledyba", 0)], EntriesOf(Ledyba));
            Assert.Equal([new GoalEntry(Pidgey, null, 0)], EntriesOf(Pidgey));
            Assert.Equal([new GoalEntry(Hoothoot, null, 0)], EntriesOf(Hoothoot));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(RomVersion.HeartGold)]
        [InlineData(RomVersion.SoulSilver)]
        public void Seed_Route30Grass_VersionExclusives_ExclusivesAtOne(RomVersion? version)
        {
            var exclusives = Versions(version).SelectMany(v => GoalPresets.Entries(GoalPreset.VersionExclusives, v, Hgss, Metadata()))
                .Select(e => e.Species).ToHashSet();

            var rows = Route30(GoalPreset.VersionExclusives, version);

            Assert.All(rows, r => Assert.Equal(exclusives.Contains(r.Entry.Species) ? 1 : 0, r.Entry.Amount));
            Assert.Contains(rows, r => r.Entry.Amount == 1);
            Assert.Contains(rows, r => r.Entry.Amount == 0);
        }

        [Fact]
        public void Seed_RuinsOfAlph_LivingDex_28UnownRows()
        {
            var rows = PhaseInfoService.Seed(Hgss, "315-land", Versions(null), GoalPreset.LivingDex, Metadata());

            Assert.Equal(28, rows.Count);
            Assert.All(rows, r => Assert.Equal(Unown, r.Entry.Species));
            Assert.All(rows, r => Assert.Equal(1, r.Entry.Amount));
            Assert.Equal(28, rows.Select(r => r.Entry.FormCode).Distinct().Count());
        }

        [Fact]
        public void Seed_Rs_FemaleForms_Throws()
        {
            var land = Rs[RomVersion.Ruby].First(m => m.Modality == Modality.Land).Id;

            Assert.Throws<ArgumentException>(() => PhaseInfoService.Seed(Rs, land, [RomVersion.Ruby, RomVersion.Sapphire], GoalPreset.FemaleForms, Metadata()));
        }

        [Fact]
        public void Times_Rs_AllDay()
        {
            var land = Rs[RomVersion.Ruby].First(m => m.Modality == Modality.Land);

            Assert.All(land.Slots, s => Assert.Equal([Morning, Day, Night], PhaseInfoService.Times([land], s.Species)));
        }

        [Fact]
        public void Times_NoneSlotUnderANightFishingSlot_MorningAndDayOnly()
        {
            var rod = new Milestone([1], Modality.GoodRod, false,
            [
                new Slot(0, 129, 10, 10, 60, SlotCondition.None),
                new Slot(1, 118, 10, 10, 40, SlotCondition.None),
                new Slot(1, 98, 10, 10, 40, SlotCondition.NightFishing)
            ], []);

            Assert.Equal([Morning, Day, Night], PhaseInfoService.Times([rod], 129));
            Assert.Equal([Morning, Day], PhaseInfoService.Times([rod], 118));
            Assert.Equal([Night], PhaseInfoService.Times([rod], 98));
            Assert.Empty(PhaseInfoService.Times([rod], Pidgey));
        }

        [Theory]
        [InlineData(SlotCondition.Morning)]
        [InlineData(SlotCondition.Day)]
        [InlineData(SlotCondition.Night)]
        public void HiddenUntil_Route30_AtMorningDayNight(SlotCondition now)
        {
            var metadata = Metadata();
            var milestone = Hgss[RomVersion.SoulSilver].Single(m => m.Id == "34-land");
            // SoulSilver's Route 30 rows, plus Caterpie, a row added by hand that is in no SoulSilver slot.
            var rows = milestone.Slots.Select(s => s.Species).Append(Caterpie).Distinct()
                .Select(s => (Name: metadata.RowName(s, null), Times: PhaseInfoService.Times([milestone], s))).ToList();

            var hidden = PhaseInfoService.HiddenUntil(rows, now);

            Dictionary<SlotCondition, List<string>> expected = now switch
            {
                SlotCondition.Morning => new() { [Night] = ["Rattata", "Hoothoot"] },
                SlotCondition.Day => new() { [Night] = ["Rattata", "Hoothoot"], [Morning] = ["Ledyba"] },
                _ => new() { [Morning] = ["Ledyba", "Weedle", "Kakuna", "Pidgey"] }
            };
            Assert.Equal(expected.Keys, hidden.Keys);
            Assert.All(expected, e => Assert.Equal(e.Value.Order(), hidden[e.Key].Order()));
        }

        [Fact]
        public void HiddenUntil_TimeNotKnown_Empty()
        {
            Assert.Empty(PhaseInfoService.HiddenUntil([("Hoothoot", [Night])], null));
        }

        [Fact]
        public void HiddenUntil_FormRowsOfOneSpecies_NameOnce()
        {
            Assert.Equal(["Rattata"], PhaseInfoService.HiddenUntil([("Rattata", [Night]), ("Rattata", [Night])], Day)[Night]);
        }

        // The card's Route 32 rows as PhaseMetadataController.LocationRows builds them, for a milestone made from game data at
        // "Both versions": read for a file of this version, or, with null, the editor's read over both versions' slots.
        private static (List<string> HiddenNow, Dictionary<SlotCondition, List<string>> HiddenUntil) Route32Card(RomVersion? version, SlotCondition now)
        {
            var metadata = Metadata();
            var at = PhaseInfoService.At(Hgss, "36-land");
            IEnumerable<Milestone> scope = version is { } v ? [at[v]] : at.Values;
            var rows = PhaseInfoService.Seed(Hgss, "36-land", Versions(null), GoalPreset.None, metadata)
                .Select(r => (Name: metadata.RowName(r.Entry.Species, null), Only: r.Version, Times: PhaseInfoService.Times(scope, r.Entry.Species))).ToList();
            return (rows.Where(r => PhaseInfoService.HiddenNow(r.Only, version, r.Times, now)).Select(r => r.Name).ToList(),
                PhaseInfoService.HiddenUntil(rows.Select(r => (r.Name, r.Times)), now));
        }

        [Theory]
        [InlineData(SlotCondition.Morning)]
        [InlineData(SlotCondition.Day)]
        [InlineData(SlotCondition.Night)]
        public void Route32Card_HeartGoldFile_SoulSilversEkansHiddenAndNeverComesBack(SlotCondition now)
        {
            // Today's rows hidden by the time of day, the same on the editor's read and on each version's file.
            (List<string> HiddenNow, Dictionary<SlotCondition, List<string>> HiddenUntil) byTime = now switch
            {
                SlotCondition.Morning => (["Wooper"], new() { [Night] = ["Wooper"] }),
                SlotCondition.Day => (["Zubat", "Wooper"], new() { [Night] = ["Zubat", "Wooper"] }),
                _ => (["Hoppip"], new() { [Morning] = ["Hoppip"] })
            };
            // SoulSilver's Route 32 has Rattata in the morning and day only.
            (List<string> HiddenNow, Dictionary<SlotCondition, List<string>> HiddenUntil) soulSilver = now == Night
                ? (["Rattata", "Hoppip"], new() { [Morning] = ["Rattata", "Hoppip"] })
                : byTime;

            var heartGold = Route32Card(RomVersion.HeartGold, now);
            var editor = Route32Card(null, now);
            var onSoulSilver = Route32Card(RomVersion.SoulSilver, now);

            Assert.Equal([.. byTime.HiddenNow, "Ekans"], heartGold.HiddenNow);
            Assert.Equal(byTime.HiddenUntil, heartGold.HiddenUntil);
            Assert.Equal(byTime.HiddenNow, editor.HiddenNow);
            Assert.Equal(byTime.HiddenUntil, editor.HiddenUntil);
            Assert.Equal(soulSilver.HiddenNow, onSoulSilver.HiddenNow);
            Assert.Equal(soulSilver.HiddenUntil, onSoulSilver.HiddenUntil);
        }

        [Fact]
        public void HiddenNow_LocationNotOnTheFilesVersion_OtherVersionRowShown()
        {
            Assert.False(PhaseInfoService.HiddenNow(RomVersion.SoulSilver, null, [], Day));
        }
    }
}
