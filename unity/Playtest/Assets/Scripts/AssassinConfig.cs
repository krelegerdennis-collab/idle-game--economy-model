using System;
using System.Collections.Generic;
using UnityEngine;

namespace Playtest
{
    public sealed class SkyTintStop
    {
        public int Owned;
        public Color Color;
    }

    public sealed class AssassinConfig
    {
        public const int SupportedSchemaVersion = 1;

        public string Display;
        public double BaseAmount;
        public double PerGenerator;
        public float CooldownSeconds;
        public float BackstabChance;
        public double BackstabMultiplier;
        public string BackstabLabel;
        public float BackstabFlashSeconds;
        public float ButtonHeight;
        public float ButtonWidthFraction;
        public int LabelSize;

        public string HvtTitle;
        public float HvtFirstDelaySeconds;
        public float HvtIntervalSeconds;
        public float HvtWindowSeconds;
        public int HvtTapsRequired;
        public double HvtReward;
        public float HvtFailCooldownSeconds;

        public float MotionAmplitude;
        public float MotionPeriodSeconds;
        public float VignetteAlpha;
        public int VignetteTexels;
        public float VignetteInner;
        public float PrestigeFlashSeconds;
        public SkyTintStop[] SkyTints;
        public string CurrencyLabel;
        public string InsightLabel;
        public string PrestigeLabel;
        public Dictionary<string, string> Informants;
        public Dictionary<string, string> RelicNames;

        public double Burst(int totalOwned, bool backstab)
        {
            double amount = BaseAmount * (1.0 + totalOwned * PerGenerator);
            if (backstab)
                amount *= BackstabMultiplier;
            return amount;
        }

        public Color TintForOwned(int owned)
        {
            SkyTintStop[] stops = SkyTints;
            if (stops == null || stops.Length == 0)
                return Color.white;
            if (owned <= stops[0].Owned)
                return stops[0].Color;
            int last = stops.Length - 1;
            if (owned >= stops[last].Owned)
                return stops[last].Color;
            for (int index = 1; index < stops.Length; index++)
            {
                if (owned > stops[index].Owned)
                    continue;
                float start = stops[index - 1].Owned;
                float end = stops[index].Owned;
                float span = end - start;
                float t = span <= 0f ? 1f : (owned - start) / span;
                return Color.Lerp(stops[index - 1].Color, stops[index].Color, Mathf.Clamp01(t));
            }
            return stops[last].Color;
        }

        public string InformantName(string id)
        {
            return Lookup(Informants, id);
        }

        public string RelicName(string id)
        {
            return Lookup(RelicNames, id);
        }

        static string Lookup(Dictionary<string, string> names, string id)
        {
            string name;
            if (!string.IsNullOrEmpty(id) && names != null && names.TryGetValue(id, out name))
                return name;
            return id;
        }

        public static AssassinConfig Parse(string json)
        {
            JsonValue root = JsonMini.Parse(json);
            if (root.Type != JsonValue.Kind.Object)
                throw new InvalidOperationException("assassin.json must be an object");
            int schema = RequiredInt(root, "schemaVersion");
            if (schema != SupportedSchemaVersion)
                throw new InvalidOperationException("assassin.json schemaVersion is " + schema + ".");
            JsonValue assassinate = Required(root, "assassinate");
            JsonValue target = Required(root, "highValueTarget");
            JsonValue motion = Required(root, "backgroundMotion");
            JsonValue labels = Required(root, "labels");
            AssassinConfig config = new AssassinConfig
            {
                Display = RequiredText(assassinate, "display"),
                BaseAmount = RequiredPositive(assassinate, "baseAmount"),
                PerGenerator = RequiredNonNegative(assassinate, "perGenerator"),
                CooldownSeconds = (float)RequiredNonNegative(assassinate, "cooldownSeconds"),
                BackstabChance = (float)RequiredUnit(assassinate, "backstabChance"),
                BackstabMultiplier = RequiredPositive(assassinate, "backstabMultiplier"),
                BackstabLabel = RequiredText(assassinate, "backstabLabel"),
                BackstabFlashSeconds = (float)RequiredNonNegative(assassinate, "backstabFlashSeconds"),
                ButtonHeight = (float)RequiredPositive(assassinate, "buttonHeight"),
                ButtonWidthFraction = (float)RequiredFraction(assassinate, "buttonWidthFraction"),
                LabelSize = RequiredInt(assassinate, "labelSize"),
                HvtTitle = RequiredText(target, "title"),
                HvtFirstDelaySeconds = (float)RequiredNonNegative(target, "firstDelaySeconds"),
                HvtIntervalSeconds = (float)RequiredPositive(target, "intervalSeconds"),
                HvtWindowSeconds = (float)RequiredPositive(target, "windowSeconds"),
                HvtTapsRequired = RequiredInt(target, "tapsRequired"),
                HvtReward = RequiredPositive(target, "reward"),
                HvtFailCooldownSeconds = (float)RequiredNonNegative(target, "failCooldownSeconds"),
                MotionAmplitude = (float)RequiredNonNegative(motion, "amplitude"),
                MotionPeriodSeconds = (float)RequiredPositive(motion, "periodSeconds"),
                VignetteAlpha = (float)RequiredUnit(root, "vignetteAlpha"),
                VignetteTexels = RequiredInt(root, "vignetteTexels"),
                VignetteInner = (float)RequiredInner(root, "vignetteInner"),
                PrestigeFlashSeconds = (float)RequiredNonNegative(root, "prestigeFlashSeconds"),
                SkyTints = ReadTints(root),
                CurrencyLabel = RequiredText(labels, "currency"),
                InsightLabel = RequiredText(labels, "insight"),
                PrestigeLabel = RequiredText(labels, "prestige"),
                Informants = ReadNames(labels, "informants"),
                RelicNames = ReadNames(labels, "relics"),
            };
            if (config.LabelSize < 1)
                throw new InvalidOperationException("assassin.json labelSize must be positive");
            if (config.HvtTapsRequired < 1)
                throw new InvalidOperationException("assassin.json tapsRequired must be positive");
            if (config.VignetteTexels < 2)
                throw new InvalidOperationException("assassin.json vignetteTexels must be at least 2");
            return config;
        }

