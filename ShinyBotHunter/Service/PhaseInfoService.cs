using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using System.Text.Json;

namespace ShinyBotHunter.Service
{
    public class PhaseInfoService
    {
        public record GamePhase(GameFlavor game, string phase, string? gameFileName)
        {
            // FR-39c: a stored subscription has no such element and loads as a fixed card; a fixed card is saved without it.
            [BsonIgnoreIfDefault] public bool followsHunt { get; init; }
        }
        // Set once in the constructor; thread-safe for concurrent use.
        private PokemonMetadataService metadataService;
        // Guards phaseInfos and gamePhases after startup. Held only around dictionary reads, writes and the GetAllPhases copy, never across an await.
        private readonly object phasesLock = new();
        // Filled by InitAsync before the host serves requests or starts bots (single-threaded by construction); after that, written by AddOrUpdatePhaseInfoAsync and SetLocationAsync (dashboard requests) and read by requests, bot loops and the notification pipeline, only under phasesLock.
        private readonly Dictionary<GamePhase, PhaseInfo> phaseInfos = new Dictionary<GamePhase, PhaseInfo>();
        // Same as phaseInfos: filled by InitAsync before the host starts, then read and written only under phasesLock. A name is added here only in the same critical section that adds its phaseInfos entry.
        private readonly Dictionary<GameFlavor, List<string>> gamePhases = new Dictionary<GameFlavor, List<string>>();
        // The rate inputs each game file's hunt last saw, and whether its loop still runs (FR-28). Written by the bot loops
        // (RecordLive after each decision, EndLive when the loop ends), read by Seen on request threads; only
        // under phasesLock, never across an await. Memory only: empty after a restart (FR-28).
        private readonly Dictionary<string, (RateInputs Inputs, bool Live)> rates = new();
        // Set once in the constructor; thread-safe for concurrent use.
        private IMongoCollection<PhaseInfo> phaseInfoCollection;

        public PhaseInfoService(PokemonMetadataService metadataService, MongoDbConnection mongoDbConnection)
        {
            this.metadataService = metadataService;
            phaseInfoCollection = mongoDbConnection.GetDatabase().GetCollection<PhaseInfo>("PhaseInfos");
        }

        public async Task InitAsync()
        {
            IEnumerable<string> files = Directory.EnumerateFiles("./Phases/", "*.*", SearchOption.AllDirectories);
            List<PhaseInfo> phaseInfosToAdd = new List<PhaseInfo>();
            var phaseInfosDb = await phaseInfoCollection.Find(Builders<PhaseInfo>.Filter.Empty).ToListAsync();
            foreach (var file in files)
            {
                string json = File.ReadAllText(file);
                var phaseInfo = JsonSerializer.Deserialize<PhaseInfo>(json);
                phaseInfo!.IsStatic = true;
                if(phaseInfosDb.Where(x => x.Name == phaseInfo!.Name && x.GameName == phaseInfo.GameName).Count() == 0)
                {
                    phaseInfo.Id = Guid.NewGuid();
                    phaseInfosToAdd.Add(phaseInfo);
                }
            }
            if (phaseInfosToAdd.Count() > 0)
            {
                await phaseInfoCollection.InsertManyAsync(phaseInfosToAdd);
            }
            phaseInfosDb = await phaseInfoCollection.Find(Builders<PhaseInfo>.Filter.Empty).ToListAsync();
            foreach (var phaseInfo in phaseInfosDb)
            {
                
                if(phaseInfo is not null)
                {
                    Normalize(phaseInfo, metadataService);
                    phaseInfos.Add(new GamePhase(phaseInfo.Game, phaseInfo.Name, null), phaseInfo);
                    if (!gamePhases.ContainsKey(phaseInfo.Game))
                    {
                        gamePhases.Add(phaseInfo.Game, new List<string>());
                    }
                    gamePhases[phaseInfo.Game].Add(phaseInfo.Name);
                }
            }
        }

