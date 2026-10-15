using System;
using System.Collections.Generic;

namespace Playtest
{
    public sealed class GeneratorConfig
    {
        public string Id;
        public int Tier;
        public double CostBase;
        public double GrowthRate;
        public double ProductionBase;
    }

    public sealed class MilestoneConfig
    {
        public int OwnedAtLeast;
        public double ProductionMultiplier;
    }

    public sealed class BranchConfig
    {
        public string Id;
        public double CostBase;
        public double GrowthRate;
        public int MaxLevel;
        public double ActiveProductionMultiplier;
        public double PeakProductionMultiplier;
        public int PeakTotalOwnedAtLeast;
        public int CommitBase;
        public int OwnedDivisor;
    }

    public sealed class PrestigeConfig
    {
        public double Coeff;
        public double Divisor;
        public double Exponent;
        public double PointValue;
        public int MinPointsToPrestige;
        public double StallSeconds;
    }

    public sealed class OfflineConfig
    {
        public double OfflineRate;
        public double CapHours;
        public double WakingHours;
    }

    public sealed class InsightSourceConfig
    {
        public string Id;
        public string Kind;
        public int Threshold;
        public int Insight;
    }

    public sealed class RelicItemConfig
    {
        public string Id;
        public int InsightCost;
        public int MaxLevel;
        public double OfflineRateAdd;
        public double SessionStartFreeGeneratorChance;
        public string FreeGeneratorId;
        public MilestoneConfig ExtraMilestone;
        public double SessionOpenBoostMultiplier;
        public double SessionOpenBoostSeconds;
    }

    public sealed class RelicAxisConfig
    {
        public string CurrencyId;
        public InsightSourceConfig[] Sources;
        public RelicItemConfig[] Items;
    }

    public sealed class EconomyConfig
    {
        public const int SupportedSchemaVersion = 1;

        public int SchemaVersion;
        public string CurrencyId;
        public double StartingCurrency;
        public double ActiveClickPerSecond;
        public GeneratorConfig[] Generators;
        public MilestoneConfig[] Milestones;
        public BranchConfig[] Branches;
        public PrestigeConfig Prestige;
        public OfflineConfig Offline;
        public string MonetizationDefaultAction;
        public RelicAxisConfig Relics;

        public static EconomyConfig Parse(string json)
        {
            JsonValue root = JsonMini.Parse(json);
            int schema = root.RequiredInt("schemaVersion");
            if (schema != SupportedSchemaVersion)
                throw new InvalidOperationException(
                    "economy_config.json schemaVersion is " + schema + ". This playtest reads schema 1 only.");
            ExpectFormula(root, "costNext", "cost_base * growth_rate^owned");
            ExpectFormula(root, "productionTotal", "production_base * owned * multipliers");
            ExpectFormula(root, "prestigeReset", "floor(coeff * (lifetime_currency / divisor)^exponent)");
            ExpectFormula(root, "prestigeMultiplier", "(1 + point_value)^total_points");
            ExpectFormula(root, "offlineEarnings", "production * min(time_away, cap_seconds) * offline_rate");

            JsonValue monetization = root.Require("monetization");
            string defaultAction = monetization.RequiredString("defaultAction");
            if (defaultAction != "decline_offer")
                throw new InvalidOperationException(
                    "economy_config.json monetization.defaultAction is " + defaultAction
                    + ". This playtest has no offer UI and only implements decline_offer.");

            EconomyConfig config = new EconomyConfig
            {
                SchemaVersion = schema,
                CurrencyId = root.RequiredString("currencyId"),
                StartingCurrency = root.RequiredNumber("startingCurrency"),
                ActiveClickPerSecond = root.RequiredNumber("activeClickPerSecond"),
                Generators = ReadGenerators(root.Require("generators")),
                Milestones = ReadMilestones(root.Require("upgrades")),
                Branches = ReadBranches(root.Require("branches")),
                Prestige = ReadPrestige(root.Require("prestige")),
                Offline = ReadOffline(root.Require("offline")),
                MonetizationDefaultAction = defaultAction,
                Relics = ReadRelics(root.Require("relics")),
            };
            return config;
        }

