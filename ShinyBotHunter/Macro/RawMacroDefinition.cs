namespace ShinyBotHunter.Macro
{
    public class RawMacroDefinition
    {
        public string Name { get; set; } = "";
        public object Flavor { get; set; } = null!;
        public List<object> Steps { get; set; } = [];

    }
}
