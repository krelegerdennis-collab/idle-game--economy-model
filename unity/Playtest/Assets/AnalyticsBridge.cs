using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Playtest
{
    [CreateAssetMenu(fileName = "AnalyticsBridge", menuName = "Playtest/Analytics Bridge")]
    public sealed class AnalyticsBridge : ScriptableObject
    {
        public const string LogFileName = "playtest_log.jsonl";
        public const string PrefsKey = "playtest_log_jsonl";

        readonly StringBuilder transcript = new StringBuilder();
        StreamWriter writer;
        string playerId = "";

        public string LogPath { get; private set; }
        public string Transcript => transcript.ToString();

        public void Open(string path)
        {
            LogPath = path;
            transcript.Length = 0;
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                if (PlayerPrefs.HasKey(PrefsKey))
                    transcript.Append(PlayerPrefs.GetString(PrefsKey));
                return;
            }
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            writer = new StreamWriter(path, true, new UTF8Encoding(false));
            writer.AutoFlush = true;
        }

        public void SetPlayer(string id)
        {
            playerId = id ?? "";
        }

        public void SessionBegin(double cumulativePlaytime)
        {
            Dictionary<string, object> fields = new Dictionary<string, object>
            {
                { "cumulative_playtime", cumulativePlaytime },
            };
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                fields["medium"] = "webgl";
            Write("session_begin", fields);
        }

        public void SessionEnd(double sessionDuration, double currencyAtEnd)
        {
            Write("session_end", new Dictionary<string, object>
            {
                { "session_duration", sessionDuration },
                { "currency_at_end", currencyAtEnd },
            });
        }

        public void UpgradePurchase(string generatorType, double cost, int ownedAfter, double currencyAfter, double activePlaytime)
        {
            Write("upgrade_purchase", new Dictionary<string, object>
            {
                { "generator_type", generatorType },
                { "cost", cost },
                { "owned_after", ownedAfter },
                { "currency_after", currencyAfter },
                { "active_playtime", activePlaytime },
            });
        }

        public void ContractOfferShown(string branchId, double ownedAtOffer, double activePlaytime)
        {
            Write("contract_offer_shown", new Dictionary<string, object>
            {
                { "branch_id", branchId },
                { "owned_at_offer", ownedAtOffer },
                { "active_playtime", activePlaytime },
            });
        }

        public void ContractPurchase(string branchId, double ownedAtPurchase, double timeSinceOfferShown, double activePlaytime)
        {
            Write("contract_purchase", new Dictionary<string, object>
            {
                { "branch_id", branchId },
                { "owned_at_purchase", ownedAtPurchase },
                { "time_since_offer_shown", timeSinceOfferShown },
                { "active_playtime", activePlaytime },
            });
        }

        public void ContractDeclined(string branchId, double ownedAtDecline, string reasonIfKnown, double activePlaytime)
        {
            Write("contract_declined", new Dictionary<string, object>
            {
                { "branch_id", branchId },
                { "owned_at_decline", ownedAtDecline },
                { "reason_if_known", reasonIfKnown },
                { "active_playtime", activePlaytime },
            });
        }

        public void PrestigeTrigger(double lifetimeCurrency, int pointsGranted, double multiplierAfter, double activePlaytime)
        {
            Write("prestige_trigger", new Dictionary<string, object>
            {
                { "lifetime_currency", lifetimeCurrency },
                { "points_granted", pointsGranted },
                { "multiplier_after", multiplierAfter },
                { "active_playtime", activePlaytime },
            });
        }

        public void RelicPurchase(string relicId, int insightCost, int insightAfter, double activePlaytime)
        {
            Write("relic_purchase", new Dictionary<string, object>
            {
                { "relic_id", relicId },
                { "insight_cost", insightCost },
                { "insight_after", insightAfter },
                { "active_playtime", activePlaytime },
            });
        }

        public void OfflineEarningCollected(double timeAway, double currencyGranted)
        {
            Write("offline_earning_collected", new Dictionary<string, object>
            {
                { "time_away", timeAway },
                { "currency_granted", currencyGranted },
            });
        }

        public void Flush()
        {
            if (writer != null)
                writer.Flush();
            if (Application.platform != RuntimePlatform.WebGLPlayer)
                return;
            try
            {
                PlayerPrefs.SetString(PrefsKey, transcript.ToString());
                PlayerPrefs.Save();
            }
            catch (Exception)
            {
                Debug.LogWarning("playtest log bleef in het geheugen; PlayerPrefs weigerde de tekst.");
            }
        }

        void Write(string eventName, Dictionary<string, object> fields)
        {
            fields["event"] = eventName;
            fields["timestamp"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            fields["player_id"] = playerId;
            string line = Encode(fields);
            transcript.Append(line).Append('\n');
            if (writer != null)
                writer.WriteLine(line);
        }

        static string Encode(Dictionary<string, object> fields)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> pair in fields)
            {
                if (!first)
                    builder.Append(',');
                first = false;
                builder.Append('"').Append(Escape(pair.Key)).Append("\":");
                builder.Append(EncodeValue(pair.Value));
            }
            builder.Append('}');
            return builder.ToString();
        }

        static string EncodeValue(object value)
        {
            if (value == null)
                return "null";
            if (value is bool flag)
                return flag ? "true" : "false";
            if (value is string text)
                return "\"" + Escape(text) + "\"";
            if (value is int || value is long)
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is double number)
                return number.ToString("G17", CultureInfo.InvariantCulture);
            if (value is float single)
                return ((double)single).ToString("G17", CultureInfo.InvariantCulture);
            return "\"" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture)) + "\"";
        }

        static string Escape(string value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        void OnDisable()
        {
            Flush();
            if (writer == null)
                return;
            writer.Dispose();
            writer = null;
        }
    }
}
