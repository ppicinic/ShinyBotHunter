using System.Text.Json;
using System.Text.Json.Serialization;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Persistence
{
    // The dashboard appearance of this PC, saved as JSON outside the install folder (docs/specs/appearance-per-pc.md FR-1).
    public class AppearanceFile
    {
        // The controllers' options, so the file reads like the admin/appearance POST body; indented because a person may open it.
        // RespectNullableAnnotations turns a hand-edited null where the type has none into the FR-5 "cannot be read".
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            RespectNullableAnnotations = true,
            Converters = { new JsonStringEnumConverter() },
        };

        // Set once in the constructor; the class holds no mutable state, and its callers serialise access (AdminService's saveGate, start-up).
        private readonly string path;
        private readonly ILogger logger;

        public AppearanceFile(ILogger<AppearanceFile> logger)
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShinyBotHunter", "appearance.json"), logger)
        {
        }

        // The test seam for the file's location.
        internal AppearanceFile(string path, ILogger logger)
        {
            this.path = path;
            this.logger = logger;
        }

        // Null when there is no file (FR-8 copies only then); the defaults when the file cannot be read (FR-5), which is left as it is.
        public async Task<Appearance?> LoadAsync()
        {
            if (!File.Exists(path))
            {
                return null;
            }
            string text = await File.ReadAllTextAsync(path);
            Appearance? loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<Appearance>(text, Options);
            }
            catch (JsonException e)
            {
                logger.LogWarning(e, "Appearance file {Path} cannot be read; using the defaults", path);
                return new Appearance();
            }
            // RespectNullableAnnotations does not reach a top-level null or a dictionary value.
            if (loaded == null || loaded.Windows.ContainsValue(null!))
            {
                logger.LogWarning("Appearance file {Path} cannot be read: it is null or has a null window; using the defaults", path);
                return new Appearance();
            }
            return loaded;
        }

        public async Task SaveAsync(Appearance appearance)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(appearance, Options));
        }
    }
}
