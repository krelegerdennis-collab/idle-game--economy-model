using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Playtest
{
    public sealed class PlaytestController : MonoBehaviour
    {
        const string SaveFileName = "playtest_save.json";
        const string SavePrefsKey = "playtest_save";
        const double ForegroundGapSeconds = 1.0;

        sealed class LabeledButton
        {
            public Button Button;
            public Text Label;
        }

        sealed class GeneratorRow
        {
            public GeneratorConfig Generator;
            public Text Owned;
            public LabeledButton Buy;
        }

        EconomyConfig config;
        PlaytestState state;
        AnalyticsBridge analytics;
        ExportLogs exportLogs;
        Font font;
        Text currencyText;
        Text timeText;
        Text insightText;
        Text errorText;
        LabeledButton prestigeButton;
        readonly List<GeneratorRow> generatorRows = new List<GeneratorRow>();
        readonly Dictionary<string, LabeledButton> contractButtons = new Dictionary<string, LabeledButton>();
        readonly Dictionary<string, LabeledButton> relicButtons = new Dictionary<string, LabeledButton>();
        bool ready;
        bool sessionOpen;
        DateTime sessionStartedAt;
        DateTime? backgroundAt;
        double lastTick;
        string savePath;

        void Awake()
        {
            analytics = ScriptableObject.CreateInstance<AnalyticsBridge>();
            if (Browser())
                analytics.Open(AnalyticsBridge.LogFileName);
            else
                analytics.Open(Path.Combine(Application.persistentDataPath, AnalyticsBridge.LogFileName));
            exportLogs = gameObject.AddComponent<ExportLogs>();
            exportLogs.Bind(analytics);
            savePath = Path.Combine(Application.persistentDataPath, SaveFileName);
        }

        IEnumerator Start()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "economy_config.json");
            string json = null;
            if (path.Contains("://"))
            {
                UnityWebRequest request = UnityWebRequest.Get(path);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error;
                    request.Dispose();
                    ShowError(error);
                    yield break;
                }
                json = request.downloadHandler.text;
                request.Dispose();
            }
            else if (!File.Exists(path))
            {
                ShowError("economy_config.json ontbreekt in StreamingAssets.");
                yield break;
            }
            else
            {
                json = File.ReadAllText(path);
            }

            try
            {
                Boot(json);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }

        void Boot(string json)
        {
            config = EconomyConfig.Parse(json);
            string saved = ReadSave();
            if (saved != null)
                state = PlaytestState.FromJson(config, saved);
            else
                state = PlaytestState.NewGame(config);
            state.BeginSession();
            analytics.SetPlayer(state.PlayerId);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            BuildUi();
            ready = true;
            OpenSession();
            SyncOffers();
            Refresh();
            Save();
        }

        void Update()
        {
            if (!ready || !sessionOpen)
                return;
            double now = Time.realtimeSinceStartupAsDouble;
            double dt = now - lastTick;
            lastTick = now;
            if (dt > ForegroundGapSeconds)
                dt = 0.0;
            state.Tick(dt);
            SyncOffers();
            Refresh();
        }

        void OnApplicationPause(bool paused)
        {
            if (!ready)
                return;
            if (paused)
            {
                CloseSession();
                Save();
                exportLogs.ExportNow();
            }
            else
            {
                CollectOffline();
                OpenSession();
                Refresh();
            }
        }

        void OnApplicationQuit()
        {
            if (!ready)
                return;
            CloseSession();
            Save();
            exportLogs.ExportNow();
        }

        void OpenSession()
        {
            if (sessionOpen)
                return;
            sessionOpen = true;
            sessionStartedAt = DateTime.UtcNow;
            lastTick = Time.realtimeSinceStartupAsDouble;
            analytics.SessionBegin(state.CumulativeActiveSeconds);
        }

        void CloseSession()
        {
            if (!sessionOpen)
                return;
            SyncOffers();
            DeclineStillOpen("session_end");
            double wall = (DateTime.UtcNow - sessionStartedAt).TotalSeconds;
            analytics.SessionEnd(wall, state.Currency);
            analytics.Flush();
            sessionOpen = false;
            backgroundAt = DateTime.UtcNow;
        }

        void CollectOffline()
        {
            if (backgroundAt == null)
                return;
            double away = (DateTime.UtcNow - backgroundAt.Value).TotalSeconds;
            backgroundAt = null;
            double granted = state.OfflineCurrency(away);
            if (granted <= 0.0)
                return;
            state.GrantOffline(granted);
            state.ClaimInsight();
            analytics.OfflineEarningCollected(away, granted);
        }

        void SyncOffers()
        {
            foreach (BranchConfig branch in config.Branches)
            {
                OfferProgress offer = state.Offer(branch.Id);
                bool open = state.OfferIsOpen(branch);
                if (open && !offer.Shown)
                {
                    analytics.ContractOfferShown(branch.Id, state.TotalOwned, state.RunActiveSeconds);
                    state.MarkOfferShown(branch.Id);
                }
                if (!open && offer.Shown && !offer.Purchased && !offer.Declined)
                {
                    analytics.ContractDeclined(branch.Id, state.TotalOwned, "offer_closed", state.RunActiveSeconds);
                    state.MarkOfferDeclined(branch.Id);
                }
            }
        }

        void DeclineStillOpen(string reason)
        {
            foreach (BranchConfig branch in config.Branches)
            {
                OfferProgress offer = state.Offer(branch.Id);
                if (!offer.Shown || offer.Purchased || offer.Declined || !state.OfferIsOpen(branch))
                    continue;
                analytics.ContractDeclined(branch.Id, state.TotalOwned, reason, state.RunActiveSeconds);
                state.MarkOfferDeclined(branch.Id);
            }
        }

        void BuyGenerator(GeneratorConfig generator)
        {
            PurchaseResult result;
            if (!state.TryBuyGenerator(generator, out result))
                return;
            analytics.UpgradePurchase(generator.Id, result.Cost, result.OwnedAfter, result.CurrencyAfter, state.RunActiveSeconds);
            SyncOffers();
            Refresh();
            Save();
        }

        void BuyBranch(BranchConfig branch)
        {
            double since = state.ActiveSecondsSinceOffer(branch.Id);
            double active = state.RunActiveSeconds;
            double owned = state.TotalOwned;
            PurchaseResult result;
            if (!state.TryBuyBranch(branch, out result))
                return;
            analytics.ContractPurchase(branch.Id, owned, since, active);
            SyncOffers();
            Refresh();
            Save();
        }

        void BuyRelic(RelicItemConfig relic)
        {
            RelicPurchaseResult result;
            if (!state.TryBuyRelic(relic, out result))
                return;
            analytics.RelicPurchase(result.RelicId, result.InsightCost, result.InsightAfter, state.RunActiveSeconds);
            Refresh();
            Save();
        }

        void Prestige()
        {
            double active = state.RunActiveSeconds;
            PrestigeOutcome outcome;
            if (!state.TryPrestige(out outcome))
                return;
            analytics.PrestigeTrigger(outcome.LifetimeCurrency, outcome.PointsGranted, outcome.MultiplierAfter, active);
            SyncOffers();
            Refresh();
            Save();
        }

        void Save()
        {
            string json = state.ToJson();
            if (Browser())
            {
                PlayerPrefs.SetString(SavePrefsKey, json);
                PlayerPrefs.Save();
                return;
            }
            File.WriteAllText(savePath, json);
        }

        string ReadSave()
        {
            if (Browser())
            {
                if (!PlayerPrefs.HasKey(SavePrefsKey))
                    return null;
                string saved = PlayerPrefs.GetString(SavePrefsKey);
                return string.IsNullOrEmpty(saved) ? null : saved;
            }
            if (!File.Exists(savePath))
                return null;
            return File.ReadAllText(savePath);
        }

        static bool Browser()
        {
            return Application.platform == RuntimePlatform.WebGLPlayer;
        }

        void Refresh()
        {
            currencyText.text = config.CurrencyId + "  " + FormatAmount(state.Currency);
            insightText.text = config.Relics.CurrencyId + "  " + state.Insight.ToString(CultureInfo.InvariantCulture);
            if (sessionOpen)
                timeText.text = "tijd  " + FormatElapsed((DateTime.UtcNow - sessionStartedAt).TotalSeconds);
            foreach (GeneratorRow row in generatorRows)
            {
                row.Owned.text = row.Generator.Id + "  " + state.OwnedCount(row.Generator.Id).ToString(CultureInfo.InvariantCulture);
                double cost = state.NextGeneratorCost(row.Generator);
                row.Buy.Label.text = row.Generator.Id + "  " + FormatAmount(cost);
                row.Buy.Button.interactable = state.Currency >= cost;
            }
            prestigeButton.Button.gameObject.SetActive(state.PrestigeAvailable());
            prestigeButton.Label.text = "Prestige  x" + FormatAmount(state.PrestigeMultiplierAfterReset());
            foreach (BranchConfig branch in config.Branches)
            {
                LabeledButton button = contractButtons[branch.Id];
                bool open = state.OfferIsOpen(branch);
                button.Button.gameObject.SetActive(open);
                double cost = state.NextBranchCost(branch);
                button.Label.text = "Contract  dip " + FormatAmount(branch.ActiveProductionMultiplier)
                    + "  piek " + FormatAmount(branch.PeakProductionMultiplier)
                    + "  " + FormatAmount(cost);
                button.Button.interactable = state.Currency >= cost;
            }
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                LabeledButton button = relicButtons[relic.Id];
                bool available = state.RelicLevel(relic.Id) < relic.MaxLevel;
                button.Button.gameObject.SetActive(available);
                button.Label.text = "Koop relic " + relic.Id + "  " + relic.InsightCost.ToString(CultureInfo.InvariantCulture)
                    + " " + config.Relics.CurrencyId;
                button.Button.interactable = state.Insight >= relic.InsightCost;
            }
        }

        void BuildUi()
        {
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(RectMask2D));
            scrollObject.transform.SetParent(canvasObject.transform, false);
            Stretch(scrollObject.GetComponent<RectTransform>());
            scrollObject.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.12f, 1f);
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = scrollObject.GetComponent<RectTransform>();

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(scrollObject.transform, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 32, 48);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            currencyText = MakeText(content, 56, 88);
            timeText = MakeText(content, 28, 40);
            insightText = MakeText(content, 32, 48);
            if (Browser())
            {
                LabeledButton download = MakeButton(content, 72, () => exportLogs.ExportNow());
                download.Label.text = "Log downloaden";
            }
            foreach (GeneratorConfig generator in config.Generators)
            {
                GeneratorConfig captured = generator;
                GeneratorRow row = new GeneratorRow
                {
                    Generator = generator,
                    Owned = MakeText(content, 22, 32),
                    Buy = MakeButton(content, 72, () => BuyGenerator(captured)),
                };
                generatorRows.Add(row);
            }
            prestigeButton = MakeButton(content, 88, Prestige);
            prestigeButton.Button.gameObject.SetActive(false);
            foreach (BranchConfig branch in config.Branches)
            {
                BranchConfig captured = branch;
                LabeledButton button = MakeButton(content, 88, () => BuyBranch(captured));
                button.Button.gameObject.SetActive(false);
                contractButtons[branch.Id] = button;
            }
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                RelicItemConfig captured = relic;
                relicButtons[relic.Id] = MakeButton(content, 72, () => BuyRelic(captured));
            }
        }

        void ShowError(string message)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            errorText = MakeText(canvasObject.transform, 32, 400);
            Stretch(errorText.rectTransform);
            errorText.alignment = TextAnchor.UpperLeft;
            errorText.text = message;
        }

        Text MakeText(Transform parent, int size, float height)
        {
            GameObject item = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            item.transform.SetParent(parent, false);
            Text text = item.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            LayoutElement element = item.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return text;
        }

        LabeledButton MakeButton(Transform parent, float height, UnityEngine.Events.UnityAction action)
        {
            GameObject item = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            item.transform.SetParent(parent, false);
            item.GetComponent<Image>().color = Color.white;
            LayoutElement element = item.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            Button button = item.GetComponent<Button>();
            button.onClick.AddListener(action);
            Text label = MakeText(item.transform, 24, height - 8f);
            Stretch(label.rectTransform);
            label.color = Color.black;
            label.alignment = TextAnchor.MiddleCenter;
            label.GetComponent<LayoutElement>().ignoreLayout = true;
            return new LabeledButton { Button = button, Label = label };
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static string FormatAmount(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        static string FormatElapsed(double seconds)
        {
            if (seconds < 0.0 || double.IsNaN(seconds))
                seconds = 0.0;
            int total = (int)seconds;
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;
            int secs = total % 60;
            if (hours > 0)
                return hours.ToString(CultureInfo.InvariantCulture) + ":"
                    + minutes.ToString("00", CultureInfo.InvariantCulture) + ":"
                    + secs.ToString("00", CultureInfo.InvariantCulture);
            return minutes.ToString(CultureInfo.InvariantCulture) + ":"
                + secs.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
