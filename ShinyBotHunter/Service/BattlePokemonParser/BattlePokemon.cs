using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Service.BattlePokemonParser
{
    public class BattlePokemon
    {
        public ushort Species { get; set; }
        public ushort HP { get; set; }
        public byte Level { get; set; }
        public ushort MaxHP { get; set; }
        public byte Move1PP { get; set; }
        public byte Move2PP { get; set; }
        public byte Move3PP { get; set; }
        public byte Move4PP { get; set; }
        public uint PID { get; set; }
        public PokemonData Pokemon { get; set; } = null!;

        public byte[] MovePP
        {
            get
            {
                return [Move1PP, Move2PP, Move3PP, Move4PP];
            }
        }

        public Move[] Moves
        {
            get
            {
                var moves = Pokemon.Moves;
                var movePP = MovePP;
                for (int i = 0; i < moves.Length; i++)
                {
                    moves[i].PP = movePP[i];
                }
                return moves;
            }
        }
    }
}
