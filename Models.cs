using System.Collections.Generic;
using Newtonsoft.Json;

namespace VoidLedger.Models
{
    //  WFM API response wrappers 

    public class WfmItemsResponse
    {
        [JsonProperty("data")]
        public List<WfmItemShort>? Data { get; set; }
    }

    public class WfmItemShort
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("slug")]
        public string? Slug { get; set; }

        [JsonProperty("gameRef")]
        public string? GameRef { get; set; }

        [JsonProperty("tags")]
        public List<string>? Tags { get; set; }

        [JsonProperty("bulkTradable")]
        public bool? BulkTradable { get; set; }

        [JsonProperty("subtypes")]
        public List<string>? Subtypes { get; set; }

        [JsonProperty("vaulted")]
        public bool? Vaulted { get; set; }

        [JsonProperty("i18n")]
        public WfmItemI18n? I18n { get; set; }
    }

    public class WfmItemI18n
    {
        [JsonProperty("en")]
        public WfmItemLang? En { get; set; }
    }

    public class WfmItemLang
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("icon")]
        public string? Icon { get; set; }

        [JsonProperty("thumb")]
        public string? Thumb { get; set; }
    }

    public class WfmOrderTopResponse
    {
        [JsonProperty("data")]
        public WfmOrderTopData? Data { get; set; }
    }

    public class WfmOrderTopData
    {
        [JsonProperty("sell")]
        public List<WfmOrder>? Sell { get; set; }

        [JsonProperty("buy")]
        public List<WfmOrder>? Buy { get; set; }
    }

    public class WfmOrder
    {
        [JsonProperty("type")]
        public string? Type { get; set; }

        [JsonProperty("platinum")]
        public int? Platinum { get; set; }

        [JsonProperty("quantity")]
        public int? Quantity { get; set; }

        [JsonProperty("visible")]
        public bool? Visible { get; set; }

        [JsonProperty("subtype")]
        public string? Subtype { get; set; }
    }

    //  Local relic drop file model 

    public class RelicDropFile
    {
        [JsonProperty("tier")]
        public string? Tier { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("rewards")]
        public RelicDropRewards? Rewards { get; set; }
    }

    public class RelicDropRewards
    {
        [JsonProperty("Intact")]
        public List<RelicDropEntry>? Intact { get; set; }
    }

    public class RelicDropEntry
    {
        [JsonProperty("itemName")]
        public string? ItemName { get; set; }

        [JsonProperty("rarity")]
        public string? Rarity { get; set; }

        [JsonProperty("chance")]
        public double Chance { get; set; }
    }

    //  App models 

    public enum RelicTier { Lith, Meso, Neo, Axi }

    public class DropItem
    {
        public string UrlName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Rarity { get; set; } = "common";
        public double? Price { get; set; }
        public int Quantity { get; set; } = 1;
        public bool Vaulted { get; set; } = false;
        public bool IsForma => UrlName == "forma_blueprint" || UrlName == "2xforma";
    }

    public class RelicData
    {
        public string UrlName { get; set; } = "";
        public string Name { get; set; } = "";
        public RelicTier Tier { get; set; }
        public bool IsVaulted { get; set; } = false;
        public bool IsOwned { get; set; } = false;
        public List<DropItem> Drops { get; set; } = new();
        public double? Avg { get; set; }

        public string TotalDisplay => Avg.HasValue ? $"{Avg.Value:F1} ℙ" : "-";
        public string AvgDisplay => TotalDisplay;
    }
}
