using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VoidLedger.Models;

namespace VoidLedger.Services
{
    public static class CacheService
    {
        private static readonly string CacheDir =
            Path.Combine(AppContext.BaseDirectory, "cache");

        private static readonly string CachePath = Path.Combine(CacheDir, "relic_cache.json");

        //  Wrapper stored in the JSON file 
        private class CacheFile
        {
            [JsonProperty("pulled_at")]
            public DateTime PulledAt { get; set; }

            [JsonProperty("data")]
            public Dictionary<RelicTier, List<RelicData>>? Data { get; set; }

            [JsonProperty("owned_relics")]
            public HashSet<string>? OwnedRelics { get; set; }
        }

        //  Save 
        public static async Task SaveAsync(Dictionary<RelicTier, List<RelicData>> data, HashSet<string>? ownedRelics = null, DateTime? pulledAt = null)
        {
            Directory.CreateDirectory(CacheDir);

            var file = new CacheFile { PulledAt = pulledAt ?? DateTime.UtcNow, Data = data, OwnedRelics = ownedRelics };
            var json = JsonConvert.SerializeObject(file, Formatting.Indented);
            await File.WriteAllTextAsync(CachePath, json);
        }

        //  Load 
        // Returns (data, pulledAt, ownedRelics) or null if no cache exists / is corrupt.
        public static async Task<(Dictionary<RelicTier, List<RelicData>> Data, DateTime PulledAt, HashSet<string> OwnedRelics)?> LoadAsync()
        {
            if (!File.Exists(CachePath)) return null;

            try
            {
                var json = await File.ReadAllTextAsync(CachePath);
                var file = JsonConvert.DeserializeObject<CacheFile>(json);
                if (file?.Data == null) return null;

                var owned = file.OwnedRelics ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return (file.Data, file.PulledAt, owned);
            }
            catch
            {
                // Corrupt cache - treat as missing
                return null;
            }
        }

        //  Helpers 
        public static bool Exists() => File.Exists(CachePath);

        // Human-readable age string, e.g. "3 hours ago" or "2 days ago".
        public static string AgeString(DateTime savedAt)
        {
            var age = DateTime.UtcNow - savedAt;
            if (age.TotalMinutes < 1)   return "just now";
            if (age.TotalMinutes < 60)  return $"{(int)age.TotalMinutes}m ago";
            if (age.TotalHours   < 24)  return $"{(int)age.TotalHours}h ago";
            return $"{(int)age.TotalDays}d ago";
        }
    }
}
