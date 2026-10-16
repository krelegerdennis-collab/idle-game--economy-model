using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
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
            public RectTransform Rect;
        }

        sealed class GeneratorRow
        {
            public GeneratorConfig Generator;
            public Text Owned;
            public Image BarFill;
            public LabeledButton Buy;
        }

        EconomyConfig config;
        PresentationConfig presentation;
        CosmeticsConfig cosmetics;
        SeasonPassConfig seasonPass;
        PlaytestState state;
        AnalyticsBridge analytics;
        ExportLogs exportLogs;
        AudioManager audioManager;
        PresentationFeedback feedback;
        CharacterController hero;
        EnemyController enemies;
        Font font;
        TMP_FontAsset tmpFont;
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
        readonly Dictionary<string, byte[]> pictures = new Dictionary<string, byte[]>();
        readonly Dictionary<string, Outline> classOutlines = new Dictionary<string, Outline>();
        string selectedClass;
        Text classNameText;

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
            string economy = null;
            string theme = null;
            string cosmetic = null;
            string season = null;
            yield return ReadStreaming("economy_config.json", text => economy = text);
            if (economy == null)
                yield break;
            yield return ReadStreaming("presentation.json", text => theme = text);
            if (theme == null)
                yield break;
            yield return ReadStreaming("cosmetics.json", text => cosmetic = text);
            if (cosmetic == null)
                yield break;
            yield return ReadStreaming("season_pass.json", text => season = text);
            if (season == null)
                yield break;
            PresentationConfig parsed = null;
            try
            {
                parsed = PresentationConfig.Parse(theme);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
                yield break;
            }
            List<string> paths = parsed.PicturePaths();
            for (int index = 0; index < paths.Count; index++)
            {
                string picturePath = paths[index];
                byte[] data = null;
                yield return ReadStreamingBytes(picturePath, bytes => data = bytes);
                if (data != null && data.Length > 0)
                    pictures[picturePath] = data;
            }
            try
            {
                Boot(economy, theme, cosmetic, season);
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }

        IEnumerator ReadStreaming(string fileName, Action<string> sink)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (path.Contains("://"))
            {
                UnityWebRequest request = UnityWebRequest.Get(path);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string error = request.error;
                    request.Dispose();
                    ShowError(fileName + " " + error);
                    yield break;
                }
                sink(request.downloadHandler.text);
                request.Dispose();
                yield break;
            }
            if (!File.Exists(path))
            {
                ShowError(fileName + " ontbreekt in StreamingAssets.");
                yield break;
            }
            sink(File.ReadAllText(path));
        }

        IEnumerator ReadStreamingBytes(string fileName, Action<byte[]> sink)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (path.Contains("://"))
            {
                UnityWebRequest request = UnityWebRequest.Get(path);
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                    sink(request.downloadHandler.data);
                request.Dispose();
                yield break;
            }
            if (File.Exists(path))
                sink(File.ReadAllBytes(path));
        }

        void Boot(string economy, string theme, string cosmetic, string season)
        {
            config = EconomyConfig.Parse(economy);
            presentation = PresentationConfig.Parse(theme);
            cosmetics = CosmeticsConfig.Parse(cosmetic);
            seasonPass = SeasonPassConfig.Parse(season, cosmetics);
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
            try
            {
                if (font != null)
                    tmpFont = TMP_FontAsset.CreateFontAsset(font);
            }
            catch (Exception)
            {
                tmpFont = null;
            }
            Camera view = Camera.main;
            if (view != null)
                view.backgroundColor = presentation.Background;
            audioManager = ScriptableObject.CreateInstance<AudioManager>();
            audioManager.Open(presentation, gameObject);
            selectedClass = presentation.DefaultClass;
            if (!string.IsNullOrEmpty(presentation.ClassPrefKey) && PlayerPrefs.HasKey(presentation.ClassPrefKey))
            {
                string savedClass = PlayerPrefs.GetString(presentation.ClassPrefKey);
                if (presentation.FindClass(savedClass) != null)
                    selectedClass = savedClass;
            }
            RectTransform shakeRoot = BuildUi();
            feedback = gameObject.AddComponent<PresentationFeedback>();
            feedback.Bind(presentation, shakeRoot, view, tmpFont, font);
            SpawnActors();
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
            if (audioManager != null)
                audioManager.SetBoss(ApproachingPrestige());
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
            double before = state.Currency;
            PurchaseResult result;
            if (!state.TryBuyGenerator(generator, out result))
                return;
            analytics.UpgradePurchase(generator.Id, result.Cost, result.OwnedAfter, result.CurrencyAfter, state.RunActiveSeconds);
            GeneratorRow row = RowFor(generator.Id);
            Juice(result.Cost, before, CrossedMilestone(result.OwnedAfter), row == null ? null : row.Buy.Rect);
            SyncOffers();
            Refresh();
            Save();
        }

        void BuyBranch(BranchConfig branch)
        {
            double since = state.ActiveSecondsSinceOffer(branch.Id);
            double active = state.RunActiveSeconds;
            double owned = state.TotalOwned;
            double before = state.Currency;
            PurchaseResult result;
            if (!state.TryBuyBranch(branch, out result))
                return;
            analytics.ContractPurchase(branch.Id, owned, since, active);
            LabeledButton button;
            contractButtons.TryGetValue(branch.Id, out button);
            Juice(result.Cost, before, false, button == null ? null : button.Rect);
            SyncOffers();
            Refresh();
            Save();
        }

        void BuyRelic(RelicItemConfig relic)
        {
            double before = state.Insight;
            RelicPurchaseResult result;
            if (!state.TryBuyRelic(relic, out result))
                return;
            analytics.RelicPurchase(result.RelicId, result.InsightCost, result.InsightAfter, state.RunActiveSeconds);
            LabeledButton button;
            relicButtons.TryGetValue(relic.Id, out button);
            Juice(result.InsightCost, before, false, button == null ? null : button.Rect);
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
            if (feedback != null)
            {
                feedback.Confetti();
                feedback.Shake();
                feedback.Float(
                    presentation.PrestigeLabel,
                    presentation.Currency,
                    prestigeButton == null ? null : prestigeButton.Rect);
            }
            if (audioManager != null)
                audioManager.PlayCrit();
            if (hero != null)
                hero.Cue(presentation.PrestigeHero);
            if (enemies != null)
                enemies.Cue(presentation.PrestigeEnemy);
            SyncOffers();
            Refresh();
            Save();
        }

        void Juice(double cost, double before, bool milestone, RectTransform anchor)
        {
            bool large = LargePurchase(cost, before) || milestone;
            if (feedback != null)
            {
                feedback.Float(FormatAmount(cost), large ? presentation.Critical : presentation.Currency, anchor);
                if (large)
                    feedback.Shake();
                if (milestone)
                    feedback.Confetti();
            }
            if (audioManager != null)
            {
                if (large)
                    audioManager.PlayCrit();
                else
                    audioManager.PlayUpgrade();
            }
            if (hero != null)
                hero.Cue(milestone ? presentation.MilestoneHero : presentation.PurchaseHero);
            if (enemies != null)
                enemies.Cue(milestone ? presentation.MilestoneEnemy : presentation.PurchaseEnemy);
        }

        bool LargePurchase(double cost, double before)
        {
            return before > 0.0 && cost >= before * presentation.LargePurchaseCostShare;
        }

        bool CrossedMilestone(int ownedAfter)
        {
            if (config.Milestones == null)
                return false;
            for (int index = 0; index < config.Milestones.Length; index++)
            {
                if (config.Milestones[index].OwnedAtLeast == ownedAfter)
                    return true;
            }
            return false;
        }

        bool ApproachingPrestige()
        {
            PrestigeConfig prestige = config.Prestige;
            if (prestige.Coeff <= 0.0 || prestige.Exponent <= 0.0 || prestige.Divisor <= 0.0)
                return false;
            double needed = prestige.Divisor * Math.Pow(
                prestige.MinPointsToPrestige / prestige.Coeff,
                1.0 / prestige.Exponent);
            return state.LifetimeCurrency >= needed * presentation.BossApproachRatio;
        }

        GeneratorRow RowFor(string id)
        {
            for (int index = 0; index < generatorRows.Count; index++)
            {
                if (generatorRows[index].Generator.Id == id)
                    return generatorRows[index];
            }
            return null;
        }

        void SpawnActors()
        {
            RuntimeAnimatorController controller = Resources.Load<RuntimeAnimatorController>(presentation.AnimatorResource);
            GameObject heroObject = new GameObject(presentation.Hero.Id);
            hero = heroObject.AddComponent<CharacterController>();
            byte[] heroPng = Picture(SelectedSpritePath());
            hero.Bind(presentation, presentation.Hero, controller, heroPng);
            GameObject enemyObject = new GameObject("Enemies");
            enemies = enemyObject.AddComponent<EnemyController>();
            enemies.Bind(presentation, controller, pictures);
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
            currencyText.color = presentation.Currency;
            insightText.text = config.Relics.CurrencyId + "  " + state.Insight.ToString(CultureInfo.InvariantCulture);
            insightText.color = presentation.Text;
            if (sessionOpen)
                timeText.text = presentation.TimeLabel + "  " + FormatElapsed((DateTime.UtcNow - sessionStartedAt).TotalSeconds);
            foreach (GeneratorRow row in generatorRows)
            {
                int owned = state.OwnedCount(row.Generator.Id);
                row.Owned.text = row.Generator.Id + "  " + owned.ToString(CultureInfo.InvariantCulture);
                row.Owned.color = presentation.Text;
                int next = NextMilestone(owned);
                float ratio = next <= 0 ? 0f : Mathf.Clamp01(owned / (float)next);
                row.BarFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                double cost = state.NextGeneratorCost(row.Generator);
                row.Buy.Label.text = row.Generator.Id + "  " + FormatAmount(cost);
                row.Buy.Button.interactable = state.Currency >= cost;
            }
            prestigeButton.Button.gameObject.SetActive(state.PrestigeAvailable());
            prestigeButton.Label.text = presentation.PrestigeLabel + "  x" + FormatAmount(state.PrestigeMultiplierAfterReset());
            foreach (BranchConfig branch in config.Branches)
            {
                LabeledButton button = contractButtons[branch.Id];
                bool open = state.OfferIsOpen(branch);
                button.Button.gameObject.SetActive(open);
                double cost = state.NextBranchCost(branch);
                button.Label.text = presentation.ContractLabel
                    + "  " + presentation.DipLabel + " " + FormatAmount(branch.ActiveProductionMultiplier)
                    + "  " + presentation.PeakLabel + " " + FormatAmount(branch.PeakProductionMultiplier)
                    + "  " + FormatAmount(cost);
                button.Button.interactable = state.Currency >= cost;
            }
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                LabeledButton button = relicButtons[relic.Id];
                bool available = state.RelicLevel(relic.Id) < relic.MaxLevel;
                button.Button.gameObject.SetActive(available);
                button.Label.text = presentation.RelicLabel + " " + relic.Id + "  "
                    + relic.InsightCost.ToString(CultureInfo.InvariantCulture)
                    + " " + config.Relics.CurrencyId;
                button.Button.interactable = state.Insight >= relic.InsightCost;
            }
        }

        int NextMilestone(int owned)
        {
            int next = 0;
            bool found = false;
            if (config.Milestones == null)
                return owned > 0 ? owned : 1;
            for (int index = 0; index < config.Milestones.Length; index++)
            {
                int at = config.Milestones[index].OwnedAtLeast;
                if (at > owned && (!found || at < next))
                {
                    next = at;
                    found = true;
                }
            }
            if (found)
                return next;
            return owned > 0 ? owned : 1;
        }

        RectTransform BuildUi()
        {
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(presentation.ReferenceWidth, presentation.ReferenceHeight);
            scaler.matchWidthOrHeight = presentation.MatchWidthOrHeight;

            GameObject rootObject = new GameObject("Shake", typeof(RectTransform), typeof(VerticalLayoutGroup));
            rootObject.transform.SetParent(canvasObject.transform, false);
            RectTransform shakeRoot = rootObject.GetComponent<RectTransform>();
            Stretch(shakeRoot);
            VerticalLayoutGroup column = rootObject.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(presentation.Padding, presentation.Padding, presentation.Padding, presentation.Padding);
            column.spacing = presentation.Spacing;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            GameObject stage = new GameObject("Stage", typeof(RectTransform), typeof(LayoutElement));
            stage.transform.SetParent(shakeRoot, false);
            LayoutElement stageElement = stage.GetComponent<LayoutElement>();
            stageElement.preferredHeight = presentation.ReferenceHeight * presentation.StageFraction;
            stageElement.minHeight = stageElement.preferredHeight;
            byte[] backdrop = Picture(presentation.StageBackground);
            if (backdrop != null)
            {
                Image background = stage.AddComponent<Image>();
                background.sprite = UiSprite(backdrop);
                background.preserveAspect = true;
                background.color = Color.white;
                background.raycastTarget = false;
            }

            BuildClassRow(shakeRoot);
            classNameText = MakeText(shakeRoot, presentation.BodySize, presentation.BodyMinHeight, presentation.Text);
            ClassChoice selected = presentation.FindClass(selectedClass);
            classNameText.text = selected == null ? "" : selected.Name;
            classNameText.alignment = TextAnchor.MiddleCenter;

            currencyText = MakeIconRow(shakeRoot, presentation.CurrencyIcon, presentation.CurrencySize, presentation.CurrencyMinHeight, presentation.Currency);
            timeText = MakeText(shakeRoot, presentation.BodySize, presentation.BodyMinHeight, presentation.Text);
            insightText = MakeIconRow(shakeRoot, presentation.InsightIcon, presentation.BodySize, presentation.BodyMinHeight, presentation.Text);
            if (Browser())
            {
                LabeledButton download = MakeButton(shakeRoot, presentation.ButtonMinHeight, () => exportLogs.ExportNow());
                download.Label.text = presentation.DownloadLabel;
            }

            GameObject scrollObject = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(RectMask2D), typeof(LayoutElement));
            scrollObject.transform.SetParent(shakeRoot, false);
            scrollObject.GetComponent<Image>().color = presentation.Background;
            LayoutElement scrollElement = scrollObject.GetComponent<LayoutElement>();
            scrollElement.flexibleHeight = 1f;
            scrollElement.minHeight = presentation.ButtonMinHeight;
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
            layout.padding = new RectOffset(0, 0, 0, presentation.Padding);
            layout.spacing = presentation.Spacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            int iconIndex = 0;
            foreach (GeneratorConfig generator in config.Generators)
            {
                GeneratorConfig captured = generator;
                MakeIcon(content, presentation.IconColor(iconIndex));
                iconIndex++;
                GeneratorRow row = new GeneratorRow
                {
                    Generator = generator,
                    Owned = MakeText(content, presentation.GeneratorSize, presentation.BodyMinHeight, presentation.Text),
                    BarFill = MakeBar(content),
                    Buy = MakeButton(content, presentation.ButtonMinHeight, () => BuyGenerator(captured)),
                };
                generatorRows.Add(row);
            }
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                RelicItemConfig captured = relic;
                relicButtons[relic.Id] = MakeButton(content, presentation.ButtonMinHeight, () => BuyRelic(captured));
            }
            MakeText(content, presentation.BodySize, presentation.BodyMinHeight, presentation.Text).text =
                presentation.SeasonLabel + "  " + seasonPass.SeasonId + "  " + seasonPass.StartDate + "  " + seasonPass.EndDate;
            foreach (CosmeticItem item in cosmetics.Items)
            {
                string premium = item.Premium ? "  " + presentation.PremiumLabel : "";
                MakeText(content, presentation.BodySize, presentation.BodyMinHeight, presentation.Text).text =
                    item.Name + "  " + item.Slot + premium;
            }

            prestigeButton = MakeButton(shakeRoot, presentation.ButtonMinHeight, Prestige);
            byte[] prestigeIcon = Picture(presentation.PrestigeIcon);
            if (prestigeIcon != null)
            {
                prestigeButton.Button.image.sprite = UiSprite(prestigeIcon);
                prestigeButton.Button.image.color = Color.white;
                ColorBlock colors = prestigeButton.Button.colors;
                colors.normalColor = Color.white;
                colors.selectedColor = Color.white;
                prestigeButton.Button.colors = colors;
            }
            prestigeButton.Button.gameObject.SetActive(false);
            foreach (BranchConfig branch in config.Branches)
            {
                BranchConfig captured = branch;
                LabeledButton button = MakeButton(shakeRoot, presentation.ButtonMinHeight, () => BuyBranch(captured));
                button.Button.gameObject.SetActive(false);
                contractButtons[branch.Id] = button;
            }
            return shakeRoot;
        }

        void BuildClassRow(Transform parent)
        {
            if (presentation.Classes == null || presentation.Classes.Length == 0)
                return;
            GameObject row = new GameObject("Classes", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.minHeight = presentation.ClassButton;
            rowElement.preferredHeight = presentation.ClassButton;
            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            group.spacing = presentation.Spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            for (int index = 0; index < presentation.Classes.Length; index++)
            {
                ClassChoice choice = presentation.Classes[index];
                string classId = choice.Id;
                GameObject item = new GameObject(choice.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(Outline));
                item.transform.SetParent(row.transform, false);
                Image image = item.GetComponent<Image>();
                byte[] png = Picture(choice.SpritePath);
                if (png != null)
                    image.sprite = UiSprite(png);
                image.preserveAspect = true;
                image.color = Color.white;
                LayoutElement element = item.GetComponent<LayoutElement>();
                element.minHeight = presentation.ClassButton;
                element.preferredHeight = presentation.ClassButton;
                Outline outline = item.GetComponent<Outline>();
                outline.effectDistance = new Vector2(presentation.GlowDistance, presentation.GlowDistance);
                classOutlines[choice.Id] = outline;
                Button button = item.GetComponent<Button>();
                button.onClick.AddListener(() =>
                {
                    if (audioManager != null)
                        audioManager.PlayClick();
                    SelectClass(classId);
                });
            }
            RefreshClassChrome();
        }

        void SelectClass(string id)
        {
            if (presentation.FindClass(id) == null || id == selectedClass)
                return;
            selectedClass = id;
            if (!string.IsNullOrEmpty(presentation.ClassPrefKey))
            {
                PlayerPrefs.SetString(presentation.ClassPrefKey, id);
                PlayerPrefs.Save();
            }
            ClassChoice choice = presentation.FindClass(id);
            if (classNameText != null && choice != null)
                classNameText.text = choice.Name;
            if (hero != null)
                hero.SetPicture(Picture(choice == null ? null : choice.SpritePath));
            RefreshClassChrome();
        }

        void RefreshClassChrome()
        {
            foreach (KeyValuePair<string, Outline> pair in classOutlines)
            {
                Color color = pair.Key == selectedClass ? presentation.Currency : presentation.BarTrack;
                color.a = presentation.GlowAlpha;
                pair.Value.effectColor = color;
            }
        }

        string SelectedSpritePath()
        {
            ClassChoice choice = presentation.FindClass(selectedClass);
            if (choice != null)
                return choice.SpritePath;
            return presentation.Hero == null ? null : presentation.Hero.SpritePath;
        }

        byte[] Picture(string path)
        {
            byte[] data;
            if (string.IsNullOrEmpty(path) || !pictures.TryGetValue(path, out data))
                return null;
            return data;
        }

        Text MakeIconRow(Transform parent, string iconPath, int size, float height, Color color)
        {
            GameObject row = new GameObject("IconRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.minHeight = height;
            rowElement.preferredHeight = height;
            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            group.spacing = presentation.Spacing;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            byte[] png = Picture(iconPath);
            if (png != null)
            {
                GameObject icon = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                icon.transform.SetParent(row.transform, false);
                Image image = icon.GetComponent<Image>();
                image.sprite = UiSprite(png);
                image.preserveAspect = true;
                image.color = Color.white;
                image.raycastTarget = false;
                LayoutElement iconElement = icon.GetComponent<LayoutElement>();
                iconElement.minWidth = height;
                iconElement.preferredWidth = height;
                iconElement.minHeight = height;
            }
            Text text = MakeText(row.transform, size, height, color);
            LayoutElement textElement = text.GetComponent<LayoutElement>();
            textElement.minWidth = presentation.ReferenceWidth * 0.5f;
            textElement.flexibleWidth = 1f;
            return text;
        }

        Sprite UiSprite(byte[] png)
        {
            if (png == null || png.Length == 0)
                return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(png))
                return null;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                presentation.PixelsPerUnit);
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
            errorText = MakeText(canvasObject.transform, 32, 400f, Color.white);
            Stretch(errorText.rectTransform);
            errorText.alignment = TextAnchor.UpperLeft;
            errorText.text = message;
        }

        Text MakeText(Transform parent, int size, float height, Color color)
        {
            GameObject item = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            item.transform.SetParent(parent, false);
            Text text = item.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
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
            ColorBlock colors = button.colors;
            colors.normalColor = presentation.Button;
            colors.highlightedColor = presentation.Text;
            colors.pressedColor = presentation.Critical;
            colors.selectedColor = presentation.Button;
            colors.disabledColor = presentation.BarTrack;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                if (audioManager != null)
                    audioManager.PlayClick();
            });
            button.onClick.AddListener(action);
            Text label = MakeText(item.transform, presentation.BodySize, height - presentation.Spacing, presentation.ButtonText);
            Stretch(label.rectTransform);
            label.alignment = TextAnchor.MiddleCenter;
            label.GetComponent<LayoutElement>().ignoreLayout = true;
            return new LabeledButton
            {
                Button = button,
                Label = label,
                Rect = item.GetComponent<RectTransform>(),
            };
        }

        Image MakeIcon(Transform parent, Color color)
        {
            GameObject item = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            item.transform.SetParent(parent, false);
            Image image = item.GetComponent<Image>();
            image.color = color;
            LayoutElement element = item.GetComponent<LayoutElement>();
            element.minHeight = presentation.BarHeight * 2f;
            element.preferredHeight = presentation.BarHeight * 2f;
            element.minWidth = presentation.BarHeight * 2f;
            return image;
        }

        Image MakeBar(Transform parent)
        {
            GameObject track = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            track.transform.SetParent(parent, false);
            track.GetComponent<Image>().color = presentation.BarTrack;
            LayoutElement element = track.GetComponent<LayoutElement>();
            element.minHeight = presentation.BarHeight;
            element.preferredHeight = presentation.BarHeight;
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image), typeof(Outline));
            fill.transform.SetParent(track.transform, false);
            RectTransform rect = fill.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = fill.GetComponent<Image>();
            image.color = presentation.Bar;
            Outline glow = fill.GetComponent<Outline>();
            Color glowColor = presentation.Glow;
            glowColor.a = presentation.GlowAlpha;
            glow.effectColor = glowColor;
            glow.effectDistance = new Vector2(presentation.GlowDistance, presentation.GlowDistance);
            return image;
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
