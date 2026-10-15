using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Playtest
{
    public sealed class OfferProgress
    {
        public bool Shown;
        public bool Purchased;
        public bool Declined;
        public double ShownAtActiveSeconds;
    }

    public struct PurchaseResult
    {
        public readonly double Cost;
        public readonly int OwnedAfter;
        public readonly double CurrencyAfter;

        public PurchaseResult(double cost, int ownedAfter, double currencyAfter)
        {
            Cost = cost;
            OwnedAfter = ownedAfter;
            CurrencyAfter = currencyAfter;
        }
    }

    public struct PrestigeOutcome
    {
        public readonly double LifetimeCurrency;
        public readonly int PointsGranted;
        public readonly double MultiplierAfter;

        public PrestigeOutcome(double lifetimeCurrency, int pointsGranted, double multiplierAfter)
        {
            LifetimeCurrency = lifetimeCurrency;
            PointsGranted = pointsGranted;
            MultiplierAfter = multiplierAfter;
        }
    }

    public struct RelicPurchaseResult
    {
        public readonly string RelicId;
        public readonly int InsightCost;
        public readonly int InsightAfter;

        public RelicPurchaseResult(string relicId, int insightCost, int insightAfter)
        {
            RelicId = relicId;
            InsightCost = insightCost;
            InsightAfter = insightAfter;
        }
    }

    /// <summary>
    /// Live run. Every coefficient is read from <see cref="EconomyConfig"/>.
    /// The offline ceiling is the published offlineRate: the export has no
    /// separate cap field. noisy_seed is not in the export, so the session
    /// roll is keyed by the install id rather than the simulation seed.
    /// </summary>
    public sealed class PlaytestState
    {
        public const double SecondsPerHour = 3600.0;

        readonly EconomyConfig config;
        readonly Dictionary<string, int> owned = new Dictionary<string, int>();
        readonly Dictionary<string, int> branchLevels = new Dictionary<string, int>();
        readonly Dictionary<string, int> relicLevels = new Dictionary<string, int>();
        readonly HashSet<string> claimedSources = new HashSet<string>();
        readonly Dictionary<string, OfferProgress> offers = new Dictionary<string, OfferProgress>();

        public PlaytestState(EconomyConfig config, string playerId)
        {
            this.config = config;
            PlayerId = playerId;
            foreach (GeneratorConfig generator in config.Generators)
                owned[generator.Id] = 0;
            foreach (BranchConfig branch in config.Branches)
            {
                branchLevels[branch.Id] = 0;
                offers[branch.Id] = new OfferProgress();
            }
            foreach (RelicItemConfig relic in config.Relics.Items)
                relicLevels[relic.Id] = 0;
        }

        public string PlayerId { get; private set; }
        public double Currency { get; private set; }
        public double LifetimeCurrency { get; private set; }
        public double CumulativeActiveSeconds { get; private set; }
        public double RunActiveSeconds { get; private set; }
        public int PrestigePoints { get; private set; }
        public int PrestigeCount { get; private set; }
        public int Insight { get; private set; }
        public int PendingPrestigePoints { get; private set; }
        public bool SessionStarted { get; private set; }
        public double SessionBoostUntil { get; private set; }
        public double SessionBoostMultiplier { get; private set; }

        public int TotalOwned
        {
            get
            {
                int total = 0;
                foreach (int count in owned.Values)
                    total += count;
                return total;
            }
        }

        public int OwnedCount(string generatorId)
        {
            int count;
            return owned.TryGetValue(generatorId, out count) ? count : 0;
        }

        public int RelicLevel(string relicId)
        {
            int level;
            return relicLevels.TryGetValue(relicId, out level) ? level : 0;
        }

        public OfferProgress Offer(string branchId)
        {
            return offers[branchId];
        }

        public static PlaytestState NewGame(EconomyConfig config)
        {
            PlaytestState state = new PlaytestState(config, Guid.NewGuid().ToString("N"));
            state.Currency = config.StartingCurrency;
            return state;
        }

        public void Tick(double seconds)
        {
            if (seconds <= 0.0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return;
            double earned = ProductionPerSecond() * seconds;
            Currency += earned;
            LifetimeCurrency += earned;
            RunActiveSeconds += seconds;
            CumulativeActiveSeconds += seconds;
            ClaimInsight();
        }

        public double ProductionPerSecond()
        {
            double prestige = PrestigeMultiplier(PrestigePoints);
            double generators = 0.0;
            foreach (GeneratorConfig generator in config.Generators)
            {
                int count = OwnedCount(generator.Id);
                double multipliers = MilestoneMultiplier(count) * prestige;
                generators += generator.ProductionBase * count * multipliers;
            }
            generators *= BranchMultiplier();
            double clicks = config.ActiveClickPerSecond * prestige;
            return (clicks + generators) * OpeningBoost();
        }

        public double NextGeneratorCost(GeneratorConfig generator)
        {
            return CostNext(generator.CostBase, generator.GrowthRate, OwnedCount(generator.Id));
        }

        public double NextBranchCost(BranchConfig branch)
        {
            int level;
            branchLevels.TryGetValue(branch.Id, out level);
            return CostNext(branch.CostBase, branch.GrowthRate, level);
        }

        public bool OfferIsOpen(BranchConfig branch)
        {
            int level;
            branchLevels.TryGetValue(branch.Id, out level);
            if (level >= branch.MaxLevel)
                return false;
            int total = TotalOwned;
            if (total >= branch.PeakTotalOwnedAtLeast)
                return false;
            int threshold = branch.CommitBase + total / branch.OwnedDivisor;
            return total < threshold;
        }

        public int PointsReady()
        {
            return PointsFromLifetime(LifetimeCurrency) + PendingPrestigePoints;
        }

        public bool PrestigeAvailable()
        {
            return PointsReady() >= config.Prestige.MinPointsToPrestige;
        }

        public double PrestigeMultiplierAfterReset()
        {
            return PrestigeMultiplier(PrestigePoints + PointsReady());
        }

        public bool TryBuyGenerator(GeneratorConfig generator, out PurchaseResult result)
        {
            double cost = NextGeneratorCost(generator);
            if (!CanAfford(cost))
            {
                result = default(PurchaseResult);
                return false;
            }
            Currency -= cost;
            if (Currency < 0.0)
                Currency = 0.0;
            owned[generator.Id] = OwnedCount(generator.Id) + 1;
            ClaimInsight();
            result = new PurchaseResult(cost, owned[generator.Id], Currency);
            return true;
        }

        public bool TryBuyBranch(BranchConfig branch, out PurchaseResult result)
        {
            if (!OfferIsOpen(branch))
            {
                result = default(PurchaseResult);
                return false;
            }
            double cost = NextBranchCost(branch);
            if (!CanAfford(cost))
            {
                result = default(PurchaseResult);
                return false;
            }
            Currency -= cost;
            if (Currency < 0.0)
                Currency = 0.0;
            int level;
            branchLevels.TryGetValue(branch.Id, out level);
            branchLevels[branch.Id] = level + 1;
            offers[branch.Id].Purchased = true;
            result = new PurchaseResult(cost, branchLevels[branch.Id], Currency);
            return true;
        }

        public bool TryBuyRelic(RelicItemConfig relic, out RelicPurchaseResult result)
        {
            int level = RelicLevel(relic.Id);
            if (level >= relic.MaxLevel || Insight < relic.InsightCost)
            {
                result = default(RelicPurchaseResult);
                return false;
            }
            Insight -= relic.InsightCost;
            relicLevels[relic.Id] = level + 1;
            result = new RelicPurchaseResult(relic.Id, relic.InsightCost, Insight);
            return true;
        }

        public bool TryPrestige(out PrestigeOutcome outcome)
        {
            int granted = PointsReady();
            if (granted < config.Prestige.MinPointsToPrestige)
            {
                outcome = default(PrestigeOutcome);
                return false;
            }
            double lifetime = LifetimeCurrency;
            int totalPoints = PrestigePoints + granted;
            double multiplier = PrestigeMultiplier(totalPoints);
            outcome = new PrestigeOutcome(lifetime, granted, multiplier);
            PrestigePoints = totalPoints;
            PrestigeCount += 1;
            Currency = 0.0;
            LifetimeCurrency = 0.0;
            RunActiveSeconds = 0.0;
            PendingPrestigePoints = 0;
            SessionStarted = false;
            SessionBoostUntil = 0.0;
            SessionBoostMultiplier = 1.0;
            foreach (GeneratorConfig generator in config.Generators)
                owned[generator.Id] = 0;
            foreach (BranchConfig branch in config.Branches)
            {
                branchLevels[branch.Id] = 0;
                offers[branch.Id] = new OfferProgress();
            }
            claimedSources.Clear();
            BeginSession();
            ClaimInsight();
            return true;
        }

        public void BeginSession()
        {
            if (SessionStarted)
                return;
            SessionStarted = true;
            if (RunActiveSeconds != 0.0)
                return;
            double multiplier = 1.0;
            double until = 0.0;
            double roll = SessionRoll(PlayerId, PrestigeCount);
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                int level = RelicLevel(relic.Id);
                if (level <= 0)
                    continue;
                if (relic.SessionOpenBoostSeconds > 0.0 && relic.SessionOpenBoostMultiplier > 0.0)
                {
                    multiplier *= Math.Pow(relic.SessionOpenBoostMultiplier, level);
                    if (relic.SessionOpenBoostSeconds > until)
                        until = relic.SessionOpenBoostSeconds;
                }
                if (relic.SessionStartFreeGeneratorChance > 0.0
                    && !string.IsNullOrEmpty(relic.FreeGeneratorId)
                    && roll < relic.SessionStartFreeGeneratorChance
                    && owned.ContainsKey(relic.FreeGeneratorId))
                {
                    owned[relic.FreeGeneratorId] = OwnedCount(relic.FreeGeneratorId) + level;
                }
            }
            SessionBoostMultiplier = multiplier;
            SessionBoostUntil = until;
        }

        public double OfflineCurrency(double timeAwaySeconds)
        {
            if (timeAwaySeconds <= 0.0)
                return 0.0;
            double capped = Math.Min(timeAwaySeconds, config.Offline.CapHours * SecondsPerHour);
            return ProductionPerSecond() * capped * EffectiveOfflineRate();
        }

        public void GrantOffline(double currency)
        {
            if (currency <= 0.0)
                return;
            Currency += currency;
            LifetimeCurrency += currency;
        }

        public void MarkOfferShown(string branchId)
        {
            OfferProgress offer = offers[branchId];
            offer.Shown = true;
            offer.ShownAtActiveSeconds = RunActiveSeconds;
        }

        public void MarkOfferDeclined(string branchId)
        {
            offers[branchId].Declined = true;
        }

        public double ActiveSecondsSinceOffer(string branchId)
        {
            return RunActiveSeconds - offers[branchId].ShownAtActiveSeconds;
        }

        public string ToJson()
        {
            JsonValue root = JsonValue.MakeObject();
            root.Map["playerId"] = JsonValue.From(PlayerId);
            root.Map["currency"] = JsonValue.From(Currency);
            root.Map["lifetimeCurrency"] = JsonValue.From(LifetimeCurrency);
            root.Map["cumulativeActiveSeconds"] = JsonValue.From(CumulativeActiveSeconds);
            root.Map["runActiveSeconds"] = JsonValue.From(RunActiveSeconds);
            root.Map["prestigePoints"] = JsonValue.From(PrestigePoints);
            root.Map["prestigeCount"] = JsonValue.From(PrestigeCount);
            root.Map["insight"] = JsonValue.From(Insight);
            root.Map["pendingPrestigePoints"] = JsonValue.From(PendingPrestigePoints);
            root.Map["sessionStarted"] = JsonValue.From(SessionStarted);
            root.Map["sessionBoostUntil"] = JsonValue.From(SessionBoostUntil);
            root.Map["sessionBoostMultiplier"] = JsonValue.From(SessionBoostMultiplier);
            root.Map["owned"] = IntMap(owned);
            root.Map["branchLevels"] = IntMap(branchLevels);
            root.Map["relicLevels"] = IntMap(relicLevels);
            JsonValue claimed = JsonValue.MakeArray();
            foreach (string source in claimedSources)
                claimed.Items.Add(JsonValue.From(source));
            root.Map["claimedInsightSources"] = claimed;
            JsonValue offerMap = JsonValue.MakeObject();
            foreach (KeyValuePair<string, OfferProgress> pair in offers)
            {
                JsonValue offer = JsonValue.MakeObject();
                offer.Map["shown"] = JsonValue.From(pair.Value.Shown);
                offer.Map["purchased"] = JsonValue.From(pair.Value.Purchased);
                offer.Map["declined"] = JsonValue.From(pair.Value.Declined);
                offer.Map["shownAtActiveSeconds"] = JsonValue.From(pair.Value.ShownAtActiveSeconds);
                offerMap.Map[pair.Key] = offer;
            }
            root.Map["offers"] = offerMap;
            return root.Write();
        }

        public static PlaytestState FromJson(EconomyConfig config, string json)
        {
            JsonValue root = JsonMini.Parse(json);
            PlaytestState state = new PlaytestState(config, root.RequiredString("playerId"));
            state.Currency = root.RequiredNumber("currency");
            state.LifetimeCurrency = root.RequiredNumber("lifetimeCurrency");
            state.CumulativeActiveSeconds = root.RequiredNumber("cumulativeActiveSeconds");
            state.RunActiveSeconds = root.RequiredNumber("runActiveSeconds");
            state.PrestigePoints = root.RequiredInt("prestigePoints");
            state.PrestigeCount = root.RequiredInt("prestigeCount");
            state.Insight = root.RequiredInt("insight");
            state.PendingPrestigePoints = root.RequiredInt("pendingPrestigePoints");
            state.SessionStarted = root.Require("sessionStarted").Bool;
            state.SessionBoostUntil = root.RequiredNumber("sessionBoostUntil");
            state.SessionBoostMultiplier = root.RequiredNumber("sessionBoostMultiplier");
            ReadIntMap(root.Require("owned"), state.owned);
            ReadIntMap(root.Require("branchLevels"), state.branchLevels);
            ReadIntMap(root.Require("relicLevels"), state.relicLevels);
            foreach (JsonValue source in root.Require("claimedInsightSources").Items)
                state.claimedSources.Add(source.Text);
            JsonValue offerMap = root.Require("offers");
            foreach (BranchConfig branch in config.Branches)
            {
                if (!offerMap.Map.ContainsKey(branch.Id))
                    continue;
                JsonValue offer = offerMap.Map[branch.Id];
                OfferProgress progress = state.offers[branch.Id];
                progress.Shown = offer.Require("shown").Bool;
                progress.Purchased = offer.Require("purchased").Bool;
                progress.Declined = offer.Require("declined").Bool;
                progress.ShownAtActiveSeconds = offer.RequiredNumber("shownAtActiveSeconds");
            }
            return state;
        }

        public void ClaimInsight()
        {
            int total = TotalOwned;
            int ready = PointsFromLifetime(LifetimeCurrency);
            foreach (InsightSourceConfig source in config.Relics.Sources)
            {
                if (claimedSources.Contains(source.Id))
                    continue;
                if (source.Kind == "total_owned_at_least" && total < source.Threshold)
                    continue;
                if (source.Kind == "prestige_points_at_least" && ready < source.Threshold)
                    continue;
                Insight += source.Insight;
                claimedSources.Add(source.Id);
            }
        }

        double MilestoneMultiplier(int count)
        {
            double multiplier = 1.0;
            foreach (MilestoneConfig milestone in config.Milestones)
            {
                if (count >= milestone.OwnedAtLeast)
                    multiplier *= milestone.ProductionMultiplier;
            }
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                if (RelicLevel(relic.Id) <= 0 || relic.ExtraMilestone == null)
                    continue;
                if (count >= relic.ExtraMilestone.OwnedAtLeast)
                    multiplier *= relic.ExtraMilestone.ProductionMultiplier;
            }
            return multiplier;
        }

        double BranchMultiplier()
        {
            int total = TotalOwned;
            double multiplier = 1.0;
            foreach (BranchConfig branch in config.Branches)
            {
                int level;
                branchLevels.TryGetValue(branch.Id, out level);
                if (level <= 0)
                    continue;
                multiplier *= Math.Pow(branch.ActiveProductionMultiplier, level);
                if (total >= branch.PeakTotalOwnedAtLeast)
                    multiplier *= Math.Pow(branch.PeakProductionMultiplier, level);
            }
            return multiplier;
        }

        double OpeningBoost()
        {
            if (RunActiveSeconds < SessionBoostUntil && SessionBoostMultiplier > 0.0)
                return SessionBoostMultiplier;
            return 1.0;
        }

        double EffectiveOfflineRate()
        {
            double rate = config.Offline.OfflineRate;
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                int level = RelicLevel(relic.Id);
                if (level <= 0 || relic.OfflineRateAdd == 0.0)
                    continue;
                rate += relic.OfflineRateAdd * level;
            }
            if (rate < 0.0)
                return 0.0;
            if (rate > config.Offline.OfflineRate)
                return config.Offline.OfflineRate;
            return rate;
        }

        int PointsFromLifetime(double lifetime)
        {
            PrestigeConfig prestige = config.Prestige;
            if (lifetime <= 0.0 || prestige.Divisor <= 0.0 || prestige.Coeff <= 0.0)
                return 0;
            double raw = prestige.Coeff * Math.Pow(lifetime / prestige.Divisor, prestige.Exponent);
            if (double.IsInfinity(raw) || double.IsNaN(raw) || raw < 1.0)
                return 0;
            return (int)Math.Floor(raw);
        }

        double PrestigeMultiplier(int points)
        {
            return Math.Pow(1.0 + config.Prestige.PointValue, points);
        }

        static double CostNext(double costBase, double growthRate, int ownedCount)
        {
            return costBase * Math.Pow(growthRate, ownedCount);
        }

        static bool CanAfford(double currency, double cost)
        {
            return currency + 1e-9 * Math.Max(1.0, cost) >= cost;
        }

        bool CanAfford(double cost)
        {
            return CanAfford(Currency, cost);
        }

        static JsonValue IntMap(Dictionary<string, int> values)
        {
            JsonValue map = JsonValue.MakeObject();
            foreach (KeyValuePair<string, int> pair in values)
                map.Map[pair.Key] = JsonValue.From(pair.Value);
            return map;
        }

        static void ReadIntMap(JsonValue map, Dictionary<string, int> into)
        {
            foreach (KeyValuePair<string, JsonValue> pair in map.Map)
            {
                if (!into.ContainsKey(pair.Key))
                    continue;
                into[pair.Key] = (int)Math.Round(pair.Value.Number);
            }
        }

        /// <summary>
        /// Same unit-interval construction as model.relics.session_roll.
        /// The export does not carry noisy_seed, so the key is this install.
        /// </summary>
        public static double SessionRoll(string playerId, int prestigeCount)
        {
            string text = playerId + ":" + prestigeCount.ToString(CultureInfo.InvariantCulture);
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(text));
                uint bucket = ((uint)hash[0] << 24) | ((uint)hash[1] << 16) | ((uint)hash[2] << 8) | hash[3];
                return bucket / (double)0xFFFFFFFF;
            }
        }
    }
}
