using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VoidLedger.Models;

namespace VoidLedger.Services
{
    public class WfmService
    {
        private static readonly HttpClient _http = new()
        {
            BaseAddress = new Uri("https://api.warframe.market/v2/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const int ConcurrencyLimit = 2;
        private const int DelayMs = 1000;
        private const double FormaPrice = 12.0;
        private const string TwoFormaSlug = "2xforma";

        static WfmService()
        {
            _http.DefaultRequestHeaders.Add("Accept", "application/json");
            _http.DefaultRequestHeaders.Add("Language", "en");
            _http.DefaultRequestHeaders.Add("Platform", "pc");
        }

        //  Fetch all items 
        public async Task<List<WfmItemShort>> GetAllItemsAsync()
        {
            var json = await _http.GetStringAsync("items");
            var response = JsonConvert.DeserializeObject<WfmItemsResponse>(json);
            return response?.Data ?? new();
        }

        //  Fetch average price for one item using top orders 
        public async Task<double?> GetAvgPriceAsync(string slug)
        {
            if (slug == "forma_blueprint") return FormaPrice;
            if (slug == TwoFormaSlug) return FormaPrice * 2;

            try
            {
                var json = await _http.GetStringAsync($"orders/item/{slug}/top");
                var response = JsonConvert.DeserializeObject<WfmOrderTopResponse>(json);
                var sellOrders = response?.Data?.Sell ?? new List<WfmOrder>();
                if (!sellOrders.Any()) return null;

                var prices = sellOrders
                    .Where(o => o.Platinum.HasValue)
                    .OrderBy(o => o.Platinum!.Value)
                    .Take(2)
                    .Select(o => (double)o.Platinum!.Value)
                    .ToList();

                return prices.Any() ? prices.Average() : null;
            }
            catch
            {
                return null;
            }
        }

        //  Batch helper with concurrency + rate limiting 
        public async Task BatchedAsync<T>(
            IList<T> items,
            Func<T, Task> action,
            IProgress<(int done, int total)>? progress = null,
            CancellationToken ct = default)
        {
            var sem = new SemaphoreSlim(ConcurrencyLimit);
            int done = 0;
            var tasks = items.Select(async item =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    await action(item);
                    int d = Interlocked.Increment(ref done);
                    progress?.Report((d, items.Count));
                    await Task.Delay(DelayMs, ct);
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(tasks);
        }

        //  Mark drop items as vaulted
        // A drop is vaulted when every relic that contains it is itself vaulted.
        public static void ApplyVaultedDrops(List<RelicData> relics)
        {
            // Track which drop slugs appear in at least one unvaulted relic
            var appearsInUnvaulted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var relic in relics)
            {
                if (!relic.IsVaulted)
                {
                    foreach (var drop in relic.Drops)
                        appearsInUnvaulted.Add(drop.UrlName);
                }
            }

            // A drop is vaulted only if it never appeared in any unvaulted relic
            foreach (var relic in relics)
            {
                foreach (var drop in relic.Drops)
                    drop.Vaulted = !drop.IsForma && !appearsInUnvaulted.Contains(drop.UrlName);
            }
        }

        //  Parse tier from url_name 
        public static RelicTier? ParseTier(string urlName)
        {
            var l = urlName.ToLower();
            if (l.StartsWith("lith_"))  return RelicTier.Lith;
            if (l.StartsWith("meso_"))  return RelicTier.Meso;
            if (l.StartsWith("neo_"))   return RelicTier.Neo;
            if (l.StartsWith("axi_"))   return RelicTier.Axi;
            return null;
        }
    }
}