        static Dictionary<string, string> ReadNames(JsonValue labels, string key)
        {
            JsonValue array = Required(labels, key);
            if (array.Type != JsonValue.Kind.Array || array.Items.Count == 0)
                throw new InvalidOperationException("assassin.json " + key + " is empty");
            Dictionary<string, string> names = new Dictionary<string, string>();
            for (int index = 0; index < array.Items.Count; index++)
            {
                JsonValue item = array.Items[index];
                if (item.Type != JsonValue.Kind.Object)
                    throw new InvalidOperationException("assassin.json " + key + " entry is not an object");
                string id = RequiredText(item, "id");
                string name = RequiredText(item, "name");
                if (names.ContainsKey(id))
                    throw new InvalidOperationException("assassin.json duplicate " + key + " " + id);
                names.Add(id, name);
            }
            return names;
        }

        static SkyTintStop[] ReadTints(JsonValue root)
        {
            JsonValue array = Required(root, "skyTints");
            if (array.Type != JsonValue.Kind.Array || array.Items.Count < 2)
                throw new InvalidOperationException("assassin.json skyTints must list at least two stops");
            SkyTintStop[] stops = new SkyTintStop[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
            {
                JsonValue item = array.Items[index];
                if (item.Type != JsonValue.Kind.Object)
                    throw new InvalidOperationException("assassin.json sky tint is not an object");
                stops[index] = new SkyTintStop
                {
                    Owned = RequiredInt(item, "owned"),
                    Color = ReadColor(item, "color"),
                };
            }
            Array.Sort(stops, (left, right) => left.Owned.CompareTo(right.Owned));
            for (int index = 1; index < stops.Length; index++)
            {
                if (stops[index].Owned <= stops[index - 1].Owned)
                    throw new InvalidOperationException("assassin.json sky tints must increase owned");
            }
            return stops;
        }

        static Color ReadColor(JsonValue node, string key)
        {
            string hex = RequiredText(node, key);
            try
            {
                return PresentationConfig.Hex(hex);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("assassin.json color " + hex);
            }
        }

        static JsonValue Required(JsonValue node, string key)
        {
            if (node.Type != JsonValue.Kind.Object || !node.Map.ContainsKey(key) || node.Map[key].Type == JsonValue.Kind.Null)
                throw new InvalidOperationException("assassin.json missing " + key);
            return node.Map[key];
        }

        static string RequiredText(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.String || string.IsNullOrEmpty(value.Text))
                throw new InvalidOperationException("assassin.json " + key + " must be text");
            return value.Text;
        }

        static double RequiredNumber(JsonValue node, string key)
        {
            JsonValue value = Required(node, key);
            if (value.Type != JsonValue.Kind.Number || double.IsNaN(value.Number) || double.IsInfinity(value.Number))
                throw new InvalidOperationException("assassin.json " + key + " is not a number");
            return value.Number;
        }

        static int RequiredInt(JsonValue node, string key)
        {
            double value = RequiredNumber(node, key);
            double rounded = Math.Round(value);
            if (Math.Abs(value - rounded) > 1e-6)
                throw new InvalidOperationException("assassin.json " + key + " is not an integer");
            return (int)rounded;
        }

        static double RequiredPositive(JsonValue node, string key)
        {
            double value = RequiredNumber(node, key);
            if (value <= 0.0)
                throw new InvalidOperationException("assassin.json " + key + " must be positive");
            return value;
        }

        static double RequiredNonNegative(JsonValue node, string key)
        {
            double value = RequiredNumber(node, key);
            if (value < 0.0)
                throw new InvalidOperationException("assassin.json " + key + " must be zero or more");
            return value;
        }

        static double RequiredUnit(JsonValue node, string key)
        {
            double value = RequiredNumber(node, key);
            if (value < 0.0 || value > 1.0)
                throw new InvalidOperationException("assassin.json " + key + " must be from 0 to 1");
            return value;
        }

        static double RequiredFraction(JsonValue node, string key)
        {
            double value = RequiredNumber(node, key);
            if (value <= 0.0 || value >= 1.0)
                throw new InvalidOperationException("assassin.json " + key + " must be between 0 and 1");
            return value;
        }

        static double RequiredInner(JsonValue node, string key)
        {
            double value = RequiredNumber(node, key);
            if (value < 0.0 || value >= 1.0)
                throw new InvalidOperationException("assassin.json " + key + " must be from 0 up to 1");
            return value;
        }
    }
}
