using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Playtest
{
    public sealed class ActorVisual
    {
        public string Id;
        public Color Color;
        public int Width;
        public int Height;
        public float X;
        public float Y;
        public string SpritePath;
    }

    public sealed class ClassChoice
    {
        public string Id;
        public string Name;
        public string SpritePath;
    }

    public sealed class PresentationConfig
    {
        public const int SupportedSchemaVersion = 1;

        public Color Background;
        public Color Text;
        public Color Currency;
        public Color Critical;
        public Color Bar;
        public Color BarTrack;
        public Color Button;
        public Color ButtonText;
        public Color Glow;
        public float GlowDistance;
        public float GlowAlpha;
        public Color[] IconPalette;

        public int CurrencySize;
        public int GeneratorSize;
        public int BodySize;
        public float CurrencyMinHeight;
        public float BodyMinHeight;
        public float ButtonMinHeight;
        public float ClassButton;
        public float BarHeight;

        public float ReferenceWidth;
        public float ReferenceHeight;
        public int Padding;
        public float Spacing;
        public float StageFraction;
        public float MatchWidthOrHeight;

        public float FloatSeconds;
        public float FloatRise;
        public float ShakeSeconds;
        public float ShakePixels;
        public float ShakeFrequency;
        public float CameraUnitsPerPixel;
        public int ConfettiCount;
        public float ConfettiSeconds;
        public float ConfettiSpeed;
        public float ConfettiSize;
        public float ConfettiRadius;
        public double LargePurchaseCostShare;

        public string IdleTrack;
        public string BossTrack;
        public string ClickSfx;
        public string UpgradeSfx;
        public string CritSfx;
        public string[] Extensions;
        public float BossApproachRatio;
        public float IdleHz;
        public float BossHz;
        public float ClickHz;
        public float UpgradeHz;
        public float CritHz;
        public float LoopSeconds;
        public float SfxSeconds;
        public int SampleRate;
        public float Amplitude;

        public string AnimatorResource;
        public float PixelsPerUnit;
        public float CueSeconds;
        public string IdleState;
        public string[] States;
        public string PurchaseHero;
        public string PurchaseEnemy;
        public string PrestigeHero;
        public string PrestigeEnemy;
        public string MilestoneHero;
        public string MilestoneEnemy;
        public ActorVisual Hero;
        public ActorVisual[] Enemies;
        public Dictionary<string, float> Poses;

        public string TimeLabel;
        public string ContractLabel;
        public string PrestigeLabel;
        public string RelicLabel;
        public string DownloadLabel;
        public string DipLabel;
        public string PeakLabel;
        public string PremiumLabel;
        public string SeasonLabel;

        public string StageBackground;
        public string CurrencyIcon;
        public string InsightIcon;
        public string PrestigeIcon;
        public string ClassPrefKey;
        public string DefaultClass;
        public ClassChoice[] Classes;

        public Color IconColor(int index)
        {
            if (IconPalette == null || IconPalette.Length == 0)
                return Text;
            int wrapped = index % IconPalette.Length;
            if (wrapped < 0)
                wrapped += IconPalette.Length;
            return IconPalette[wrapped];
        }

        public float PoseScale(string state)
        {
            float scale;
            if (Poses != null && Poses.TryGetValue(state, out scale))
                return scale;
            return 1f;
        }

        public float ToneHz(string id)
        {
            if (id == IdleTrack)
                return IdleHz;
            if (id == BossTrack)
                return BossHz;
            if (id == ClickSfx)
                return ClickHz;
            if (id == UpgradeSfx)
                return UpgradeHz;
            if (id == CritSfx)
                return CritHz;
            return ClickHz;
        }

        public bool Loops(string id)
        {
            return id == IdleTrack || id == BossTrack;
        }

        public ClassChoice FindClass(string id)
        {
            if (Classes == null || string.IsNullOrEmpty(id))
                return null;
            for (int index = 0; index < Classes.Length; index++)
            {
                if (Classes[index].Id == id)
                    return Classes[index];
            }
            return null;
        }

        public List<string> PicturePaths()
        {
            List<string> paths = new List<string>();
            AddPath(paths, Hero == null ? null : Hero.SpritePath);
            AddPath(paths, StageBackground);
            AddPath(paths, CurrencyIcon);
            AddPath(paths, InsightIcon);
            AddPath(paths, PrestigeIcon);
            if (Classes != null)
            {
                for (int index = 0; index < Classes.Length; index++)
                    AddPath(paths, Classes[index].SpritePath);
            }
            if (Enemies != null)
            {
                for (int index = 0; index < Enemies.Length; index++)
                    AddPath(paths, Enemies[index].SpritePath);
            }
            return paths;
        }

        static void AddPath(List<string> paths, string path)
        {
            if (string.IsNullOrEmpty(path) || paths.Contains(path))
                return;
            paths.Add(path);
        }

        public static PresentationConfig Parse(string json)
        {
            JsonValue root = JsonMini.Parse(json);
            int schema = RequiredInt(root, "schemaVersion", "presentation.json");
            if (schema != SupportedSchemaVersion)
                throw new InvalidOperationException("presentation.json schemaVersion is " + schema + ".");
            JsonValue theme = Required(root, "theme", "presentation.json");
            JsonValue type = Required(root, "type", "presentation.json");
            JsonValue layout = Required(root, "layout", "presentation.json");
            JsonValue feedback = Required(root, "feedback", "presentation.json");
            JsonValue audio = Required(root, "audio", "presentation.json");
            JsonValue tone = Required(audio, "placeholder", "presentation.json");
            JsonValue actors = Required(root, "actors", "presentation.json");
            JsonValue art = Required(root, "art", "presentation.json");
            JsonValue copy = Required(root, "copy", "presentation.json");
            PresentationConfig config = new PresentationConfig
            {
                Background = ColorFrom(theme, "background"),
                Text = ColorFrom(theme, "text"),
                Currency = ColorFrom(theme, "currency"),
                Critical = ColorFrom(theme, "critical"),
                Bar = ColorFrom(theme, "bar"),
                BarTrack = ColorFrom(theme, "barTrack"),
                Button = ColorFrom(theme, "button"),
                ButtonText = ColorFrom(theme, "buttonText"),
                Glow = ColorFrom(theme, "glow"),
                GlowDistance = RequiredFloat(theme, "glowDistance", "presentation.json"),
                GlowAlpha = RequiredFloat(theme, "glowAlpha", "presentation.json"),
                IconPalette = ReadPalette(theme),
                CurrencySize = RequiredInt(type, "currencySize", "presentation.json"),
                GeneratorSize = RequiredInt(type, "generatorSize", "presentation.json"),
                BodySize = RequiredInt(type, "bodySize", "presentation.json"),
                CurrencyMinHeight = RequiredFloat(type, "currencyMinHeight", "presentation.json"),
                BodyMinHeight = RequiredFloat(type, "bodyMinHeight", "presentation.json"),
                ButtonMinHeight = RequiredFloat(type, "buttonMinHeight", "presentation.json"),
                ClassButton = RequiredFloat(type, "classButton", "presentation.json"),
                BarHeight = RequiredFloat(type, "barHeight", "presentation.json"),
                ReferenceWidth = RequiredFloat(layout, "referenceWidth", "presentation.json"),
                ReferenceHeight = RequiredFloat(layout, "referenceHeight", "presentation.json"),
                Padding = RequiredInt(layout, "padding", "presentation.json"),
                Spacing = RequiredFloat(layout, "spacing", "presentation.json"),
                StageFraction = RequiredFloat(layout, "stageFraction", "presentation.json"),
                MatchWidthOrHeight = RequiredFloat(layout, "matchWidthOrHeight", "presentation.json"),
                FloatSeconds = RequiredFloat(feedback, "floatSeconds", "presentation.json"),
                FloatRise = RequiredFloat(feedback, "floatRise", "presentation.json"),
                ShakeSeconds = RequiredFloat(feedback, "shakeSeconds", "presentation.json"),
                ShakePixels = RequiredFloat(feedback, "shakePixels", "presentation.json"),
                ShakeFrequency = RequiredFloat(feedback, "shakeFrequency", "presentation.json"),
                CameraUnitsPerPixel = RequiredFloat(feedback, "cameraUnitsPerPixel", "presentation.json"),
                ConfettiCount = RequiredInt(feedback, "confettiCount", "presentation.json"),
                ConfettiSeconds = RequiredFloat(feedback, "confettiSeconds", "presentation.json"),
                ConfettiSpeed = RequiredFloat(feedback, "confettiSpeed", "presentation.json"),
                ConfettiSize = RequiredFloat(feedback, "confettiSize", "presentation.json"),
                ConfettiRadius = RequiredFloat(feedback, "confettiRadius", "presentation.json"),
                LargePurchaseCostShare = RequiredFloat(feedback, "largePurchaseCostShare", "presentation.json"),
                IdleTrack = RequiredString(audio, "idleTrack", "presentation.json"),
                BossTrack = RequiredString(audio, "bossTrack", "presentation.json"),
                ClickSfx = RequiredString(audio, "clickSfx", "presentation.json"),
                UpgradeSfx = RequiredString(audio, "upgradeSfx", "presentation.json"),
                CritSfx = RequiredString(audio, "critSfx", "presentation.json"),
                Extensions = ReadExtensions(audio),
                BossApproachRatio = RequiredFloat(audio, "bossApproachRatio", "presentation.json"),
                IdleHz = RequiredFloat(tone, "idleHz", "presentation.json"),
                BossHz = RequiredFloat(tone, "bossHz", "presentation.json"),
                ClickHz = RequiredFloat(tone, "clickHz", "presentation.json"),
                UpgradeHz = RequiredFloat(tone, "upgradeHz", "presentation.json"),
                CritHz = RequiredFloat(tone, "critHz", "presentation.json"),
                LoopSeconds = RequiredFloat(tone, "loopSeconds", "presentation.json"),
                SfxSeconds = RequiredFloat(tone, "sfxSeconds", "presentation.json"),
                SampleRate = RequiredInt(tone, "sampleRate", "presentation.json"),
                Amplitude = RequiredFloat(tone, "amplitude", "presentation.json"),
                AnimatorResource = RequiredString(actors, "animatorResource", "presentation.json"),
                PixelsPerUnit = RequiredFloat(actors, "pixelsPerUnit", "presentation.json"),
                CueSeconds = RequiredFloat(actors, "cueSeconds", "presentation.json"),
                IdleState = RequiredString(actors, "idleState", "presentation.json"),
                States = ReadStates(actors),
                PurchaseHero = RequiredString(actors, "purchaseHero", "presentation.json"),
                PurchaseEnemy = RequiredString(actors, "purchaseEnemy", "presentation.json"),
                PrestigeHero = RequiredString(actors, "prestigeHero", "presentation.json"),
                PrestigeEnemy = RequiredString(actors, "prestigeEnemy", "presentation.json"),
                MilestoneHero = RequiredString(actors, "milestoneHero", "presentation.json"),
                MilestoneEnemy = RequiredString(actors, "milestoneEnemy", "presentation.json"),
                Hero = ReadActor(Required(actors, "hero", "presentation.json")),
                Enemies = ReadActors(actors),
                Poses = ReadPoses(actors),
                TimeLabel = RequiredString(copy, "time", "presentation.json"),
                ContractLabel = RequiredString(copy, "contract", "presentation.json"),
                PrestigeLabel = RequiredString(copy, "prestige", "presentation.json"),
                RelicLabel = RequiredString(copy, "relic", "presentation.json"),
                DownloadLabel = RequiredString(copy, "download", "presentation.json"),
                DipLabel = RequiredString(copy, "dip", "presentation.json"),
                PeakLabel = RequiredString(copy, "peak", "presentation.json"),
                PremiumLabel = RequiredString(copy, "premium", "presentation.json"),
                SeasonLabel = RequiredString(copy, "season", "presentation.json"),
                StageBackground = RequiredString(art, "stageBackground", "presentation.json"),
                CurrencyIcon = RequiredString(art, "currencyIcon", "presentation.json"),
                InsightIcon = RequiredString(art, "insightIcon", "presentation.json"),
                PrestigeIcon = RequiredString(art, "prestigeIcon", "presentation.json"),
                ClassPrefKey = RequiredString(art, "classPrefKey", "presentation.json"),
                DefaultClass = RequiredString(art, "defaultClass", "presentation.json"),
                Classes = ReadClasses(art),
            };
            if (config.CurrencySize < 48)
                throw new InvalidOperationException("presentation.json currencySize must be at least 48.");
            if (config.GeneratorSize < 24)
                throw new InvalidOperationException("presentation.json generatorSize must be at least 24.");
            return config;
        }

        static ClassChoice[] ReadClasses(JsonValue art)
        {
            JsonValue array = Required(art, "classes", "presentation.json");
            if (array.Type != JsonValue.Kind.Array || array.Items.Count == 0)
                throw new InvalidOperationException("presentation.json art.classes is empty");
            ClassChoice[] classes = new ClassChoice[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
            {
                JsonValue item = array.Items[index];
                classes[index] = new ClassChoice
                {
                    Id = RequiredString(item, "id", "presentation.json"),
                    Name = RequiredString(item, "name", "presentation.json"),
                    SpritePath = RequiredString(item, "sprite_path", "presentation.json"),
                };
            }
            return classes;
        }

        static Color[] ReadPalette(JsonValue theme)
        {
            JsonValue array = Required(theme, "iconPalette", "presentation.json");
            if (array.Type != JsonValue.Kind.Array || array.Items.Count == 0)
                throw new InvalidOperationException("presentation.json theme.iconPalette is empty");
            Color[] colors = new Color[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
            {
                if (array.Items[index].Type != JsonValue.Kind.String)
                    throw new InvalidOperationException("presentation.json icon color is not a string");
                colors[index] = Hex(array.Items[index].Text);
            }
            return colors;
        }

        static string[] ReadExtensions(JsonValue audio)
        {
            JsonValue array = Required(audio, "extensions", "presentation.json");
            if (array.Type != JsonValue.Kind.Array || array.Items.Count == 0)
                throw new InvalidOperationException("presentation.json audio.extensions is empty");
            string[] extensions = new string[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
                extensions[index] = array.Items[index].Type == JsonValue.Kind.String
                    ? array.Items[index].Text
                    : "";
            return extensions;
        }

        static string[] ReadStates(JsonValue actors)
        {
            JsonValue array = Required(actors, "states", "presentation.json");
            if (array.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("presentation.json actors.states is not an array");
            string[] states = new string[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
                states[index] = RequiredItemString(array.Items[index], "state");
            return states;
        }

        static ActorVisual[] ReadActors(JsonValue actors)
        {
            JsonValue array = Required(actors, "enemies", "presentation.json");
            if (array.Type != JsonValue.Kind.Array)
                throw new InvalidOperationException("presentation.json actors.enemies is not an array");
            ActorVisual[] visuals = new ActorVisual[array.Items.Count];
            for (int index = 0; index < array.Items.Count; index++)
                visuals[index] = ReadActor(array.Items[index]);
            return visuals;
        }

        static ActorVisual ReadActor(JsonValue item)
        {
            return new ActorVisual
            {
                Id = RequiredString(item, "id", "presentation.json"),
                Color = ColorFrom(item, "color"),
                Width = RequiredInt(item, "width", "presentation.json"),
                Height = RequiredInt(item, "height", "presentation.json"),
                X = RequiredFloat(item, "x", "presentation.json"),
                Y = RequiredFloat(item, "y", "presentation.json"),
                SpritePath = item.Has("sprite_path") ? RequiredString(item, "sprite_path", "presentation.json") : null,
            };
        }

        static Dictionary<string, float> ReadPoses(JsonValue actors)
        {
            JsonValue poses = Required(actors, "poses", "presentation.json");
            if (poses.Type != JsonValue.Kind.Object)
                throw new InvalidOperationException("presentation.json actors.poses is not an object");
            Dictionary<string, float> scales = new Dictionary<string, float>();
            foreach (KeyValuePair<string, JsonValue> pair in poses.Map)
            {
                if (pair.Value.Type != JsonValue.Kind.Number)
                    throw new InvalidOperationException("presentation.json pose " + pair.Key + " is not a number");
                scales[pair.Key] = (float)pair.Value.Number;
            }
            return scales;
        }

        static Color ColorFrom(JsonValue node, string key)
        {
            return Hex(RequiredString(node, key, "presentation.json"));
        }

        public static Color Hex(string hex)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#')
                throw new InvalidOperationException("presentation.json color " + hex);
            byte red = byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new Color32(red, green, blue, 255);
        }

        static string RequiredItemString(JsonValue value, string key)
        {
            if (value.Type != JsonValue.Kind.String || string.IsNullOrEmpty(value.Text))
                throw new InvalidOperationException("presentation.json " + key + " is not a string");
            return value.Text;
        }

        static JsonValue Required(JsonValue node, string key, string file)
        {
            if (node.Type != JsonValue.Kind.Object || !node.Map.ContainsKey(key) || node.Map[key].Type == JsonValue.Kind.Null)
                throw new InvalidOperationException(file + " missing " + key);
            return node.Map[key];
        }

        static string RequiredString(JsonValue node, string key, string file)
        {
            JsonValue value = Required(node, key, file);
            if (value.Type != JsonValue.Kind.String)
                throw new InvalidOperationException(file + " " + key + " is not a string");
            return value.Text;
        }

        static int RequiredInt(JsonValue node, string key, string file)
        {
            double value = RequiredFloat(node, key, file);
            double rounded = Math.Round(value);
            if (double.IsNaN(value) || Math.Abs(value - rounded) > 1e-6)
                throw new InvalidOperationException(file + " " + key + " is not an integer");
            return (int)rounded;
        }

        static float RequiredFloat(JsonValue node, string key, string file)
        {
            JsonValue value = Required(node, key, file);
            if (value.Type != JsonValue.Kind.Number)
                throw new InvalidOperationException(file + " " + key + " is not a number");
            return (float)value.Number;
        }
    }
}
