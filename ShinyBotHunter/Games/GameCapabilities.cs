namespace ShinyBotHunter.Games
{
    /// <summary>What the bot may try on a flavor, declared by its profile.</summary>
    public sealed record GameCapabilities(
        bool HasOverworldNavigation,   // map, position, facing, and a navigator part that walks to a Pokémon Center and back
        bool HasWildEncounters,        // the spin and run loops and a battle that can be fled
        bool HasTraining,              // overworld navigation: train modes and healing
        bool HasEggs,                  // Togepi and Primo egg events
        bool CanReadBoxes,             // PokemonStateReader.GetPokemonState
        bool HasSoftResetStarterHunt,  // RS wave hunt
        bool HasShinyStarterHunt,      // HGSS starter hunt
        bool HasWishmakerJirachi,      // RS Wishmaker Jirachi hunt over the Colosseum Bonus Disc
        bool HasChainedEncounter = false,  // Chained Encounter
        bool HasStaticEncounter = false,   // HGSS Static Encounter: the graph and pathfinding walk to the spot
        bool HasHeadbuttTrees = false,     // the Headbutt encounter type
        bool HasWildRunChecks = false);    // the RS checks and speed lines at a Wild Encounter's Run
}
