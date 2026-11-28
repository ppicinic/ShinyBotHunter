using PKHeX.Core;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    public enum GoalPreset
    {
        None,
        LivingDex,
        FemaleForms,
        VersionExclusives
    }

    // FR-8's fills: each preset's default amounts over the whole dex of one version.
    public static class GoalPresets
    {
        private const ushort Unown = 201;
        private const int UnownLetters = 28;
        private const byte Male = 1, Female = 2;

        public static IReadOnlyList<GoalEntry> Entries(GoalPreset preset, RomVersion version,
            IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> milestones, PokemonMetadataService metadata)
        {
            var (tree, maxSpecies, other) = version switch
            {
                RomVersion.HeartGold => (EvolutionTree.GetEvolutionTree(EntityContext.Gen4), PersonalTable.HGSS.MaxSpeciesID, RomVersion.SoulSilver),
                RomVersion.SoulSilver => (EvolutionTree.GetEvolutionTree(EntityContext.Gen4), PersonalTable.HGSS.MaxSpeciesID, RomVersion.HeartGold),
                RomVersion.Ruby => (EvolutionTree.GetEvolutionTree(EntityContext.Gen3), PersonalTable.RS.MaxSpeciesID, RomVersion.Sapphire),
                RomVersion.Sapphire => (EvolutionTree.GetEvolutionTree(EntityContext.Gen3), PersonalTable.RS.MaxSpeciesID, RomVersion.Ruby),
                _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
            };
            var species = Enumerable.Range(1, maxSpecies).Select(s => (ushort)s);

            return preset switch
            {
                GoalPreset.None => [],

                // Unown is one of each letter, not its line's size (Decided).
                GoalPreset.LivingDex => species.SelectMany(s => s == Unown
                    ? Enumerable.Range(0, UnownLetters).Select(i => new GoalEntry(Unown, metadata.GetFormCode(Unown, 0, i), 1))
                    : [new GoalEntry(s, null, Family(tree, s, maxSpecies).Count)]).ToList(),

                // Ruby and Sapphire have no female preset (Decided).
                GoalPreset.FemaleForms when version is RomVersion.Ruby or RomVersion.Sapphire
                    => throw new ArgumentException($"{version} has no female forms preset"),

                // The female form at the number of its family that have a female form, the male form a row at 0.
                GoalPreset.FemaleForms => species.Where(s => metadata.GetPokemonMetadata(s).HasFemaleForm).SelectMany(s => new[]
                {
                    new GoalEntry(s, metadata.GetFormCode(s, Female, 0),
                        Family(tree, s, maxSpecies).Count(f => metadata.GetPokemonMetadata(f).HasFemaleForm)),
                    new GoalEntry(s, metadata.GetFormCode(s, Male, 0), 0)
                }).ToList(),

                GoalPreset.VersionExclusives => SlotSpecies(milestones[version]).Except(SlotSpecies(milestones[other]))
                    .Order().Select(s => new GoalEntry(s, null, 1)).ToList(),

                _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null)
            };
        }

        // The species' whole evolution family within the version's dex: back to the base, then every evolution forward.
        private static List<ushort> Family(EvolutionTree tree, ushort species, int maxSpecies)
        {
            var root = tree.GetBaseSpeciesForm(species, 0);
            var family = new HashSet<ushort> { root.Item1 };
            var next = new Queue<(ushort Species, byte Form)>([root]);
            while (next.TryDequeue(out var node))
            {
                foreach (var evolution in tree.Forward.GetEvolutions(node.Species, node.Form))
                {
                    if (family.Add(evolution.Item1))
                        next.Enqueue(evolution);
                }
            }
            return family.Where(s => s <= maxSpecies).ToList();
        }

        private static IEnumerable<int> SlotSpecies(IReadOnlyList<Milestone> milestones)
            => milestones.SelectMany(m => m.Slots).Select(s => s.Species).Distinct();
    }
}
