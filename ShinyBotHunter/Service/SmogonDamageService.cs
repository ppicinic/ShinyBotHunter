using Microsoft.ClearScript.V8;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Service
{
    public class DamageResult : IComparable<DamageResult>
    {
        public string description { get; set; } = null!;
        public int[] damageRolls { get; set; } = null!; // Maps seamlessly to a JS array
        public int minDamage { get; set; }
        public int maxDamage { get; set; }

        public int CompareTo(DamageResult? other)
        {
            if(other is null)
            {
                return 1;
            }
            return (int)(this.damageRolls.Average() - other.damageRolls.Average());
        }
    }
    public class SmogonDamageService : IAsyncDisposable
    {
        private V8ScriptEngine engine;
        private PokemonMetadataService pokemonMetadataService;
        public SmogonDamageService(PokemonMetadataService pokemonMetadataService)
        {
            this.pokemonMetadataService = pokemonMetadataService;
            engine = new V8ScriptEngine();
            engine.Execute(@"
        var global = this;
        var window = this;
        
        // Mock Node.js module architecture
        var module = { exports: {} };
        var exports = module.exports;
        
        // Mock require to return the export system itself if modules look for internal paths
        function require(name) { 
            return exports; 
        }
    ");

            // 2. Load the DATA layer first
            string dataJs = File.ReadAllText(".\\Data\\smogon-data.min.js");
            engine.Execute(dataJs);

            // 3. Load the CALC engine second
            string calcJs = File.ReadAllText(".\\Data\\smogon-calc.min.js");
            engine.Execute(calcJs);

            // 4. Ensure the 'calc' object maps cleanly to our environment context
            engine.Execute("var calc = module.exports.calc || exports.calc || window.calc || this.calc;");

            // 5. Inject your wrapper function
            engine.Execute(@"
                function calculateDamage(genNum, attackerName, defenderName, moveName, options) {
                    const gen = calc.Generations.get(genNum);
                    
                    const attacker = new calc.Pokemon(gen, attackerName, options?.attackerOpts || {});
                    const defender = new calc.Pokemon(gen, defenderName, options?.defenderOpts || {});
                    const move = new calc.Move(gen, moveName);
                    const field = new calc.Field(options?.fieldOpts || {});

                    const result = calc.calculate(gen, attacker, defender, move, field);
                    
                    return JSON.stringify({
                        description: result.desc(),
                        damageRolls: result.damage,
                        minDamage: typeof result.damage === 'number' ? result.damage : result.damage[0],
                        maxDamage: typeof result.damage === 'number' ? result.damage : result.damage[result.damage.length - 1]
                    });
                }
            ");
        }

        // TODO typed options
        public DamageResult GetDamageResult(int generation, string attacker, string defender, string move, dynamic options)
        {
            try
            {
                string result = engine.Script.calculateDamage(generation, attacker, defender, move, options);
                //Console.WriteLine(result);
                //var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var toReturn = JsonConvert.DeserializeObject<DamageResult>(result)!;
                return toReturn;
            }catch(Exception e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
        }

        public int PickBestMove(PokemonBattleState state)
        {
            var moves = state.Moves;
            var attacker = state.ActivePokemon;
            var defender = state.EnemyPokemon;
            DamageResult?[] result = new DamageResult?[4];
            DamageResult?[] ohkos = new DamageResult?[4];
            bool hasOhko = false;
            for (int i = 0; i < 4; i++)
            {
                if (moves[i].Id > 0 && moves[i].PP > 0 && pokemonMetadataService.GetPokemonMoveMetadata(moves[i].Id).IsDamageMove)
                {
                    var moveMetadata = pokemonMetadataService.GetPokemonMoveMetadata(moves[i].Id);
                    result[i] = GetDamageResult(4, attacker.Pokemon.Name, defender.Pokemon.Name, moveMetadata.Name, new
                    {
                        attackerOpts = new
                        {
                            level = attacker.Level,
                            // TODO ability
                            nature = Constant.NATURE_NAMES[attacker.Pokemon.Nature],
                            ivs = new
                            {
                                hp = attacker.Pokemon.IVHp,
                                atk = attacker.Pokemon.IVAtk,
                                def = attacker.Pokemon.IVDef,
                                spa = attacker.Pokemon.IVSpAtk,
                                spd = attacker.Pokemon.IVSpDef,
                                spe = attacker.Pokemon.IVSpe
                            },
                            evs = new
                            {
                                hp = attacker.Pokemon.EVHP,
                                atk = attacker.Pokemon.EVAtk,
                                def = attacker.Pokemon.EVSpDef,
                                spa = attacker.Pokemon.EVSpAtk,
                                spd = attacker.Pokemon.EVSpDef,
                                spe = attacker.Pokemon.EVSpeed
                            }
                        },
                        defenderOpts = new
                        {
                            level = defender.Level,
                            nature = moveMetadata.IsPhysicalMove ? "Bold" : "Calm",
                            ivs = new { hp = 31, atk = 31, def = 31, spa = 31, spd = 31, spe = 31 },
                            evs = new { hp = 0, atk = 0, def = 0, spa = 0, spd = 0, spe = 0 }
                        }
                    });
                    if (result[i]!.minDamage > defender.HP)
                    {
                        ohkos[i] = result[i]!;
                        hasOhko = true;
                    }
                }
            }
            if (hasOhko)
            {
                double minDamage = double.MaxValue;
                int chosenMoveOhko = -1;
                for (int i = 0; i < 4; i++)
                {
                    if (ohkos[i] is not null)
                    {
                        Console.WriteLine($"OHKO - {i} - {ohkos[i]!.minDamage}");
                        var damage = ohkos[i]!.damageRolls.Average();
                        if(damage < minDamage)
                        {
                            minDamage = damage;
                            chosenMoveOhko = i;
                        }
                    }
                }
                return chosenMoveOhko;
            }
            double maxDamage = 0;
            int chosenMove = -1;
            for(int i = 0; i < 4; i++)
            {
                if (result[i] is not null)
                {
                    double damage = result[i]!.damageRolls.Average();
                    if(damage > maxDamage)
                    {
                        chosenMove = i;
                        maxDamage = damage;
                    }
                }
            }
            return chosenMove;
        }

        public ValueTask DisposeAsync()
        {
            engine.Dispose();
            return ValueTask.CompletedTask;
        }
    }


}