        public async Task<PhaseInfo> AddOrUpdatePhaseInfoAsync(PhaseInfo phaseInfo)
        {
            var builder = Builders<PhaseInfo>.Filter;
            var filter = builder.Eq(x => x.Name, phaseInfo.Name) & builder.Eq(x => x.GameName, phaseInfo.GameName);
            var phaseInfosDb = await phaseInfoCollection.Find(filter).FirstOrDefaultAsync();
            if (phaseInfosDb is null)
            {
                phaseInfo.Id = Guid.NewGuid();
                await phaseInfoCollection.InsertOneAsync(phaseInfo);
            }
            else
            {
                phaseInfo.Id = phaseInfosDb.Id;
                await phaseInfoCollection.ReplaceOneAsync(Builders<PhaseInfo>.Filter.Eq(x => x.Id, phaseInfo.Id), phaseInfo);
            }
            Normalize(phaseInfo, metadataService);
            lock (phasesLock)
            {
                var key = new GamePhase(phaseInfo.Game, phaseInfo.Name, null);
                if (phaseInfos.ContainsKey(key))
                {
                    phaseInfos[key] = phaseInfo;
                }
                else 
                {
                    phaseInfos.Add(key, phaseInfo);
                    if (!gamePhases.ContainsKey(phaseInfo.Game))
                    {
                        gamePhases.Add(phaseInfo.Game, new List<string>());
                    }
                    gamePhases[phaseInfo.Game].Add(phaseInfo.Name);
                }
            }
            return phaseInfo;
        }

        public PhaseInfo? GetPhaseInfo(GameFlavor game, string phase)
        {
            PhaseInfo? info;
            lock (phasesLock)
            {
                phaseInfos.TryGetValue(new GamePhase(game, phase, null), out info);
            }
            return info;
        }

        // Writes only Location, never a row or an amount; false for an unknown milestone.
        public async Task<bool> SetLocationAsync(GameFlavor game, string name, string? location)
        {
            var key = new GamePhase(game, name, null);
            if (GetPhaseInfo(game, name) is null)
            {
                return false;
            }
            var builder = Builders<PhaseInfo>.Filter;
            await phaseInfoCollection.UpdateOneAsync(builder.Eq(x => x.Name, name) & builder.Eq(x => x.GameName, game.ToString()),
                Builders<PhaseInfo>.Update.Set(x => x.Location, location));
            lock (phasesLock)
            {
                // Re-read under the lock, so a save that landed during the write keeps its rows.
                phaseInfos[key] = WithLocation(phaseInfos[key], location);
            }
            return true;
        }

        // A copy with Location replaced; the cached instance is shared with concurrent readers, so it is never written in place.
        internal static PhaseInfo WithLocation(PhaseInfo phaseInfo, string? location) => new()
        {
            GameName = phaseInfo.GameName,
            Name = phaseInfo.Name,
            Pokemon = phaseInfo.Pokemon,
            Id = phaseInfo.Id,
            IsStatic = phaseInfo.IsStatic,
            Location = location,
            GameFiles = phaseInfo.GameFiles,
            Archived = phaseInfo.Archived
        };

        public Dictionary<GameFlavor, PhaseInfo[]> GetAllPhases(IEnumerable<GameFlavor> games)
        {
            Dictionary<GameFlavor, PhaseInfo[]> result = new Dictionary<GameFlavor, PhaseInfo[]>();
            lock (phasesLock)
            {
                foreach (GameFlavor game in games)
                {
                    List<PhaseInfo> toReturn = new List<PhaseInfo>();
                    // A flavor with no phase JSON yet is normal, not an error — a newly added flavor has
                    // none until its phases are authored, and this is called with every enum member.
                    if (gamePhases.TryGetValue(game, out var phaseNames))
                    {
                        foreach (var phaseName in phaseNames)
                        {
                            toReturn.Add(phaseInfos[new GamePhase(game, phaseName, null)]);
                        }
                    }
                    result.Add(game, toReturn.ToArray());
                }
            }
            return result;
        }

