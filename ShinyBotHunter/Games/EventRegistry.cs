using ShinyBotHunter.Service;

namespace ShinyBotHunter.Games
{
    public static class EventRegistry
    {
        // The order is the order the dashboard lists the actions in.
        public static readonly IReadOnlyList<EventRegistration> All =
        [
            new("Launch Bot",
                new EventArgumentOptions { Name = "Launch Bot", RequiredParameters = ["GameName"], OptionalParameters = ["SoundOn"] },
                null, OwnsEmulators: true, (s, args, pos) => s.StartLaunchBotAsync(args, pos)),
            new("Launch Bot + Continue",
                new EventArgumentOptions { Name = "Launch Bot + Continue", RequiredParameters = ["GameName"], OptionalParameters = ["SoundOn"] },
                null, OwnsEmulators: true, (s, args, pos) => s.StartLaunchBotAndContinueAsync(args, pos)),
            new("Shiny Starter Hunt",
                // One entry for both starter hunts. HGSS keeps its own long-standing event and takes no
                // parameters; RS uses the generalized hunt, so everything below it appears only in RS mode.
                // Macros, timings and the save-menu id live in Configs/Events/RsStarterHunt.json, so the
                // only per-hunt choices are which starter to take and what to file the encounters under.
                new EventArgumentOptions
                {
                    Name = "Shiny Starter Hunt",
                    RequiredParameters = ["Mode"],
                    ConditionalParameters =
                    [
                        new ConditionalParameters { Name = "PhaseName", Required = true, Condition = new Condition { ConditionType = ConditionType.Eq, Name = "Mode", Value = "RS" } },
                        new ConditionalParameters { Name = "Emulators", Condition = new Condition { ConditionType = ConditionType.Eq, Name = "Mode", Value = "RS" } },
                    ],
                    LegCondition = new Condition { ConditionType = ConditionType.Eq, Name = "Mode", Value = "RS" },
                    LegParameters = new EventArgumentOptions
                    {
                        Name = "Starter",
                        RequiredParameters = ["GameName", "Starter"],
                        OptionalParameters = ["RecordingName", "RequireFemale"],
                    },
                },
                (nameof(GameCapabilities.HasSoftResetStarterHunt), c => c.HasSoftResetStarterHunt), OwnsEmulators: true,
                (s, args, pos) => s.StartShinyStarterHuntAsync(args, pos)),
            // Off the menu, which now reaches this through the unified action's HGSS mode, but
            // still accepted so a direct API call or a saved script keeps working.
            new("HGSS Shiny Starter Hunt",
                new EventArgumentOptions { Name = "HGSS Shiny Starter Hunt" },
                (nameof(GameCapabilities.HasShinyStarterHunt), c => c.HasShinyStarterHunt), OwnsEmulators: true,
                (s, _, pos) => s.StartHgssStarterHunt(pos)),
            new("Wild Encounter",
                EncounterParameters("Wild Encounter"),
                (nameof(GameCapabilities.HasWildEncounters), c => c.HasWildEncounters), OwnsEmulators: false,
                (s, args, pos) => s.StartWildEncounter(args, pos)),
            new("Chained Encounter",
                new EventArgumentOptions() { Name = "Chained Encounter", LegParameters = EncounterParameters("Leg") },
                (nameof(GameCapabilities.HasWildEncounters), c => c.HasWildEncounters), OwnsEmulators: false,
                (s, args, pos) => s.StartChainedEncounter(args, pos)),
            // Everything else the egg hunt needs -- macros, corridor, touch coordinates -- lives in
            // Configs/Events/TogepiEgg.json, so the only thing to pick here is where to log it.
            new("HGSS Togepi Egg",
                new EventArgumentOptions() { Name = "HGSS Togepi Egg", RequiredParameters = ["PhaseName"] },
                (nameof(GameCapabilities.HasEggs), c => c.HasEggs), OwnsEmulators: false,
                (s, args, pos) => s.StartTogepiEggAsync(args, pos)),
            new("Resume",
                new EventArgumentOptions() { Name = "Resume" },
                null, OwnsEmulators: false, (s, args, pos) => s.ResumeAsync(args, pos)),
        ];

        public static EventRegistration Get(string name)
            => All.FirstOrDefault(e => e.Name == name) ?? throw new ArgumentException($"{name} does not exist.");

        // The parameters of a single encounter. Also serves as the per-leg schema of a chain, so the
        // dashboard renders one set of fields per leg without a second definition to keep in sync.
        private static EventArgumentOptions EncounterParameters(string name) => new EventArgumentOptions()
        {
            Name = name,
            RequiredParameters = ["PhaseName", "TargetType", "EncounterType"],
            OptionalParameters = ["TrainType"],
            ConditionalParameters = [new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "EncounterType", Value = "Run" }, Name = "xRange" },
                new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "EncounterType", Value = "Run" }, Name = "yRange" },
                new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Neq, Name = "TrainType", Value = "None" }, Name = "TargetLevel" },
                new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "TrainType", Value = "Switch" }, Name = "SwitchSlot" }
            ]
        };
    }
}
