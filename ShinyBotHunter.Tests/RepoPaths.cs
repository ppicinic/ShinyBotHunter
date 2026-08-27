namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Locates the checkout from the test assembly. Config-driven code resolves paths like
    /// "./Macro/Configs" against the working directory, which at runtime is the app's project folder
    /// rather than its bin folder, so tests have to point themselves at the real files.
    /// </summary>
    public static class RepoPaths
    {
        public static string Root { get; } = Find();

        public static string AppProject => Path.Combine(Root, "ShinyBotHunter");

        private static string Find()
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            // Anchored on the app project rather than the solution file, whose extension varies by
            // SDK version (.sln vs .slnx).
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShinyBotHunter", "ShinyBotHunter.csproj")))
            {
                dir = dir.Parent;
            }
            if (dir is null)
            {
                throw new InvalidOperationException("Could not locate the repository root from the test assembly.");
            }
            return dir.FullName;
        }
    }
}
