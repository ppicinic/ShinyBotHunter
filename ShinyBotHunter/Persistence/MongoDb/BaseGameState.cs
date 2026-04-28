using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Persistence.MongoDb
{
    [BsonIgnoreExtraElements]
    [BsonKnownTypes(typeof(ShinyStarterGameState))] // Register other types here as you create them
    public class BaseGameState
    {
        [BsonId]
        [BsonGuidRepresentation(GuidRepresentation.Standard)]
        public Guid Id { get; set; }

        public required string Name { get; set; }
        public GameFlavor Game { get; set; }
    }
}
