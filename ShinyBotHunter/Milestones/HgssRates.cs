using PKHeX.Core;

namespace ShinyBotHunter.Milestones
{
    // The expected species shares of an HGSS milestone and the share of slot rolls that become a battle, as
    // FieldSystem_GenerateRegularEncounter in pokeheartgold's src/field/encounter_check.c picks and cancels a wild Pokémon.
    public static class HgssRates
    {
        // condition: the time of day (Morning, Day or Night, evening counted as day) for land, surf, the rods and rock smash;
        // the tree kind for headbutt. repelLevel: null when the Repel counter is 0, otherwise the level of the first party
        // member that is neither fainted nor an egg. leadAbility, leadLevel, leadIsEgg: party slot 0, fainted or not.
        // The radio sounds are not read (FR-30), so sound slots are never taken.
        public static (IReadOnlyDictionary<int, double> Shares, double Survive) Compute(
            Milestone milestone, SlotCondition condition, int? repelLevel, byte leadAbility, int leadLevel, bool leadIsEgg)
        {
            Slot[] slots = Applicable(milestone, condition);
            // An egg lead reads as NUM_ABILITIES, which no ability rule matches (encounter_check.c:1380-1381).
            Ability ability = leadIsEgg ? Ability.None : (Ability)leadAbility;
            // Only the land and surf check sets the Repel flag (encounter_check.c:239-242); the others clear it (:1384).
            int? repel = milestone.Modality is Modality.Land or Modality.Surf ? repelLevel : null;

            double[] picks = Picks(slots, ability);
            var masses = slots.Select(s => s.Species).Distinct().ToDictionary(s => s, _ => 0.0);
            double survive = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                double mass = picks[i]
                    * Levels(slots[i]).Sum(l => l.Chance * Kept(l.Level, ability, leadLevel, repel));
                masses[slots[i].Species] += mass;
                survive += mass;
            }
            // A cancelled encounter is no encounter, not a reroll (encounter_check.c:918-926).
            return (masses.ToDictionary(m => m.Key, m => survive == 0 ? 0 : m.Value / survive), survive);
        }

        // One slot per index, in index order.
        private static Slot[] Applicable(Milestone milestone, SlotCondition condition) => (milestone.Modality switch
        {
            Modality.Land or Modality.Headbutt => milestone.Slots.Where(s => s.Condition == condition),
            Modality.Surf or Modality.RockSmash => milestone.Slots.Where(s => s.Condition == SlotCondition.None),
            // At night the night-fishing species takes its index (encounter_check.c:140-150).
            Modality.OldRod or Modality.GoodRod or Modality.SuperRod => milestone.Slots
                .Where(s => s.Condition == SlotCondition.None)
                .Select(s => condition == SlotCondition.Night
                    ? milestone.Slots.FirstOrDefault(n => n.Index == s.Index && n.Condition == SlotCondition.NightFishing) ?? s
                    : s),
            _ => throw new ArgumentOutOfRangeException(nameof(milestone), milestone.Modality, null)
        }).OrderBy(s => s.Index).ToArray();

        // The chance each slot is taken. Magnet Pull (Steel) or Static (Electric): half the time, a uniform pick among the slots
        // whose species has the type, unless none or all of them do (encounter_check.c:884-909, :1089-1110). The game reads
        // the base form's types, so the species alone indexes the table.
        private static double[] Picks(Slot[] slots, Ability ability)
        {
            double[] picks = slots.Select(s => s.Weight / 100.0).ToArray();
            byte? type = ability switch
            {
                Ability.MagnetPull => (byte)MoveType.Steel,
                Ability.Static => (byte)MoveType.Electric,
                _ => null
            };
            if (type is null)
                return picks;
            bool[] typed = slots.Select(s => PersonalTable.HGSS[s.Species].Type1 == type || PersonalTable.HGSS[s.Species].Type2 == type).ToArray();
            int count = typed.Count(t => t);
            if (count == 0 || count == slots.Length)
                return picks;
            return picks.Select((p, i) => 0.5 * p + (typed[i] ? 0.5 / count : 0)).ToArray();
        }

        // A land slot has one level; any other slot rolls uniformly within its range (encounter_check.c:742-764).
        private static IEnumerable<(int Level, double Chance)> Levels(Slot slot)
        {
            int range = slot.MaxLevel - slot.MinLevel + 1;
            return Enumerable.Range(slot.MinLevel, range).Select(level => (level, 1.0 / range));
        }

        // Keen Eye or Intimidate, then Repel (encounter_check.c:921-926). The lead-level-5 guard (:1153) cannot change the
        // result, since every wild level is at least 1.
        private static double Kept(int level, Ability ability, int leadLevel, int? repel)
        {
            if (repel > level)
                return 0;
            return ability is Ability.KeenEye or Ability.Intimidate && level <= leadLevel - 5 ? 0.5 : 1;
        }
    }
}
