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
        // Always a flat array of per-roll totals -- the JS wrapper collapses multi-hit and
        // fixed-damage results before serialising, so this shape never varies by move.
        public int[] damageRolls { get; set; } = null!;
        public int minDamage { get; set; }
        public int maxDamage { get; set; }
        // Number of hits the calculation covered; 1 for everything except multi-hit moves.
        public int hits { get; set; } = 1;

        /// <summary>Mean roll, or 0 when the calculator produced nothing to average.</summary>
        public double AverageDamage => damageRolls is null || damageRolls.Length == 0 ? 0 : damageRolls.Average();

        public int CompareTo(DamageResult? other)
        {
            if(other is null)
            {
                return 1;
            }
            return (int)(this.AverageDamage - other.AverageDamage);
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
                // result.damage comes back in three different shapes and only one of them used to
                // survive the trip into C#:
                //   number     - fixed damage moves (Seismic Toss, Dragon Rage, Sonic Boom)
                //   number[]   - the usual 16 damage rolls
                //   number[][] - multi-hit moves (Fury Swipes, Bullet Seed, ...), one roll array per hit
                // Flattening here means C# only ever sees a plain array of totals.
                function normalizeDamage(damage) {
                    if (typeof damage === 'number') return [damage];
                    if (!Array.isArray(damage) || damage.length === 0) return [0];
                    if (!Array.isArray(damage[0])) return damage;

                    // Multi-hit: the damage for a given roll is the sum of that roll across every
                    // hit, which is what makes the total comparable to the defender's HP.
                    var len = damage[0].length;
                    for (var h = 1; h < damage.length; h++) {
                        if (damage[h].length < len) len = damage[h].length;
                    }
                    var totals = new Array(len);
                    for (var i = 0; i < len; i++) {
                        var sum = 0;
                        for (var j = 0; j < damage.length; j++) sum += damage[j][i];
                        totals[i] = sum;
                    }
                    return totals;
                }

                function calculateDamage(genNum, attackerName, defenderName, moveName, options) {
                    const gen = calc.Generations.get(genNum);

                    const attacker = new calc.Pokemon(gen, attackerName, options?.attackerOpts || {});
                    const defender = new calc.Pokemon(gen, defenderName, options?.defenderOpts || {});
                    const move = new calc.Move(gen, moveName);
                    const field = new calc.Field(options?.fieldOpts || {});

                    const result = calc.calculate(gen, attacker, defender, move, field);
                    const rolls = normalizeDamage(result.damage);
                    const multiHit = Array.isArray(result.damage) && Array.isArray(result.damage[0]);

                    return JSON.stringify({
                        description: result.desc(),
                        damageRolls: rolls,
                        // From the rolls rather than by index: multi-hit totals are only sorted if the
                        // per-hit arrays were, and Math.min/max does not care either way.
                        minDamage: Math.min.apply(null, rolls),
                        maxDamage: Math.max.apply(null, rolls),
                        hits: multiHit ? result.damage.length : 1
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
                    try
                    {
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
                                    def = attacker.Pokemon.EVDef,
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
                    }
                    catch (Exception ex)
                    {
                        // A move the calculator can't handle must not take down the run. Leaving this
                        // slot null drops that move from consideration; if every move fails the caller
                        // gets -1 and flees, which is recoverable.
                        Console.WriteLine($"[Damage] Skipping {moveMetadata.Name}: {ex.Message}");
                        continue;
                    }
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
                        var damage = ohkos[i]!.AverageDamage;
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
                    double damage = result[i]!.AverageDamage;
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
