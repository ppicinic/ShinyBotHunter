namespace ShinyBotHunter.Milestones
{
    // The expected species shares of a Ruby or Sapphire milestone and the share of slot rolls that become a battle, as
    // pokeruby's src/wild_encounter.c picks and cancels a wild Pokémon.
    public static class RsRates
    {
        // repelLevel: null when the Repel counter is 0, otherwise the level of the first party member that is neither fainted
        // nor an egg. condition, leadAbility, leadLevel and leadIsEgg are unused: there is one species list, and the file's
        // only lead abilities, Stench and Illuminate, change the encounter rate, not the species (FR-25b). Each slot's Weight
        // is already its modality's weight, so no weight table is needed.
        public static (IReadOnlyDictionary<int, double> Shares, double Survive) Compute(
            Milestone milestone, SlotCondition condition, int? repelLevel, byte leadAbility, int leadLevel, bool leadIsEgg)
        {
            // Land, surf and rock smash call GenerateWildMon(..., TRUE), which checks Repel; FishingWildEncounter never does
            // (wild_encounter.c:314-334, :472, :502, :532, :598).
            int? repel = milestone.Modality is Modality.OldRod or Modality.GoodRod or Modality.SuperRod ? null : repelLevel;

            var masses = milestone.Slots.Select(s => s.Species).Distinct().ToDictionary(s => s, _ => 0.0);
            double survive = 0;
            foreach (Slot slot in milestone.Slots)
            {
                // The level is uniform in the range (ChooseWildMonLevel, :235-252). A level below the Repel level is
                // cancelled; equal is kept (IsWildLevelAllowedByRepel, :685-708).
                int range = slot.MaxLevel - slot.MinLevel + 1;
                int kept = repel is int r ? Math.Clamp(slot.MaxLevel - Math.Max(r, slot.MinLevel) + 1, 0, range) : range;
                double mass = slot.Weight / 100.0 * kept / range;
                masses[slot.Species] += mass;
                survive += mass;
            }
            // A cancelled encounter is no encounter, not a reroll.
            return (masses.ToDictionary(m => m.Key, m => survive == 0 ? 0 : m.Value / survive), survive);
        }
    }
}
