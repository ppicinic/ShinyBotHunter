using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class GoalPresetsTests
    {
        private const int Pidgey = 16, Pidgeot = 18, Rattata = 19, Raichu = 26, Vulpix = 37, Zubat = 41, Golbat = 42, Crobat = 169;
        private const int Growlithe = 58, Onix = 95, Eevee = 133, Ledyba = 165, Aipom = 190, Wooper = 194, Unown = 201;
        private const int Zangoose = 335, Seviper = 336;

        // Production's PokemonMetadata HasFemaleForm, species 1 to 493 (read-only export, 2026-11-25).
        private static readonly HashSet<int> HasFemaleForm =
        [
            3, 12, 19, 20, 25, 26, 29, 30, 31, 41, 42, 44, 45, 64, 65, 84, 85, 97, 111, 112, 113, 115, 118, 119, 123, 124, 129, 130,
            133, 154, 165, 166, 178, 185, 186, 190, 194, 195, 198, 202, 203, 207, 208, 212, 214, 215, 217, 221, 224, 229, 232, 238,
            241, 242, 255, 256, 257, 267, 269, 272, 274, 275, 307, 308, 314, 315, 316, 317, 322, 323, 332, 350, 369, 380, 396, 397,
            398, 399, 400, 401, 402, 403, 404, 405, 407, 413, 415, 416, 417, 418, 419, 424, 440, 443, 444, 445, 449, 450, 453, 454,
            456, 457, 459, 460, 461, 464, 465, 473, 478, 488
        ];

        private static readonly Dictionary<int, string> Slugs = new()
        {
            [Pidgey] = "pidgey", [Rattata] = "rattata", [Zubat] = "zubat", [Golbat] = "golbat", [Ledyba] = "ledyba", [Wooper] = "wooper"
        };

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));
        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Rs =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "RS"));

        private static PokemonMetadataService Metadata()
        {
            var metadata = new PokemonMetadataService(null!);
            for (int id = 1; id <= 493; id++)
            {
                metadata.Seed(new PokemonMetadata
                {
                    Id = id,
                    PokemonName = $"Species {id}",
                    PokeApiName = Slugs.GetValueOrDefault(id, $"species-{id}"),
                    HasFemaleForm = HasFemaleForm.Contains(id)
                });
            }
            return metadata;
        }

        private static int AnyForm(IEnumerable<GoalEntry> entries, int species) => entries.Single(e => e.Species == species && e.FormCode is null).Amount;

        // TID 0, SID 0 and PID 0 give shiny value 0; PID 0x00080000 gives 8, not shiny.
        private static PokemonData Pokemon(int species, string formCode, bool shiny = true)
            => new PokemonData { Species = (ushort)species, FormCode = formCode, PID = shiny ? 0u : 0x00080000u };

        [Fact]
        public void HgssLivingDex_EverySpeciesAtLineSize()
        {
            var entries = GoalPresets.Entries(GoalPreset.LivingDex, RomVersion.SoulSilver, Hgss, Metadata());

            Assert.Equal(492, entries.Count(e => e.FormCode is null));
            Assert.Equal(Enumerable.Range(1, 493).Where(s => s != Unown), entries.Where(e => e.FormCode is null).Select(e => e.Species));
            var letters = entries.Where(e => e.Species == Unown).ToList();
            Assert.Equal(28, letters.Count);
            Assert.All(letters, e => Assert.Equal(1, e.Amount));
            Assert.Equal(["unown-a", "unown-z", "unown-exclamation", "unown-question"],
                new[] { letters[0], letters[25], letters[26], letters[27] }.Select(e => e.FormCode));
            Assert.Equal(3, AnyForm(entries, Pidgey));
            Assert.Equal(3, AnyForm(entries, Pidgeot));
            Assert.Equal(3, AnyForm(entries, Zubat));
            Assert.Equal(3, AnyForm(entries, Crobat));
            Assert.Equal(3, AnyForm(entries, Raichu));
            Assert.Equal(2, AnyForm(entries, Ledyba));
            Assert.Equal(2, AnyForm(entries, Onix));
            Assert.Equal(8, AnyForm(entries, Eevee));
            Assert.Equal(2, AnyForm(entries, Aipom));
        }

        [Fact]
        public void RsLivingDex_EverySpeciesAtLineSize()
        {
            var entries = GoalPresets.Entries(GoalPreset.LivingDex, RomVersion.Ruby, Rs, Metadata());

            Assert.Equal(385, entries.Count(e => e.FormCode is null));
            Assert.Equal(28, entries.Count(e => e.Species == Unown));
            Assert.Equal(386, entries.Max(e => e.Species));
            Assert.Equal(6, AnyForm(entries, Eevee));
            Assert.Equal(1, AnyForm(entries, Aipom));
            Assert.Equal(2, AnyForm(entries, Onix));
            Assert.Equal(3, AnyForm(entries, Zubat));
        }

        [Fact]
        public void HgssFemaleForms_FemaleAtFemaleFamilyCount_MaleAtZero()
        {
            var entries = GoalPresets.Entries(GoalPreset.FemaleForms, RomVersion.HeartGold, Hgss, Metadata());

            Assert.Equal(2 * HasFemaleForm.Count, entries.Count);
            Assert.Equal(new[] { new GoalEntry(Zubat, "zubat;f", 2), new GoalEntry(Zubat, "zubat", 0) }, entries.Where(e => e.Species == Zubat));
            Assert.Equal(new[] { new GoalEntry(Golbat, "golbat;f", 2), new GoalEntry(Golbat, "golbat", 0) }, entries.Where(e => e.Species == Golbat));
            Assert.Equal(2, entries.Single(e => e.FormCode == "wooper;f").Amount);
            Assert.Equal(2, entries.Single(e => e.FormCode == "ledyba;f").Amount);
            Assert.Equal(2, entries.Single(e => e.FormCode == "rattata;f").Amount);
            Assert.DoesNotContain(entries, e => e.Species == Crobat);
            Assert.DoesNotContain(entries, e => e.Species == Pidgey);
        }

        [Fact]
        public void RsFemaleForms_Refused()
        {
            Assert.Throws<ArgumentException>(() => GoalPresets.Entries(GoalPreset.FemaleForms, RomVersion.Sapphire, Rs, Metadata()));
        }

        [Fact]
        public void HgssVersionExclusives_OneEach()
        {
            var heartGold = GoalPresets.Entries(GoalPreset.VersionExclusives, RomVersion.HeartGold, Hgss, Metadata());
            var soulSilver = GoalPresets.Entries(GoalPreset.VersionExclusives, RomVersion.SoulSilver, Hgss, Metadata());

            Assert.Contains(new GoalEntry(Growlithe, null, 1), heartGold);
            Assert.DoesNotContain(heartGold, e => e.Species == Vulpix);
            Assert.Contains(new GoalEntry(Vulpix, null, 1), soulSilver);
            Assert.DoesNotContain(soulSilver, e => e.Species == Growlithe);
            Assert.All(heartGold.Concat(soulSilver), e => Assert.Equal(1, e.Amount));
            var heartGoldSlots = Hgss[RomVersion.HeartGold].SelectMany(m => m.Slots).Select(s => s.Species).ToHashSet();
            var soulSilverSlots = Hgss[RomVersion.SoulSilver].SelectMany(m => m.Slots).Select(s => s.Species).ToHashSet();
            Assert.All(heartGold, e => Assert.DoesNotContain(e.Species, soulSilverSlots));
            Assert.All(soulSilver, e => Assert.DoesNotContain(e.Species, heartGoldSlots));
        }

        [Fact]
        public void RsVersionExclusives_OneEach()
        {
            var ruby = GoalPresets.Entries(GoalPreset.VersionExclusives, RomVersion.Ruby, Rs, Metadata());
            var sapphire = GoalPresets.Entries(GoalPreset.VersionExclusives, RomVersion.Sapphire, Rs, Metadata());

            Assert.Contains(new GoalEntry(Zangoose, null, 1), ruby);
            Assert.DoesNotContain(ruby, e => e.Species == Seviper);
            Assert.Contains(new GoalEntry(Seviper, null, 1), sapphire);
            Assert.DoesNotContain(sapphire, e => e.Species == Zangoose);
            Assert.All(ruby.Concat(sapphire), e => Assert.Equal(1, e.Amount));
        }

        [Fact]
        public void HgssNone_IsEmpty()
        {
            Assert.Empty(GoalPresets.Entries(GoalPreset.None, RomVersion.HeartGold, Hgss, Metadata()));
        }
    }
}
