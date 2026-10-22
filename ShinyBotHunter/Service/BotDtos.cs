using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Service
{
    public enum TargetType
    {
        PhaseTarget,
        ShinyTarget,
        NoTarget
    }

    public record EmulatorState(string Name, EventState EventState, GameFlavor GameFlavor);

    /// <summary>One occupied party slot, as offered to the dashboard for picking a switch-train battler.</summary>
    public record PartySlotInfo(int Slot, string Name, int Level, int Hp, int MaxHp, bool IsShiny);

    public class EventArgumentOptions
    {
        public string Name { get; set; } = null!;
        public string[]? RequiredParameters { get; set; }
        public string[]? OptionalParameters { get; set; }
        public ConditionalParameters[] ConditionalParameters { get; set; } = [];
        // Set on actions that take a list of legs instead of a single set of parameters. The
        // dashboard renders one copy of this schema per leg.
        public EventArgumentOptions? LegParameters { get; set; }
        // When set, the legs are only offered while this holds -- an action whose modes do not all
        // take a list. Null means always, which is every action that existed before modes.
        public Condition? LegCondition { get; set; }
    }

    public class ConditionalParameters
    {
        public Condition Condition { get; set; } = null!;
        public string Name { get; set; } = null!;
        public bool HideIfConditionNotMet { get; set; } = true;
        /// <summary>Must be filled before the action can start, while its condition holds.</summary>
        public bool Required { get; set; }
    }

    public class Condition
    {
        public ConditionType ConditionType { get; set; }
        public string Name { get; set; } = null!;
        public string Value { get; set; } = null!;
    }

    public enum ConditionType
    {
        Eq,
        Neq
    }

    /// <summary>Parameters of one encounter leg. A chained action posts a list of these.</summary>
    public class NewEventLegArguments
    {
        public string? PhaseName { get; set; }
        public string? TargetType { get; set; }
        public EncounterType? EncounterType { get; set; }
        public int? yRange { get; set; }
        public int? xRange { get; set; }
        public TrainType? TrainType { get; set; }
        public int? TargetLevel { get; set; }
        public int? SwitchSlot { get; set; }

        // Starter-hunt queue entries. Each entry picks its own ROM and label, the way chained
        // encounter legs each pick their own phase.
        public string? GameName { get; set; }
        public string? Starter { get; set; }
        public string? RecordingName { get; set; }
        public bool? RequireFemale { get; set; }
    }

    public class NewEventArguments
    {
        public string EventName { get; set; } = "";
        /// <summary>Which hunt an action that covers several game flavors should run.</summary>
        public string? Mode { get; set; }
        public string? GameName { get; set; }
        public string? PhaseName { get; set; }
        public string? TargetType { get; set; }
        public bool? SoundOn { get; set; }
        public EncounterType? EncounterType { get; set; }
        public int? yRange { get; set; }
        public int? xRange { get; set; }
        public TrainType? TrainType { get; set; }
        public int? TargetLevel { get; set; }
        public int? SwitchSlot { get; set; }
        /// <summary>Which starter to take, for the shiny starter hunt.</summary>
        public string? Starter { get; set; }
        /// <summary>How many emulators the shiny starter hunt should run at once.</summary>
        public int? Emulators { get; set; }
        /// <summary>
        /// What to file this hunt's encounters under. Deliberately not GameName, which already means
        /// which ROM to launch and renders as a ROM picker.
        /// </summary>
        public string? RecordingName { get; set; }
        // Only used by chained actions. The flat fields above stay in place for single encounters.
        public List<NewEventLegArguments>? Legs { get; set; }

        public NewEventLegArguments ToLeg()
        {
            return new NewEventLegArguments()
            {
                PhaseName = PhaseName,
                TargetType = TargetType,
                EncounterType = EncounterType,
                yRange = yRange,
                xRange = xRange,
                TrainType = TrainType,
                TargetLevel = TargetLevel,
                SwitchSlot = SwitchSlot,
            };
        }
    }
}
