namespace ShinyBotHunter.Games
{
    /// <summary>What the bot may try on a flavor, declared by its profile.</summary>
    public sealed record GameCapabilities(
        bool HasOverworldNavigation,   // map, position, facing, pathfinding
        bool HasWildEncounters,        // run/spin loops and the battle orchestrator
        bool HasEggs,                  // Togepi egg event
        bool CanReadBoxes,             // PokemonStateReader.GetPokemonState
        bool HasSoftResetStarterHunt,  // RS wave hunt
        bool HasShinyStarterHunt,      // HGSS starter hunt
        bool HasWishmakerJirachi);     // RS Wishmaker Jirachi hunt over the Colosseum Bonus Disc
}
