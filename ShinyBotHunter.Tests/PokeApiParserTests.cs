using System.Text.Json;
using ShinyBotHunter.Scripts;

namespace ShinyBotHunter.Tests
{
    public class PokeApiParserTests
    {
        private const string Still = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon";
        private const string BlackWhite = Still + "/versions/generation-v/black-white/animated";
        private const string Showdown = Still + "/other/showdown";

        private static PokemonSprite Urls(string? normal, string? shiny = null, string? female = null, string? shinyFemale = null)
            => new(normal!, shiny!, female!, shinyFemale!, null, null);

        // A sprite record in the shape the API returns: the still URLs, with the two animated sets nested.
        private static PokemonSprite Record(int id, PokemonSprite? blackWhite, PokemonSprite? showdown, string? stillFemale = null)
            => new($"{Still}/{id}.png", $"{Still}/shiny/{id}.png", stillFemale!, null!,
                new SpriteOther(showdown), new SpriteVersions(new SpriteGenV(new SpriteBlackWhite(blackWhite))));

        [Fact]
        public void Pikachu_TakesBlackWhite()
        {
            var own = Record(25, Urls($"{BlackWhite}/25.gif"), Urls($"{Showdown}/25.gif"));

            string? url = PokeApiParser.AnimatedSpriteUrl("pikachu", 25, own, null, shiny: false, female: false);

            Assert.Equal($"{BlackWhite}/25.gif", url);
        }

        [Fact]
        public void DeoxysAttack_TakesBlackWhite()
        {
            var own = Record(10001, Urls($"{BlackWhite}/10001.gif"), Urls($"{Showdown}/10001.gif"));

            string? url = PokeApiParser.AnimatedSpriteUrl("deoxys-attack", 386, own, null, shiny: false, female: false);

            Assert.Equal($"{BlackWhite}/10001.gif", url);
        }

        [Fact]
        public void Sprigatito_TakesShowdown()
        {
            var own = Record(906, Urls($"{BlackWhite}/906.gif"), Urls($"{Showdown}/906.gif"));

            string? url = PokeApiParser.AnimatedSpriteUrl("sprigatito", 906, own, null, shiny: false, female: false);

            Assert.Equal($"{Showdown}/906.gif", url);
        }

        [Fact]
        public void IronMoth_TakesBlackWhiteAsFill()
        {
            var own = Record(994, Urls($"{BlackWhite}/994.gif"), Urls(null));

            string? url = PokeApiParser.AnimatedSpriteUrl("iron-moth", 994, own, null, shiny: false, female: false);

            Assert.Equal($"{BlackWhite}/994.gif", url);
        }

        [Fact]
        public void FlabebeRed_TakesItsPokemon()
        {
            var own = Record(669, Urls(null), Urls(null));
            var owner = Record(669, Urls($"{BlackWhite}/669.gif"), Urls($"{Showdown}/669.gif"));

            string? url = PokeApiParser.AnimatedSpriteUrl("flabebe-red", 669, own, owner, shiny: false, female: false);

            Assert.Equal($"{Showdown}/669.gif", url);
        }

        [Theory]
        [InlineData("alcremie-ruby-cream-berry-sweet", 869, false, "https://play.pokemonshowdown.com/sprites/ani/alcremie-rubycream-berry.gif")]
        [InlineData("alcremie-ruby-cream-strawberry-sweet", 869, true, "https://play.pokemonshowdown.com/sprites/ani-shiny/alcremie-rubycream.gif")]
        [InlineData("furfrou-la-reine", 676, false, "https://play.pokemonshowdown.com/sprites/ani/furfrou-lareine.gif")]
        [InlineData("maushold-family-of-three", 925, false, "https://play.pokemonshowdown.com/sprites/ani/maushold.gif")]
        public void ShowdownName_MapsForms(string form, int speciesId, bool shiny, string expected)
        {
            var own = Record(speciesId, Urls(null), Urls(null));
            var owner = Record(speciesId,
                Urls($"{BlackWhite}/{speciesId}.gif", $"{BlackWhite}/shiny/{speciesId}.gif"),
                Urls($"{Showdown}/{speciesId}.gif", $"{Showdown}/shiny/{speciesId}.gif"));

            string? url = PokeApiParser.AnimatedSpriteUrl(form, speciesId, own, owner, shiny, female: false);

            Assert.Equal(expected, url);
        }

        [Fact]
        public void IronBoulder_GetsNone()
        {
            var own = Record(1022, Urls(null), Urls(null));

            string? url = PokeApiParser.AnimatedSpriteUrl("iron-boulder", 1022, own, null, shiny: false, female: false);

            Assert.Null(url);
        }

        [Fact]
        public void UnfezantFemale_TakesFemaleGif()
        {
            var own = Record(521,
                Urls($"{BlackWhite}/521.gif", female: $"{BlackWhite}/female/521.gif"),
                Urls($"{Showdown}/521.gif", female: $"{Showdown}/female/521.gif"),
                stillFemale: $"{Still}/female/521.png");

            string? url = PokeApiParser.AnimatedSpriteUrl("unfezant", 521, own, null, shiny: false, female: true);

            Assert.Equal($"{BlackWhite}/female/521.gif", url);
        }

        [Fact]
        public void ChanseyFemale_TakesOrdinary()
        {
            var own = Record(113, Urls($"{BlackWhite}/113.gif"), Urls($"{Showdown}/113.gif"), stillFemale: $"{Still}/113.png");

            string? url = PokeApiParser.AnimatedSpriteUrl("chansey", 113, own, null, shiny: false, female: true);

            Assert.Equal($"{BlackWhite}/113.gif", url);
        }

        [Fact]
        public void EeveeFemale_GetsNone()
        {
            var own = Record(133, Urls($"{BlackWhite}/133.gif"), Urls($"{Showdown}/133.gif"), stillFemale: $"{Still}/female/133.png");

            string? url = PokeApiParser.AnimatedSpriteUrl("eevee", 133, own, null, shiny: false, female: true);

            Assert.Null(url);
        }

        [Fact]
        public void WikidexClips_HasTheElevenForms()
        {
            var pecharunt = PokeApiParser.WikidexClips["pecharunt"];

            Assert.Equal(11, PokeApiParser.WikidexClips.Count);
            Assert.Equal("Pecharunt (abierto) EP.webm", pecharunt.Normal);
            Assert.Equal("Pecharunt (abierto) EP variocolor.webm", pecharunt.Shiny);
        }

        [Fact]
        public void SpriteRecords_ReadTheNestedSources()
        {
            const string json = """
                {
                  "front_default": "https://example/25.png",
                  "other": { "showdown": { "front_default": "https://example/showdown/25.gif" } },
                  "versions": { "generation-v": { "black-white": { "animated": { "front_default": "https://example/bw/25.gif" } } } }
                }
                """;

            // The parser reads with GetFromJsonAsync, which uses the web defaults.
            var sprite = JsonSerializer.Deserialize<PokemonSprite>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

            Assert.Equal("https://example/bw/25.gif", sprite.versions?.GenerationV?.BlackWhite?.animated?.front_default);
            Assert.Equal("https://example/showdown/25.gif", sprite.other?.showdown?.front_default);
        }
    }
}