        static void ExpectFormula(JsonValue root, string key, string expected)
        {
            string actual = root.Require("formulas").RequiredString(key);
            if (actual != expected)
                throw new InvalidOperationException(
                    "economy_config.json formulas." + key + " is not the formula this client implements.");
        }

        static GeneratorConfig[] ReadGenerators(JsonValue array)
        {
            if (array.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("economy_config.json generators is not an array");
            List<GeneratorConfig> generators = new List<GeneratorConfig>();
            HashSet<string> ids = new HashSet<string>();
            foreach (JsonValue item in array.Items)
            {
                GeneratorConfig generator = new GeneratorConfig
                {
                    Id = item.RequiredString("id"),
                    Tier = item.RequiredInt("tier"),
                    CostBase = item.RequiredNumber("costBase"),
                    GrowthRate = item.RequiredNumber("growthRate"),
                    ProductionBase = item.RequiredNumber("productionBase"),
                };
                if (generator.GrowthRate <= 1.0)
                    throw new InvalidOperationException(generator.Id + " growthRate must be > 1");
                if (generator.CostBase <= 0.0 || generator.ProductionBase < 0.0)
                    throw new InvalidOperationException(generator.Id + " has a non-positive cost or negative production");
                if (!ids.Add(generator.Id))
                    throw new InvalidOperationException("duplicate generator id " + generator.Id);
                generators.Add(generator);
            }
            return generators.ToArray();
        }

        static MilestoneConfig[] ReadMilestones(JsonValue array)
        {
            if (array.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("economy_config.json upgrades is not an array");
            MilestoneConfig[] milestones = new MilestoneConfig[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
                milestones[index] = ReadMilestone(array.Items[index]);
            return milestones;
        }

        static MilestoneConfig ReadMilestone(JsonValue item)
        {
            return new MilestoneConfig
            {
                OwnedAtLeast = item.RequiredInt("ownedAtLeast"),
                ProductionMultiplier = item.RequiredNumber("productionMultiplier"),
            };
        }

        static BranchConfig[] ReadBranches(JsonValue array)
        {
            if (array.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("economy_config.json branches is not an array");
            BranchConfig[] branches = new BranchConfig[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
            {
                JsonValue item = array.Items[index];
                if (item.Map.ContainsKey("structure"))
                    throw new InvalidOperationException(
                        "Branch " + item.RequiredString("id")
                        + " has a structure field. This playtest reads the single-purchase export only.");
                JsonValue formula = item.Require("commitThresholdFormula");
                BranchConfig branch = new BranchConfig
                {
                    Id = item.RequiredString("id"),
                    CostBase = item.RequiredNumber("costBase"),
                    GrowthRate = item.RequiredNumber("growthRate"),
                    MaxLevel = item.RequiredInt("maxLevel"),
                    ActiveProductionMultiplier = item.RequiredNumber("activeProductionMultiplier"),
                    PeakProductionMultiplier = item.RequiredNumber("peakProductionMultiplier"),
                    PeakTotalOwnedAtLeast = item.RequiredInt("peakTotalOwnedAtLeast"),
                    CommitBase = formula.RequiredInt("base"),
                    OwnedDivisor = formula.RequiredInt("ownedDivisor"),
                };
                if (branch.GrowthRate <= 1.0 || branch.CostBase <= 0.0)
                    throw new InvalidOperationException(branch.Id + " cost curve is not exponential");
                if (branch.MaxLevel < 1 || branch.CommitBase < 1 || branch.OwnedDivisor < 1)
                    throw new InvalidOperationException(branch.Id + " commit threshold is not usable");
                branches[index] = branch;
            }
            return branches;
        }

        static PrestigeConfig ReadPrestige(JsonValue item)
        {
            PrestigeConfig prestige = new PrestigeConfig
            {
                Coeff = item.RequiredNumber("coeff"),
                Divisor = item.RequiredNumber("divisor"),
                Exponent = item.RequiredNumber("exponent"),
                PointValue = item.RequiredNumber("pointValue"),
                MinPointsToPrestige = item.RequiredInt("minPointsToPrestige"),
                StallSeconds = item.RequiredNumber("stallSeconds"),
            };
            if (prestige.Exponent <= 0.0 || prestige.Exponent >= 1.0)
                throw new InvalidOperationException("prestige exponent must be sublinear");
            if (prestige.PointValue < 0.0 || prestige.Divisor <= 0.0 || prestige.Coeff <= 0.0)
                throw new InvalidOperationException("prestige config is not usable");
            return prestige;
        }

        static OfflineConfig ReadOffline(JsonValue item)
        {
            OfflineConfig offline = new OfflineConfig
            {
                OfflineRate = item.RequiredNumber("offlineRate"),
                CapHours = item.RequiredNumber("capHours"),
                WakingHours = item.RequiredNumber("wakingHours"),
            };
            if (offline.OfflineRate < 0.0 || offline.CapHours < 0.0)
                throw new InvalidOperationException("offline config is not usable");
            return offline;
        }

        static RelicAxisConfig ReadRelics(JsonValue item)
        {
            JsonValue sources = item.Require("sources");
            JsonValue relics = item.Require("items");
            if (sources.Type != JsonValue.Kind.Array || relics.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("economy_config.json relics is incomplete");
            InsightSourceConfig[] sourceList = new InsightSourceConfig[sources.Items.Count];
            for (int index = 0; index < sources.Items.Count; index++)
            {
                JsonValue source = sources.Items[index];
                string kind = source.RequiredString("kind");
                if (kind != "total_owned_at_least" && kind != "prestige_points_at_least")
                    throw new InvalidOperationException("unsupported insight source " + source.RequiredString("id"));
                sourceList[index] = new InsightSourceConfig
                {
                    Id = source.RequiredString("id"),
                    Kind = kind,
                    Threshold = source.RequiredInt("threshold"),
                    Insight = source.RequiredInt("insight"),
                };
            }
            RelicItemConfig[] items = new RelicItemConfig[relics.Items.Count];
            HashSet<string> ids = new HashSet<string>();
            for (int index = 0; index < relics.Items.Count; index++)
            {
                JsonValue relic = relics.Items[index];
                string freeId = null;
                if (relic.Has("freeGeneratorId"))
                    freeId = relic.RequiredString("freeGeneratorId");
                MilestoneConfig extra = null;
                if (relic.Has("extraMilestone"))
                    extra = ReadMilestone(relic.Require("extraMilestone"));
                RelicItemConfig itemConfig = new RelicItemConfig
                {
                    Id = relic.RequiredString("id"),
                    InsightCost = relic.RequiredInt("insightCost"),
                    MaxLevel = relic.RequiredInt("maxLevel"),
                    OfflineRateAdd = relic.RequiredNumber("offlineRateAdd"),
                    SessionStartFreeGeneratorChance = relic.RequiredNumber("sessionStartFreeGeneratorChance"),
                    FreeGeneratorId = freeId,
                    ExtraMilestone = extra,
                    SessionOpenBoostMultiplier = relic.RequiredNumber("sessionOpenBoostMultiplier"),
                    SessionOpenBoostSeconds = relic.RequiredNumber("sessionOpenBoostSeconds"),
                };
                double chance = itemConfig.SessionStartFreeGeneratorChance;
                if (chance < 0.0 || chance > 1.0)
                    throw new InvalidOperationException(itemConfig.Id + " free-generator chance is outside 0..1");
                if (chance > 0.0 && string.IsNullOrEmpty(freeId))
                    throw new InvalidOperationException(itemConfig.Id + " names no free generator");
                if (!ids.Add(itemConfig.Id))
                    throw new InvalidOperationException("duplicate relic id " + itemConfig.Id);
                items[index] = itemConfig;
            }
            return new RelicAxisConfig
            {
                CurrencyId = item.RequiredString("currencyId"),
                Sources = sourceList,
                Items = items,
            };
        }
    }
}