        public void RecordLive(string gameFileName, RateInputs inputs)
        {
            lock (phasesLock)
            {
                rates[gameFileName] = (inputs, true);
            }
        }

        // Keeps the last inputs; a file never recorded stays without an entry.
        public void EndLive(string gameFileName)
        {
            lock (phasesLock)
            {
                if (rates.TryGetValue(gameFileName, out var seen))
                {
                    rates[gameFileName] = (seen.Inputs, false);
                }
            }
        }

        public (RateInputs Inputs, bool Live)? Seen(string gameFileName)
        {
            lock (phasesLock)
            {
                return rates.TryGetValue(gameFileName, out var seen) ? seen : null;
            }
        }

        private static readonly SlotCondition[] DayTimes = [SlotCondition.Morning, SlotCondition.Day, SlotCondition.Night];

        // The game's milestones with this id, per version that has one; empty for an id the game does not have.
        internal static Dictionary<RomVersion, Milestone> At(IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> milestones, string locationId)
            => milestones.Where(v => v.Value.Any(m => m.Id == locationId)).ToDictionary(v => v.Key, v => v.Value.Single(m => m.Id == locationId));

        // The one version of the game's two that has the species at this location; null when both have it.
        internal static RomVersion? OnlyVersion(IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> milestones, Dictionary<RomVersion, Milestone> at, int species)
        {
            var have = milestones.Keys.Where(v => at.TryGetValue(v, out var m) && m.Slots.Any(s => s.Species == species)).ToList();
            return have.Count == 1 ? have[0] : null;
        }

        // When the species can be met at these milestones, in the order Morning, Day, Night. At night a None slot whose index has a
        // NightFishing slot is replaced by it, as HgssRates and MilestoneRows.Reason treat it; sound and tree slots are all day.
        internal static SlotCondition[] Times(IEnumerable<Milestone> milestones, int species)
            => DayTimes.Where(t => milestones.Any(m => m.Slots.Any(s => s.Species == species && s.Condition switch
            {
                SlotCondition.Morning or SlotCondition.Day or SlotCondition.Night => s.Condition == t,
                SlotCondition.NightFishing => t == SlotCondition.Night,
                SlotCondition.None => t != SlotCondition.Night || !m.Slots.Any(o => o.Index == s.Index && o.Condition == SlotCondition.NightFishing),
                _ => true
            }))).ToArray();

        // A card's row not drawn now: on a file whose version has the location, a row only the other version has there, which
        // MilestoneRows.Counted leaves out and which never comes back (default 5); else a row not meetable at this time of day.
        // version is null on the editor's read and where the location is not on the file's version: every row counts there.
        internal static bool HiddenNow(RomVersion? only, RomVersion? version, SlotCondition[] times, SlotCondition? now)
            => (version is not null && only is not null && only != version) || (now is { } t && times.Length > 0 && !times.Contains(t));

        // The rows not meetable now, grouped by the next time each can be met, in order from now; empty when now is not known.
        // A row with Times [] (not in the slots) or all three is never hidden. Form rows of one species give its name once.
        internal static Dictionary<SlotCondition, List<string>> HiddenUntil(IEnumerable<(string Name, SlotCondition[] Times)> rows, SlotCondition? now)
        {
            if (now is not { } time)
            {
                return new();
            }
            int at = Array.IndexOf(DayTimes, time);
            SlotCondition[] next = [DayTimes[(at + 1) % 3], DayTimes[(at + 2) % 3]];
            var hidden = rows.Where(r => r.Times.Length > 0 && !r.Times.Contains(time)).ToList();
            return next.Select(t => (Time: t, Names: hidden.Where(r => next.First(r.Times.Contains) == t).Select(r => r.Name).Distinct().ToList()))
                .Where(g => g.Names.Count > 0)
                .ToDictionary(g => g.Time, g => g.Names);
        }

