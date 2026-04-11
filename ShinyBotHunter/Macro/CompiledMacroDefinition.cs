namespace ShinyBotHunter.Macro
{
    public class CompiledMacroDefinition
    {
        public required string Name { get; set; }
        public required List<string> Frames { get; set; }
        public required string Macro { get; set; }
    }
}
