namespace ShinyBotHunter.Macro
{
    public class IntermediateMacroDefinition
    {
        public required string Name { get; set; }
        public required List<GameFlavor> MacroFlavors { get; set; }
        public required List<StepDefinition> Steps { get; set; }
        public required List<string> Dependencies { get; set; }
    }
}