        // A location's species as milestone rows, in slot order of the first included version, then any new ones from the second.
        // Amounts are the preset's for the included versions, applied to each species: its form entries, else its any-form
        // entry, else one any-form row at 0.
        internal static List<(GoalEntry Entry, RomVersion? Version, SlotCondition[] Times)> Seed(IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> milestones,
            string locationId, IReadOnlyList<RomVersion> versions, GoalPreset preset, PokemonMetadataService metadata)
        {
            var at = At(milestones, locationId);
            var included = versions.Where(at.ContainsKey).Select(v => at[v]).ToList();
            var entries = versions.SelectMany(v => GoalPresets.Entries(preset, v, milestones, metadata)).DistinctBy(e => e.Key).ToList();
            return included.SelectMany(m => m.Slots).Select(s => s.Species).Distinct().SelectMany(species =>
            {
                var mine = entries.Where(e => e.Species == species).ToList();
                var forms = mine.Where(e => e.FormCode is not null).ToList();
                IEnumerable<GoalEntry> rows = forms.Count > 0 ? forms : [mine.SingleOrDefault() ?? new GoalEntry(species, null, 0)];
                var version = OnlyVersion(milestones, at, species);
                var times = Times(included, species);
                return rows.Select(e => (e, version, times));
            }).ToList();
        }

        // Fills each phase row's name, species and default form code from the metadata, as InitAsync and AddOrUpdatePhaseInfoAsync load a phase.
        internal static void Normalize(PhaseInfo phaseInfo, PokemonMetadataService metadataService)
        {
            foreach (var pokemon in phaseInfo.Pokemon)
            {
                var metadata = GetMetadata(pokemon, metadataService);
                pokemon.Name = metadata.PokemonName;
                pokemon.SpeciesId = metadata.Id;
                if (String.IsNullOrEmpty(pokemon.FormCode))
                {
                    pokemon.FormCode = metadata.PokeApiName;
                }
            }
        }

        private static PokemonMetadata GetMetadata(PhasePokemonInfo pokemon, PokemonMetadataService metadataService)
        {
            // TODO form code would be at the top as it's the most descriptive
            if(pokemon.SpeciesId != 0)
            {
                return metadataService.GetPokemonMetadata(pokemon.SpeciesId);
            }
            if (!String.IsNullOrWhiteSpace(pokemon.Name))
            {
                return metadataService.GetPokemonMetadata(pokemon.Name);
            }
            throw new ArgumentException("Pokemon phase info is corrupted");
        }
    }

    public class PhaseInfo
    {
        public string GameName { get; set; } = "";
        public string Name { get; set; } = "";
        public GameFlavor Game { get
            {
                return Enum.Parse<GameFlavor>(GameName);
            } 
        }
        public List<PhasePokemonInfo> Pokemon { get; set; } = null!;
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }
        public bool IsStatic { get; set; } = false;
        // A generated milestone id ("34-land"), or null for none; a document stored without these three reads as today (FR-42).
        public string? Location { get; set; }
        // The game files it is offered on; empty is every file.
        public List<string> GameFiles { get; set; } = new();
        // Hidden from the launch form only.
        public bool Archived { get; set; }
    }

    public class PhasePokemonInfo
    {
        public string Name { get; set; } = "";
        public int SpeciesId { get; set; } = 0;
        public int Amount { get; set; } = 0;
        public string FormCode { get; set; } = "";
        public bool RequireSpecificForm { get; set; } = false;

        /// <summary>
        /// Whether this species has a distinct female form. Derived from pokemon metadata and filled in
        /// on the way out, never stored: it lets a client tell a male form from a genderless one, which
        /// the form code alone cannot express (GetFormCode only ever appends ";f", so male and
        /// genderless both come through as the plain slug).
        /// </summary>
        [BsonIgnore]
        public bool HasFemaleForm { get; set; } = false;
    }

}
