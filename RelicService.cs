using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VoidLedger.Models;

namespace VoidLedger.Services
{
    public class RelicService
    {
        //  WDD API 
        private const string WddRelicsUrl = "https://drops.warframestat.us/data/relics.json";
        private const string WddInfoUrl   = "https://drops.warframestat.us/data/info.json";

        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        static RelicService()
        {
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        //  WDD response models 

        private class WddRelicsResponse
        {
            [JsonProperty("relics")]
            public List<WddRelic>? Relics { get; set; }
        }

        private class WddRelic
        {
            [JsonProperty("tier")]
            public string? Tier { get; set; }

            [JsonProperty("relicName")]
            public string? RelicName { get; set; }

            [JsonProperty("state")]
            public string? State { get; set; }

            [JsonProperty("rewards")]
            public List<WddReward>? Rewards { get; set; }
        }

        private class WddReward
        {
            [JsonProperty("itemName")]
            public string? ItemName { get; set; }

            [JsonProperty("chance")]
            public double Chance { get; set; }

            [JsonProperty("rarity")]
            public string? Rarity { get; set; }
        }

        private class WddInfoResponse
        {
            [JsonProperty("hash")]
            public string? Hash { get; set; }

            [JsonProperty("timestamp")]
            public long Timestamp { get; set; }

            [JsonProperty("modified")]
            public long Modified { get; set; }
        }

        //  Tier map 

        private static readonly Dictionary<string, RelicTier> TierMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Lith"] = RelicTier.Lith,
                ["Meso"] = RelicTier.Meso,
                ["Neo"]  = RelicTier.Neo,
                ["Axi"]  = RelicTier.Axi,
            };

        private const string FormaBlueprintSlug    = "forma_blueprint";
        private const string TwoFormaBlueprintSlug = "2xforma";

        //  Public API 

        /// Fetch all Intact relics from the WDD drop-data API and map them to
        /// <see cref="RelicData"/> objects ready for pricing.
        /// <param name="slugLookup">Display-name --> WFM slug map (from WfmService).</param>
        /// <param name="vaultedMap">WFM slug --> vaulted flag.</param>
        public async Task<List<RelicData>> LoadFromApiAsync(
            Dictionary<string, string> slugLookup,
            Dictionary<string, bool>?  vaultedMap)
        {
            var json     = await _http.GetStringAsync(WddRelicsUrl);
            var response = JsonConvert.DeserializeObject<WddRelicsResponse>(json);

            if (response?.Relics == null)
                return new List<RelicData>();

            var results = new List<RelicData>();

            foreach (var wddRelic in response.Relics)
            {
                // We only care about Intact state and known tiers
                if (!string.Equals(wddRelic.State, "Intact", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (wddRelic.Tier == null || !TierMap.TryGetValue(wddRelic.Tier, out var tier))
                    continue;
                if (wddRelic.RelicName == null)
                    continue;

                var tierName    = wddRelic.Tier;                                    // e.g. "Axi"
                var relicName   = wddRelic.RelicName;                               // e.g. "A1"
                var urlName     = $"{tierName.ToLower()}_{relicName.ToLower()}_relic"; // "axi_a1_relic"
                var displayName = $"{tierName} {relicName} Relic";

                var drops = (wddRelic.Rewards ?? new List<WddReward>())
                    .Where(r => !string.IsNullOrWhiteSpace(r.ItemName))
                    .Select(r => new DropItem
                    {
                        Name    = r.ItemName!,
                        UrlName = ResolveSlug(r.ItemName!, slugLookup),
                        Rarity  = r.Rarity?.ToLower() ?? "common",
                    })
                    .ToList();

                var relicIsVaulted = vaultedMap != null
                    && vaultedMap.TryGetValue(urlName, out var rv) && rv;

                results.Add(new RelicData
                {
                    UrlName   = urlName,
                    Name      = displayName,
                    Tier      = tier,
                    IsVaulted = relicIsVaulted,
                    Drops     = drops,
                });
            }

            return results;
        }

        /// Check the WDD /data/info.json endpoint and return the data's last-modified
        /// timestamp, or null on failure.
        public async Task<DateTime?> GetWddModifiedAsync()
        {
            try
            {
                var json = await _http.GetStringAsync(WddInfoUrl);
                var info = JsonConvert.DeserializeObject<WddInfoResponse>(json);
                if (info == null) return null;
                return DateTimeOffset.FromUnixTimeMilliseconds(info.Modified).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        //  Slug resolution 

        private static string ResolveSlug(string itemName, Dictionary<string, string> slugLookup)
        {
            if (itemName.Equals("Forma Blueprint", StringComparison.OrdinalIgnoreCase))
                return FormaBlueprintSlug;

            if (itemName.Equals("2 Forma Blueprint", StringComparison.OrdinalIgnoreCase))
                return TwoFormaBlueprintSlug;

            var squashed = itemName.Trim().Replace(" ", "");
            if (squashed.StartsWith("2x", StringComparison.OrdinalIgnoreCase)
                && squashed.IndexOf("forma", StringComparison.OrdinalIgnoreCase) >= 0)
                return TwoFormaBlueprintSlug;

            var key = NormalizeKey(itemName);
            if (slugLookup.TryGetValue(key, out var slug))
                return slug;

            // Best-effort fallback
            return itemName.ToLower()
                .Replace(" blueprint", "_blueprint")
                .Replace(" ", "_")
                .Replace("-", "_");
        }

        public static Dictionary<string, string> BuildSlugLookup(List<WfmItemShort> items)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item.Slug == null) continue;

                var name = item.I18n?.En?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    lookup.TryAdd(NormalizeKey(name), item.Slug);

                lookup.TryAdd(NormalizeKey(item.Slug.Replace("_", " ")), item.Slug);
            }
            return lookup;
        }

        private static string NormalizeKey(string s) => s.Trim().ToLowerInvariant();
    }
}
