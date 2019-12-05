﻿using System;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SeraphinaNET.Data {
    public class MongoData : DataContextFactory {
        private readonly MongoClient client;
        private readonly string db;
        public MongoData(string connectionString, string dbName) {
            this.client = new MongoClient(connectionString);
            this.db = dbName;
        }

        public MongoDataContext GetContext() => new MongoDataContext(client.GetDatabase(db));
        DataContext DataContextFactory.GetContext() => GetContext();
    }

    // I have to seal this class or VS throws a fit about proper IDisposable impl
    sealed public class MongoDataContext : DataContext {
        private readonly IMongoDatabase db;
        internal MongoDataContext(IMongoDatabase db) {
            this.db = db;
        }

        // Stub Dispose method as Mongo thinks you should only have one Client.
        // So the factory holds this and calls it a day.
        public void Dispose() { }

        [BsonIgnoreExtraElements]
        private class DBChannelInfo {
            // Mongo uses _id
            [BsonId]
            public ulong Id { get; set; }
            [BsonElement("pins")]
            public ulong[]? Pins { get; set; }
        }

        [BsonIgnoreExtraElements]
        private class DBActionInfo {
            [BsonId]
            public ulong Id { get; set; }
            [BsonElement("action")]
            public int ActionId { get; set; }
            [BsonElement("radio")]
            public Dictionary<string, string> Radio { get; set; }
            [BsonElement("tally")]
            public Dictionary<string, string[]> Tally { get; set; }
        }

        private class DBActionController : ActionData {
            private readonly IMongoDatabase db;
            private readonly DBActionInfo info;

            int ActionData.ActionType => info.ActionId;

            public DBActionController(IMongoDatabase db, DBActionInfo info) {
                this.db = db;
                this.info = info;
            }

            async Task ActionData.AddTally(ulong user, string emote) {
                var col = db.GetCollection<DBActionInfo>("actions");
                var filter = Builders<DBActionInfo>.Filter;
                var update = Builders<DBActionInfo>.Update;
                await col.UpdateOneAsync(filter.Eq("_id", info.Id), update.AddToSet($"tally.{user}", emote));
            }
            Task<string?> ActionData.GetRadioData(ulong user) => Task.FromResult(info.Radio.TryGetValue(user.ToString(), out var data) ? data : null);
            Task<string[]> ActionData.GetTallyData(ulong user) => Task.FromResult(info.Tally.TryGetValue(user.ToString(), out var data) ? data : Array.Empty<string>());
            async Task ActionData.RemoveTally(ulong user, string emote) {
                var col = db.GetCollection<DBActionInfo>("actions");
                var filter = Builders<DBActionInfo>.Filter;
                var update = Builders<DBActionInfo>.Update;
                await col.UpdateOneAsync(filter.Eq("_id", info.Id), update.Pull($"tally.{user}", emote));
            }
            async Task ActionData.SetRadioData(ulong user, string? data) {
                var col = db.GetCollection<DBActionInfo>("actions");
                var filter = Builders<DBActionInfo>.Filter;
                var update = Builders<DBActionInfo>.Update;
                await col.UpdateOneAsync(filter.Eq("_id", info.Id), update.Set($"radio.{user}", data));
            }
        }
        static MongoDataContext() { 
            BsonClassMap.RegisterClassMap<DBChannelInfo>();
            BsonClassMap.RegisterClassMap<DBActionInfo>();
        }

        #region impl Pin
        public async Task AddPin(ulong channel, ulong message) {
            var col = db.GetCollection<DBChannelInfo>("channels");
            var filter = Builders<DBChannelInfo>.Filter;
            var update = Builders<DBChannelInfo>.Update;
            await col.UpdateOneAsync(filter.Eq("_id", channel), update.AddToSet("pins", message), new UpdateOptions { IsUpsert = true });
        }
        public Task ClearPins(ulong channel) => SetPins(channel, Array.Empty<ulong>());
        public async Task<ulong[]> GetPins(ulong channel) {
            var col = db.GetCollection<DBChannelInfo>("channels");
            var filter = Builders<DBChannelInfo>.Filter;
            var info = await col.Find(filter.Eq("_id", channel)).FirstOrDefaultAsync();
            return info?.Pins ?? Array.Empty<ulong>();
        }
        public async Task SetPins(ulong channel, ulong[] messages) {
            var col = db.GetCollection<DBChannelInfo>("channels");
            var filter = Builders<DBChannelInfo>.Filter;
            var update = Builders<DBChannelInfo>.Update;
            await col.UpdateOneAsync(filter.Eq("_id", channel), update.Set("pins", messages));
        }
        public async Task<bool> IsPinned(ulong channel, ulong message) {
            var col = db.GetCollection<DBChannelInfo>("channels");
            var filter = Builders<DBChannelInfo>.Filter;
            return 0 != await col.CountDocumentsAsync(filter.Eq("_id", channel) & filter.Eq("pins", message));
        }
        public async Task RemovePin(ulong channel, ulong message) {
            var col = db.GetCollection<DBChannelInfo>("channels");
            var filter = Builders<DBChannelInfo>.Filter;
            var update = Builders<DBChannelInfo>.Update;
            await col.UpdateOneAsync(filter.Eq("_id", channel), update.Pull("pins", message));
        }
        #endregion

        #region impl Action
        public async Task<ActionData?> GetAction(ulong message) {
            var col = db.GetCollection<DBActionInfo>("actions");
            var filter = Builders<DBActionInfo>.Filter;
            var data = await col.Find(filter.Eq("_id", message)).FirstAsync();
            return (data == null || data.Id == 0) ? null : new DBActionController(db, data);
        }
        public async Task SetAction(ulong message, int action) {
            var col = db.GetCollection<DBActionInfo>("actions");
            await col.InsertOneAsync(new DBActionInfo() { Id = message, ActionId = action });
        }
        #endregion
    }
}
