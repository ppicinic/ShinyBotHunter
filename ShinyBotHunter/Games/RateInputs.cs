using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Games
{
    // What the profile's rate rule takes from the hunt (FR-26), as the decision last read it: Weekday (0 Sunday) and Hour
    // (0 to 23) from the live clock on HGSS, null on RS, which has no clock; RepelSteps from the roamer save section on HGSS
    // (null while the save's section table is not loaded) or from the save variable on RS (FR-26a); and the lead's values
    // from the decision's party read. RepelLevel is the level of the first party member that is neither fainted nor an egg
    // (script_pokemon_util.c:117), only while RepelSteps is above 0.
    public record RateInputs(int? Weekday, int? Hour, int? RepelSteps, int? RepelLevel, byte LeadAbility, int LeadLevel, bool LeadIsEgg)
    {
        public static RateInputs Of(int? weekday, int? hour, int? repelSteps, PokemonState party)
        {
            PokemonData lead = party.PartyPokemon[0]!;
            int? repelLevel = repelSteps > 0
                ? party.PartyPokemon.Take(party.PartySize).FirstOrDefault(p => p!.HP > 0 && !p.IsEgg)?.Level
                : null;
            return new RateInputs(weekday, hour, repelSteps, repelLevel, lead.Ability, lead.Level, lead.IsEgg);
        }
    }
}
