using System;
using System.Collections.Generic;
using System.Globalization;

namespace Playtest
{
    public sealed class CosmeticItem
    {
        public string Id;
        public string Name;
        public string Slot;
        public string SpritePath;
        public string UnlockCondition;
        public bool Premium;
    }

    public sealed class CosmeticsConfig
    {
        public const int SupportedSchemaVersion = 1;

        public string[] Slots;
        public CosmeticItem[] Items;

        public static CosmeticsConfig Parse(string json)
        {
            JsonValue root = JsonMini.Parse(json);
            int schema = RequiredInt(root, "schemaVersion");
            if (schema != SupportedSchemaVersion)
                throw new InvalidOperationException("cosmetics.json schemaVersion is " + schema + ".");
            JsonValue slots = Required(root, "slots");
            JsonValue items = Required(root, "items");
            if (slots.Type != JsonValue.Kind.Array || items.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("cosmetics.json slots and items must be arrays");
            CosmeticsConfig config = new CosmeticsConfig
            {
                Slots = new string[slots.Items.Count],
                Items = new CosmeticItem[items.Items.Count],
            };
            HashSet<string> allowed = new HashSet<string>();
            for (int index = 0; index < slots.Items.Count; index++)
            {
                config.Slots[index] = StringItem(slots.Items[index], "slot");
                allowed.Add(config.Slots[index]);
            }
            HashSet<string> ids = new HashSet<string>();
            for (int index = 0; index < items.Items.Count; index++)
            {
                JsonValue item = items.Items[index];
                CosmeticItem cosmetic = new CosmeticItem
                {
                    Id = RequiredString(item, "id"),
                    Name = RequiredString(item, "name"),
                    Slot = RequiredString(item, "slot"),
                    SpritePath = RequiredString(item, "sprite_path"),
                    UnlockCondition = RequiredString(item, "unlock_condition"),
                    Premium = RequiredBool(item, "is_premium"),
                };
                if (!allowed.Contains(cosmetic.Slot))
                    throw new InvalidOperationException("cosmetics.json slot " + cosmetic.Slot + " is not listed");
                if (!ids.Add(cosmetic.Id))
                    throw new InvalidOperationException("cosmetics.json duplicate id " + cosmetic.Id);
                config.Items[index] = cosmetic;
            }
            return config;
        }

        static JsonValue Required(JsonValue node, string key)
        {
            if (node.Type != JsonValue.Kind.Object || !node.Map.ContainsKey(key) || node.Map[key].Type == JsonValue.Kind.Null)
                throw new InvalidOperationException("cosmetics.json missing " + key);
            return node.Map[key];
        }

        static string RequiredString(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.String || string.IsNullOrEmpty(value.Text))
                throw new InvalidOperationException("cosmetics.json " + key + " is not a string");
            return value.Text;
        }

        static string StringItem(JsonValue value, string key)
        {
            if (value.Type != JsonValue.Kind.String || string.IsNullOrEmpty(value.Text))
                throw new InvalidOperationException("cosmetics.json " + key + " is not a string");
            return value.Text;
        }

        static bool RequiredBool(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.Bool)
                throw new InvalidOperationException("cosmetics.json " + key + " is not a boolean");
            return value.Bool;
        }

        static int RequiredInt(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.Number)
                throw new InvalidOperationException("cosmetics.json " + key + " is not a number");
            double rounded = Math.Round(value.Number);
            if (double.IsNaN(value.Number) || Math.Abs(value.Number - rounded) > 1e-6)
                throw new InvalidOperationException("cosmetics.json " + key + " is not an integer");
            return (int)rounded;
        }
    }

    public sealed class SeasonPassConfig
    {
        public const int SupportedSchemaVersion = 1;

        public string SeasonId;
        public string StartDate;
        public string EndDate;
        public string[] Rewards;
        public string PriceTier;

        public static SeasonPassConfig Parse(string json, CosmeticsConfig cosmetics)
        {
            JsonValue root = JsonMini.Parse(json);
            int schema = RequiredInt(root, "schemaVersion");
            if (schema != SupportedSchemaVersion)
                throw new InvalidOperationException("season_pass.json schemaVersion is " + schema + ".");
            SeasonPassConfig config = new SeasonPassConfig
            {
                SeasonId = RequiredString(root, "season_id"),
                StartDate = RequiredDate(root, "start_date"),
                EndDate = RequiredDate(root, "end_date"),
                PriceTier = RequiredString(root, "price_tier"),
            };
            if (string.CompareOrdinal(config.EndDate, config.StartDate) < 0)
                throw new InvalidOperationException("season_pass.json end_date is before start_date");
            JsonValue rewards = Required(root, "rewards");
            if (rewards.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("season_pass.json rewards is not an array");
            HashSet<string> known = new HashSet<string>();
            if (cosmetics != null && cosmetics.Items != null)
            {
                foreach (CosmeticItem item in cosmetics.Items)
                    known.Add(item.Id);
            }
            config.Rewards = new string[rewards.Items.Count];
            for (int index = 0; index < rewards.Items.Count; index++)
            {
                if (rewards.Items[index].Type != JsonValue.Kind.String)
                    throw new InvalidOperationException("season_pass.json reward is not a string");
                string reward = rewards.Items[index].Text;
                if (!known.Contains(reward))
                    throw new InvalidOperationException("season_pass.json reward " + reward + " is not a cosmetic id");
                config.Rewards[index] = reward;
            }
            return config;
        }

        static string RequiredDate(JsonValue node, string key)
        {
            string text = RequiredString(node, key);
            DateTime parsed;
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                throw new InvalidOperationException("season_pass.json " + key + " must be YYYY-MM-DD");
            return text;
        }

        static JsonValue Required(JsonValue node, string key)
        {
            if (node.Type != JsonValue.Kind.Object || !node.Map.ContainsKey(key) || node.Map[key].Type == JsonValue.Kind.Null)
                throw new InvalidOperationException("season_pass.json missing " + key);
            return node.Map[key];
        }

        static string RequiredString(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.String || string.IsNullOrEmpty(value.Text))
                throw new InvalidOperationException("season_pass.json " + key + " is not a string");
            return value.Text;
        }

        static int RequiredInt(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.Number)
                throw new InvalidOperationException("season_pass.json " + key + " is not a number");
            double rounded = Math.Round(value.Number);
            if (double.IsNaN(value.Number) || Math.Abs(value.Number - rounded) > 1e-6)
                throw new InvalidOperationException("season_pass.json " + key + " is not an integer");
            return (int)rounded;
        }
    }
}
