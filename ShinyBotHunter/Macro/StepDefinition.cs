namespace ShinyBotHunter.Macro
{
    public class StepDefinition
    {
        public bool IsMacro { get; set; }
        public List<string> Action { get; set; }
        public string MacroName { get; set; }

        public StepDefinition(string macro)
        {
            IsMacro = true;
            Action = new List<string>();
            MacroName = macro;
        }
        public StepDefinition(List<string> actions)
        {
            IsMacro = false;
            Action = actions;
            MacroName = "";
        }
    }
}
