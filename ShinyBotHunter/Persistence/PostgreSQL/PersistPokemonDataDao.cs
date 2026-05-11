using CmdScale.EntityFrameworkCore.TimescaleDB.Configuration.Hypertable;
using Microsoft.EntityFrameworkCore;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.ComponentModel.DataAnnotations.Schema;

namespace ShinyBotHunter.Persistence.PostgreSQL
{
    [Hypertable(nameof(Timestamp), ChunkTimeInterval = "1 day")]
    [Table("encounters")]
    [PrimaryKey("Id", "Timestamp")]
    public class PersistPokemonDataDao
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public int PID { get; set; }
        public short Species { get; set; }
        public byte Gender { get; set; }
        public string FormCode { get; set; } = null!;
        public string Name { get; set; } = null!;
        public short TID { get; set; }
        public short SID { get; set; }
        public byte Ability { get; set; }
        public int IVs { get; set; }
        public byte Nature { get; set; }
        public byte Level { get; set; }
        public short ShinyValue { get; set; }
        public bool IsShiny { get; set; }
        public GameFlavor Game { get; set; }
        public string Milestone { get; set; } = null!;
        public string GameFileName { get; set; } = null!;

        public PersistPokemonDataDao(PersistPokemonData pokemonData)
        {
            Id = Guid.NewGuid();
            PokemonData pokemon = pokemonData.PokemonData;
            Timestamp = pokemonData.DateTime;
            PID = (int)pokemon.PID;
            Species = (short)pokemon.Species;
            Gender = pokemon.Gender;
            FormCode = pokemon.FormCode;
            Name = pokemon.Name;
            TID = (short)pokemon.TID;
            SID = (short)pokemon.SID;
            Ability = pokemon.Ability;
            IVs = (int)pokemon.IVs;
            Nature = pokemon.Nature;
            Level = pokemon.Level;
            ShinyValue = (short)pokemon.ShinyValue;
            IsShiny = pokemon.IsShiny;
            Game = pokemonData.Game;
            Milestone = pokemonData.Phase;
            GameFileName = pokemonData.GameFileName;
        }

        public PersistPokemonDataDao()
        {
            Id = Guid.NewGuid();
        }
    }
}
