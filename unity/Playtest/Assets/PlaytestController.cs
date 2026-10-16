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
            public Text Name;
            public Text Owned;
            public Text Cost;
            public Image BarFill;
            public Button Button;
            public RectTransform Rect;
        }

        sealed class ScrollList
        {
            public GameObject Root;
            public RectTransform Content;
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
        RectTransform screenCanvas;
        RectTransform middleBand;
        RectTransform actorStage;
        GameObject generatorScroll;
        ScrollRect generatorScrollRect;
        bool generatorScrollPinned;
        Vector2 laidOutStage;
        GameObject shopPanel;
        AssassinConfig assassin;
        Image stageBackground;
        RectTransform stageBackgroundRect;
        Image backstabFlash;
        Image prestigeFlash;
        float backstabFlashUntil;
        float prestigeFlashUntil;
        LabeledButton assassinateButton;
        Image assassinateFill;
        GameObject hvtWidget;
        Text hvtTitle;
        Text hvtTime;
        double playClock;
        double assassinateReadyAt;
        int hvtTaps;
        double hvtSpawnAt;
        double hvtWindowEnd;
        HvtPhase hvtPhase;
        Sprite solidSprite;

        enum HvtPhase
        {
            Waiting,
            Active,
        }

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
            string assassinText = null;
            yield return ReadStreaming("assassin.json", text => assassinText = text);
            if (assassinText == null)
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
                Boot(economy, theme, cosmetic, season, assassinText);
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

        void Boot(string economy, string theme, string cosmetic, string season, string assassinJson)
        {
            config = EconomyConfig.Parse(economy);
            presentation = PresentationConfig.Parse(theme);
            assassin = AssassinConfig.Parse(assassinJson);
            hvtPhase = HvtPhase.Waiting;
            hvtSpawnAt = assassin.HvtFirstDelaySeconds;
            hvtTaps = 0;
            playClock = 0.0;
            assassinateReadyAt = 0.0;
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
            RectTransform shakeRoot = BuildUi();
            feedback = gameObject.AddComponent<PresentationFeedback>();
            feedback.Bind(presentation, shakeRoot, view, tmpFont, font);
            SpawnActors(actorStage);
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
            AdvanceAssassin(dt);
            if (audioManager != null)
                audioManager.SetBoss(ApproachingPrestige());
            SyncOffers();
            Refresh();
        }

        void LateUpdate()
        {
            if (!ready)
                return;
            LayoutActors();
            PinGenerators(false);
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
            Juice(result.Cost, before, CrossedMilestone(result.OwnedAfter), row == null ? null : row.Rect);
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
            if (assassin != null)
                StartFlash(prestigeFlash, Color.white, assassin.PrestigeFlashSeconds);
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

        void BuildBackdrop(Transform canvas)
        {
            GameObject backdropObject = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdropObject.transform.SetParent(canvas, false);
            Stretch(backdropObject.GetComponent<RectTransform>());
            stageBackgroundRect = backdropObject.GetComponent<RectTransform>();
            stageBackground = backdropObject.GetComponent<Image>();
            byte[] pixels = Picture(presentation.StageBackground);
            if (pixels != null)
                stageBackground.sprite = UiSprite(pixels);
            stageBackground.preserveAspect = false;
            stageBackground.color = assassin == null ? presentation.Background : assassin.TintForOwned(0);
            stageBackground.raycastTarget = false;
            stageBackground.type = Image.Type.Simple;
        }

        void BuildVignette(Transform canvas)
        {
            if (assassin == null || presentation == null)
                return;
            int texels = assassin.VignetteTexels;
            Texture2D texture = new Texture2D(texels, texels, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[texels * texels];
            Color edge = presentation.Background;
            float inner = assassin.VignetteInner;
            float alpha = assassin.VignetteAlpha;
            float span = texels - 1;
            for (int y = 0; y < texels; y++)
            {
                for (int x = 0; x < texels; x++)
                {
                    float nx = span <= 0f ? 0.5f : x / span;
                    float ny = span <= 0f ? 0.5f : y / span;
                    float dx = Mathf.Abs(nx * 2f - 1f);
                    float dy = Mathf.Abs(ny * 2f - 1f);
                    float reach = Mathf.Sqrt(dx * dx + dy * dy);
                    float falloff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, 1f, reach));
                    Color pixel = edge;
                    pixel.a = alpha * falloff;
                    pixels[y * texels + x] = pixel;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            GameObject overlay = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(canvas, false);
            Stretch(overlay.GetComponent<RectTransform>());
            Image image = overlay.GetComponent<Image>();
            image.sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                presentation.PixelsPerUnit);
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.raycastTarget = false;
            image.color = Color.white;
        }

        void SpawnActors(RectTransform stage)
        {
            if (stage == null || presentation == null)
                return;
            RuntimeAnimatorController controller = Resources.Load<RuntimeAnimatorController>(presentation.AnimatorResource);

            GameObject enemyObject = new GameObject("Enemies", typeof(RectTransform));
            enemies = enemyObject.AddComponent<EnemyController>();
            enemies.Bind(presentation, controller, pictures, stage);

            if (presentation.Hero == null)
                return;
            GameObject portrait = new GameObject(presentation.Hero.Id, typeof(RectTransform));
            portrait.transform.SetParent(stage, false);
            hero = portrait.AddComponent<CharacterController>();
            hero.Bind(presentation, presentation.Hero, controller, Picture(HeroPicture()));
            LayoutActors();
        }

        string HeroPicture()
        {
            if (presentation.Hero != null && !string.IsNullOrEmpty(presentation.Hero.SpritePath))
                return presentation.Hero.SpritePath;
            ClassChoice choice = presentation.FindClass(presentation.DefaultClass);
            return choice == null ? null : choice.SpritePath;
        }

        void ToggleShop()
        {
            if (shopPanel == null || generatorScroll == null)
                return;
            bool open = !shopPanel.activeSelf;
            shopPanel.SetActive(open);
            generatorScroll.SetActive(!open);
            if (!open)
                PinGenerators(true);
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
                row.Name.text = row.Generator.Id;
                row.Name.color = presentation.Text;
                row.Owned.text = owned.ToString(CultureInfo.InvariantCulture);
                row.Owned.color = presentation.Text;
                int next = NextMilestone(owned);
                float ratio = next <= 0 ? 0f : Mathf.Clamp01(owned / (float)next);
                row.BarFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
                row.BarFill.color = presentation.Currency;
                double cost = state.NextGeneratorCost(row.Generator);
                row.Cost.text = FormatAmount(cost);
                row.Cost.color = presentation.Currency;
                row.Button.interactable = state.Currency >= cost;
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
            if (stageBackground != null && assassin != null)
                stageBackground.color = assassin.TintForOwned(state.TotalOwned);
            RefreshHighValueTarget();
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
            screenCanvas = canvasObject.GetComponent<RectTransform>();
            BuildBackdrop(screenCanvas);
            BuildVignette(screenCanvas);

            GameObject rootObject = new GameObject("Shake", typeof(RectTransform));
            rootObject.transform.SetParent(canvasObject.transform, false);
            RectTransform shakeRoot = rootObject.GetComponent<RectTransform>();
            Stretch(shakeRoot);

            RectTransform top = MakeBand(shakeRoot, "Top", presentation.TopAnchor, 1f);
            middleBand = MakeBand(shakeRoot, "Middle", presentation.MiddleAnchor, presentation.TopAnchor);
            RectTransform bottom = MakeBand(shakeRoot, "Bottom", presentation.BottomAnchor, presentation.MiddleAnchor);
            Plate(top);
            BuildTop(top);
            BuildMiddle(middleBand);
            BuildBottom(bottom);
            BuildHighValueTarget(shakeRoot);
            BuildFlashes(shakeRoot);
            return shakeRoot;
        }

        static RectTransform MakeBand(Transform parent, string name, float yMin, float yMax)
        {
            GameObject item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent, false);
            RectTransform rect = item.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, yMin);
            rect.anchorMax = new Vector2(1f, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        void Plate(RectTransform band)
        {
            Image plate = band.gameObject.AddComponent<Image>();
            plate.color = presentation.Background;
            plate.raycastTarget = false;
        }

        void BuildTop(RectTransform top)
        {
            HorizontalLayoutGroup row = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(presentation.Padding, presentation.Padding, presentation.Padding, presentation.Padding);
            row.spacing = presentation.Spacing;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            currencyText = MakeIconRow(
                top,
                presentation.CurrencyIcon,
                presentation.CurrencySize,
                presentation.CurrencyMinHeight,
                presentation.Currency,
                TextAnchor.MiddleLeft,
                FontStyle.Bold);
            timeText = MakeText(top, presentation.BodySize, presentation.BodyMinHeight, presentation.Text);
            timeText.alignment = TextAnchor.MiddleCenter;
            LayoutElement timeElement = timeText.GetComponent<LayoutElement>();
            timeElement.flexibleWidth = 1f;
            insightText = MakeIconRow(
                top,
                presentation.InsightIcon,
                presentation.BodySize,
                presentation.BodyMinHeight,
                presentation.Text,
                TextAnchor.MiddleRight,
                FontStyle.Normal);
        }

        void BuildMiddle(RectTransform middle)
        {
            VerticalLayoutGroup column = middle.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(presentation.Padding, presentation.Padding, presentation.Padding, presentation.Padding);
            column.spacing = presentation.Spacing;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            GameObject stage = new GameObject("Stage", typeof(RectTransform), typeof(LayoutElement));
            stage.transform.SetParent(middle, false);
            LayoutElement stageElement = stage.GetComponent<LayoutElement>();
            stageElement.flexibleHeight = 1f;
            stageElement.minHeight = presentation.Hero == null ? presentation.BodyMinHeight : presentation.Hero.Height;
            actorStage = stage.GetComponent<RectTransform>();

            ScrollList generators = BuildScroll(middle, "Generators", false);
            generatorScroll = generators.Root;
            generatorScrollRect = generators.Root.GetComponent<ScrollRect>();
            int iconIndex = 0;
            foreach (GeneratorConfig generator in config.Generators)
            {
                AddGeneratorRow(generators.Content, generator, iconIndex);
                iconIndex++;
            }

            ScrollList shopList = BuildScroll(middle, "ShopPanel", true);
            shopPanel = shopList.Root;
            foreach (RelicItemConfig relic in config.Relics.Items)
            {
                RelicItemConfig captured = relic;
                relicButtons[relic.Id] = MakeButton(shopList.Content, presentation.ButtonMinHeight, () => BuyRelic(captured));
            }
            MakeText(shopList.Content, presentation.BodySize, presentation.BodyMinHeight, presentation.Text).text =
                presentation.SeasonLabel + "  " + seasonPass.SeasonId + "  " + seasonPass.StartDate + "  " + seasonPass.EndDate;
            foreach (CosmeticItem item in cosmetics.Items)
            {
                string premium = item.Premium ? "  " + presentation.PremiumLabel : "";
                MakeText(shopList.Content, presentation.BodySize, presentation.BodyMinHeight, presentation.Text).text =
                    item.Name + "  " + item.Slot + premium;
            }
            shopPanel.SetActive(false);
            float referenceBand = presentation.ReferenceHeight * (presentation.TopAnchor - presentation.MiddleAnchor);
            ApplyGeneratorViewport(referenceBand);
        }

        void BuildBottom(RectTransform bottom)
        {
            HorizontalLayoutGroup row = bottom.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(presentation.Padding, presentation.Padding, presentation.Padding, presentation.Padding);
            row.spacing = presentation.Spacing;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            RectTransform contracts = MakeSlot(bottom, TextAnchor.MiddleLeft);
            RectTransform center = MakeSlot(bottom, TextAnchor.MiddleCenter);
            RectTransform prestigeSlot = MakeSlot(bottom, TextAnchor.MiddleRight);
            float share = assassin.ButtonWidthFraction;
            float side = (1f - share) * 0.5f;
            contracts.GetComponent<LayoutElement>().flexibleWidth = side;
            LayoutElement centerElement = center.GetComponent<LayoutElement>();
            centerElement.flexibleWidth = share;
            centerElement.minHeight = assassin.ButtonHeight;
            centerElement.preferredHeight = assassin.ButtonHeight;
            prestigeSlot.GetComponent<LayoutElement>().flexibleWidth = side;
            assassinateButton = BuildAssassinateButton(center);
            foreach (BranchConfig branch in config.Branches)
            {
                BranchConfig captured = branch;
                LabeledButton button = MakeButton(contracts, presentation.ButtonMinHeight, () => BuyBranch(captured));
                button.Button.gameObject.SetActive(false);
                contractButtons[branch.Id] = button;
            }
            if (Browser())
            {
                LabeledButton download = MakeButton(contracts, presentation.ButtonMinHeight, () => exportLogs.ExportNow());
                download.Label.text = presentation.DownloadLabel;
            }
            LabeledButton shop = MakeButton(prestigeSlot, presentation.ButtonMinHeight, ToggleShop);
            shop.Label.text = presentation.ShopLabel;
            prestigeButton = MakeButton(prestigeSlot, presentation.ButtonMinHeight, Prestige);
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
        }

        RectTransform MakeSlot(Transform parent, TextAnchor alignment)
        {
            GameObject slot = new GameObject("Slot", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            LayoutElement element = slot.GetComponent<LayoutElement>();
            element.flexibleWidth = 1f;
            element.minHeight = presentation.ButtonMinHeight;
            element.preferredHeight = presentation.ButtonMinHeight;
            HorizontalLayoutGroup group = slot.GetComponent<HorizontalLayoutGroup>();
            group.spacing = presentation.Spacing;
            group.childAlignment = alignment;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            return slot.GetComponent<RectTransform>();
        }

        ScrollList BuildScroll(Transform parent, string name, bool solidPlate)
        {
            GameObject scrollObject = new GameObject(name, typeof(RectTransform), typeof(ScrollRect), typeof(Image), typeof(RectMask2D), typeof(LayoutElement));
            scrollObject.transform.SetParent(parent, false);
            Image background = scrollObject.GetComponent<Image>();
            Color plate = presentation.Background;
            if (!solidPlate)
                plate.a = 0f;
            background.color = plate;
            background.raycastTarget = true;
            LayoutElement scrollElement = scrollObject.GetComponent<LayoutElement>();
            scrollElement.flexibleHeight = 1f;
            scrollElement.minHeight = presentation.GeneratorRowHeight;
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
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
            layout.padding = new RectOffset(presentation.Padding, presentation.Padding, presentation.Padding, presentation.Padding);
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
            return new ScrollList
            {
                Root = scrollObject,
                Content = content,
            };
        }

        void AddGeneratorRow(Transform content, GeneratorConfig generator, int iconIndex)
        {
            GeneratorConfig captured = generator;
            GameObject row = new GameObject("Generator", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(content, false);
            row.GetComponent<Image>().color = Color.white;
            LayoutElement element = row.GetComponent<LayoutElement>();
            element.minHeight = presentation.GeneratorRowHeight;
            element.preferredHeight = presentation.GeneratorRowHeight;
            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            group.padding = new RectOffset(presentation.Padding, presentation.Padding, 0, 0);
            group.spacing = presentation.Spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            Button button = row.GetComponent<Button>();
            StyleButton(button);
            button.onClick.AddListener(() => BuyGenerator(captured));

            GameObject chip = new GameObject("Chip", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            chip.transform.SetParent(row.transform, false);
            Image chipImage = chip.GetComponent<Image>();
            chipImage.color = presentation.IconColor(iconIndex);
            chipImage.raycastTarget = false;
            LayoutElement chipElement = chip.GetComponent<LayoutElement>();
            chipElement.minWidth = presentation.BarHeight;
            chipElement.preferredWidth = presentation.BarHeight;
            chipElement.minHeight = presentation.BarHeight;
            chipElement.preferredHeight = presentation.BarHeight;

            Text name = MakeText(row.transform, presentation.GeneratorSize, presentation.GeneratorRowHeight, presentation.Text);
            name.alignment = TextAnchor.MiddleLeft;
            name.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Text owned = MakeText(row.transform, presentation.BodySize, presentation.GeneratorRowHeight, presentation.Text);
            owned.alignment = TextAnchor.MiddleCenter;
            Text cost = MakeText(row.transform, presentation.BodySize, presentation.GeneratorRowHeight, presentation.Currency);
            cost.alignment = TextAnchor.MiddleRight;
            Image bar = MakeBar(row.transform);
            generatorRows.Add(new GeneratorRow
            {
                Generator = generator,
                Name = name,
                Owned = owned,
                Cost = cost,
                BarFill = bar,
                Button = button,
                Rect = row.GetComponent<RectTransform>(),
            });
        }

        byte[] Picture(string path)
        {
            byte[] data;
            if (string.IsNullOrEmpty(path) || !pictures.TryGetValue(path, out data))
                return null;
            return data;
        }

        Text MakeIconRow(Transform parent, string iconPath, int size, float height, Color color, TextAnchor alignment, FontStyle style)
        {
            GameObject row = new GameObject("IconRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.minHeight = height;
            rowElement.preferredHeight = height;
            rowElement.flexibleWidth = 1f;
            HorizontalLayoutGroup group = row.GetComponent<HorizontalLayoutGroup>();
            group.spacing = presentation.Spacing;
            group.childAlignment = alignment;
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
                iconElement.preferredHeight = height;
            }
            Text text = MakeText(row.transform, size, height, color);
            text.fontStyle = style;
            text.alignment = alignment;
            if (alignment == TextAnchor.MiddleLeft || alignment == TextAnchor.UpperLeft || alignment == TextAnchor.LowerLeft)
            {
                LayoutElement textElement = text.GetComponent<LayoutElement>();
                textElement.flexibleWidth = 1f;
            }
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
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null)
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            Canvas canvas = FindObjectOfType<Canvas>();
            Transform parent;
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                parent = canvasObject.transform;
            }
            else
            {
                parent = canvas.transform;
            }
            if (errorText == null)
            {
                int size = presentation == null ? font.fontSize : presentation.BodySize;
                if (size < 1)
                    size = 1;
                float height = presentation == null ? font.lineHeight : presentation.BodyMinHeight;
                if (height < 1f)
                    height = 1f;
                errorText = MakeText(parent, size, height, Color.white);
                Stretch(errorText.rectTransform);
                errorText.GetComponent<LayoutElement>().ignoreLayout = true;
                errorText.alignment = TextAnchor.UpperLeft;
                if (presentation == null)
                    errorText.resizeTextForBestFit = true;
            }
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
            StyleButton(button);
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

        void StyleButton(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = presentation.Button;
            colors.highlightedColor = presentation.Text;
            colors.pressedColor = presentation.Critical;
            colors.selectedColor = presentation.Button;
            colors.disabledColor = presentation.BarTrack;
            button.colors = colors;
            Outline outline = button.GetComponent<Outline>();
            if (outline == null)
                outline = button.gameObject.AddComponent<Outline>();
            Color glow = presentation.Glow;
            glow.a = presentation.GlowAlpha;
            outline.effectColor = glow;
            outline.effectDistance = new Vector2(presentation.GlowDistance, presentation.GlowDistance);
            button.onClick.AddListener(() =>
            {
                if (audioManager != null)
                    audioManager.PlayClick();
            });
        }

        void LayoutActors()
        {
            if (actorStage == null || middleBand == null)
                return;
            float band = middleBand.rect.height;
            if (band < 1f || actorStage.rect.width < 1f)
            {
                Canvas.ForceUpdateCanvases();
                band = middleBand.rect.height;
            }
            if (band < 1f || actorStage.rect.width < 1f)
                return;
            Vector2 size = actorStage.rect.size;
            if (laidOutStage.x > 1f && (size - laidOutStage).sqrMagnitude < 0.25f)
                return;
            ApplyGeneratorViewport(band);
            Canvas.ForceUpdateCanvases();
            laidOutStage = actorStage.rect.size;
            float reserved = PlaceHero();
            if (enemies != null)
                enemies.Layout(actorStage, reserved);
        }

        float PlaceHero()
        {
            float stageWidth = actorStage.rect.width;
            float stageHeight = actorStage.rect.height;
            if (stageWidth < 1f || stageHeight < 1f)
            {
                stageWidth = presentation.ReferenceWidth;
                stageHeight = presentation.ReferenceHeight * (presentation.TopAnchor - presentation.MiddleAnchor);
            }
            float share = stageWidth * (1f - presentation.ActorSplit);
            if (hero == null || presentation.Hero == null)
                return share;
            RectTransform rect = hero.transform as RectTransform;
            if (rect == null)
                return share;
            float aspect = presentation.Hero.Height < 1
                ? 1f
                : presentation.Hero.Width / (float)presentation.Hero.Height;
            float height = stageHeight * presentation.HeroScale;
            float width = height * aspect;
            float maxWidth = share - presentation.Padding;
            if (maxWidth > 1f && width > maxWidth)
            {
                width = maxWidth;
                height = width / aspect;
            }
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width), Mathf.Max(1f, height));
            rect.anchoredPosition = new Vector2(-presentation.Padding, -presentation.Padding);
            return width + presentation.Padding * 2f;
        }

        void ApplyGeneratorViewport(float bandHeight)
        {
            int count = config == null || config.Generators == null ? 0 : config.Generators.Length;
            int rows = RowsThatFit(bandHeight, count);
            float height = RowBlock(rows);
            SetScrollHeight(generatorScroll, height);
            SetScrollHeight(shopPanel, height);
        }

        int RowsThatFit(float bandHeight, int count)
        {
            int cap = 5;
            if (count < cap)
                cap = count;
            if (cap < 1)
                return 1;
            if (bandHeight < 1f)
                bandHeight = presentation.ReferenceHeight * (presentation.TopAnchor - presentation.MiddleAnchor);
            float chrome = presentation.Padding * 2f + presentation.Spacing;
            for (int rows = cap; rows > 1; rows--)
            {
                float stage = bandHeight - chrome - RowBlock(rows);
                if (stage >= RowBlock(rows))
                    return rows;
            }
            return 1;
        }

        float RowBlock(int rows)
        {
            if (rows < 1)
                rows = 1;
            return rows * presentation.GeneratorRowHeight
                + (rows - 1) * presentation.Spacing
                + presentation.Padding * 2f;
        }

        static void SetScrollHeight(GameObject scroll, float height)
        {
            if (scroll == null)
                return;
            LayoutElement element = scroll.GetComponent<LayoutElement>();
            if (element == null)
                return;
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleHeight = 0f;
        }

        void PinGenerators(bool force)
        {
            if (generatorScrollRect == null || generatorScrollRect.content == null)
                return;
            if (generatorScrollPinned && !force)
                return;
            if (!generatorScroll.activeInHierarchy)
                return;
            Canvas.ForceUpdateCanvases();
            if (generatorScrollRect.content.rect.height < 1f)
                return;
            generatorScrollRect.verticalNormalizedPosition = 1f;
            generatorScrollPinned = true;
        }

        Image MakeBar(Transform parent)
        {
            GameObject track = new GameObject("Bar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            track.transform.SetParent(parent, false);
            Image trackImage = track.GetComponent<Image>();
            trackImage.color = presentation.BarTrack;
            trackImage.raycastTarget = false;
            LayoutElement element = track.GetComponent<LayoutElement>();
            element.minHeight = presentation.BarHeight;
            element.preferredHeight = presentation.BarHeight;
            element.flexibleWidth = 1f;
            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image), typeof(Outline));
            fill.transform.SetParent(track.transform, false);
            RectTransform rect = fill.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(0f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = fill.GetComponent<Image>();
            image.color = presentation.Currency;
            image.raycastTarget = false;
            Outline glow = fill.GetComponent<Outline>();
            Color glowColor = presentation.Bar;
            glowColor.a = presentation.GlowAlpha;
            glow.effectColor = glowColor;
            glow.effectDistance = new Vector2(presentation.GlowDistance, presentation.GlowDistance);
            return image;
        }

        void AdvanceAssassin(double dt)
        {
            if (assassin == null)
                return;
            playClock += dt;
            if (hvtPhase == HvtPhase.Waiting && playClock >= hvtSpawnAt)
                SpawnHighValueTarget();
            else if (hvtPhase == HvtPhase.Active && playClock >= hvtWindowEnd)
                FailHighValueTarget();
            UpdateAssassinateCooldown();
            UpdateBackgroundMotion();
            FadeFlash(backstabFlash, backstabFlashUntil, assassin.BackstabFlashSeconds);
            FadeFlash(prestigeFlash, prestigeFlashUntil, assassin.PrestigeFlashSeconds);
        }

        void Assassinate()
        {
            if (assassin == null || state == null || playClock < assassinateReadyAt)
                return;
            bool backstab = UnityEngine.Random.value < assassin.BackstabChance;
            double amount = assassin.Burst(state.TotalOwned, backstab);
            state.GrantCurrency(amount);
            if (analytics != null)
                analytics.AssassinateTriggered(amount, backstab);
            assassinateReadyAt = playClock + assassin.CooldownSeconds;
            if (assassinateButton != null)
                assassinateButton.Button.interactable = assassin.CooldownSeconds <= 0f;
            if (assassinateFill != null && assassin.CooldownSeconds > 0f)
                assassinateFill.fillAmount = 1f;
            if (backstab)
            {
                if (analytics != null)
                    analytics.BackstabTriggered(amount);
                StartFlash(backstabFlash, presentation.Critical, assassin.BackstabFlashSeconds);
                if (feedback != null)
                {
                    feedback.Shake();
                    string label = assassin.BackstabLabel.Replace("{amount}", FormatAmount(amount));
                    RectTransform anchor = assassinateButton == null ? null : assassinateButton.Rect;
                    feedback.Float(label, presentation.Critical, anchor);
                }
            }
            if (hvtPhase == HvtPhase.Active)
            {
                hvtTaps += 1;
                if (hvtTaps >= assassin.HvtTapsRequired)
                    CompleteHighValueTarget();
            }
            Refresh();
            Save();
        }

        void SpawnHighValueTarget()
        {
            hvtPhase = HvtPhase.Active;
            hvtTaps = 0;
            hvtWindowEnd = playClock + assassin.HvtWindowSeconds;
            if (analytics != null)
                analytics.HvtSpawned();
        }

        void FailHighValueTarget()
        {
            hvtPhase = HvtPhase.Waiting;
            hvtTaps = 0;
            hvtSpawnAt = playClock + assassin.HvtFailCooldownSeconds;
            if (analytics != null)
                analytics.HvtFailed();
        }

        void CompleteHighValueTarget()
        {
            hvtPhase = HvtPhase.Waiting;
            hvtTaps = 0;
            hvtSpawnAt = playClock + assassin.HvtIntervalSeconds;
            state.GrantCurrency(assassin.HvtReward);
            if (analytics != null)
                analytics.HvtCompleted(assassin.HvtReward);
            if (feedback != null)
                feedback.Confetti();
        }

        void UpdateAssassinateCooldown()
        {
            if (assassinateButton == null || assassin == null)
                return;
            double remaining = assassinateReadyAt - playClock;
            bool cooling = remaining > 0.0;
            assassinateButton.Button.interactable = !cooling;
            if (assassinateFill == null)
                return;
            float cooldown = assassin.CooldownSeconds;
            assassinateFill.fillAmount = !cooling || cooldown <= 0f
                ? 0f
                : Mathf.Clamp01((float)(remaining / cooldown));
        }

        void UpdateBackgroundMotion()
        {
            if (stageBackgroundRect == null || screenCanvas == null || assassin == null)
                return;
            float width = screenCanvas.rect.width;
            if (width < 1f)
                width = presentation.ReferenceWidth;
            float period = assassin.MotionPeriodSeconds;
            float half = period * 0.5f;
            float along = half <= 0f ? 0f : Mathf.PingPong((float)playClock, half) / half;
            float centered = along * 2f - 1f;
            float extra = assassin.MotionAmplitude * width;
            float shift = centered * extra;
            stageBackgroundRect.offsetMin = new Vector2(-extra + shift, 0f);
            stageBackgroundRect.offsetMax = new Vector2(extra + shift, 0f);
        }

        void RefreshHighValueTarget()
        {
            if (hvtWidget == null)
                return;
            bool active = hvtPhase == HvtPhase.Active;
            if (hvtWidget.activeSelf != active)
                hvtWidget.SetActive(active);
            if (!active || hvtTime == null || assassin == null)
                return;
            hvtTitle.text = assassin.HvtTitle;
            hvtTitle.color = presentation.Critical;
            double remaining = hvtWindowEnd - playClock;
            if (remaining < 0.0)
                remaining = 0.0;
            int secondsLeft = (int)Math.Ceiling(remaining);
            hvtTime.text = secondsLeft.ToString(CultureInfo.InvariantCulture);
            hvtTime.color = presentation.Text;
        }

        void BuildHighValueTarget(Transform parent)
        {
            GameObject widget = new GameObject("HighValueTarget", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            widget.transform.SetParent(parent, false);
            RectTransform rect = widget.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, presentation.TopAnchor);
            rect.anchorMax = new Vector2(1f, presentation.TopAnchor);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-presentation.Padding, -presentation.Padding);
            Image plate = widget.GetComponent<Image>();
            Color plateColor = presentation.Button;
            plateColor.a = presentation.GlowAlpha;
            plate.color = plateColor;
            plate.raycastTarget = false;
            VerticalLayoutGroup column = widget.GetComponent<VerticalLayoutGroup>();
            int pad = presentation.Padding;
            column.padding = new RectOffset(pad, pad, pad, pad);
            column.spacing = presentation.Spacing;
            column.childAlignment = TextAnchor.UpperRight;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = false;
            column.childForceExpandHeight = false;
            ContentSizeFitter fitter = widget.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            hvtTitle = MakeText(widget.transform, presentation.GeneratorSize, presentation.BodyMinHeight, presentation.Critical);
            hvtTitle.alignment = TextAnchor.MiddleRight;
            hvtTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
            hvtTitle.text = assassin.HvtTitle;
            hvtTime = MakeText(widget.transform, presentation.BodySize, presentation.BodyMinHeight, presentation.Text);
            hvtTime.alignment = TextAnchor.MiddleRight;
            hvtTime.horizontalOverflow = HorizontalWrapMode.Overflow;
            hvtWidget = widget;
            hvtWidget.SetActive(false);
        }

        void BuildFlashes(Transform parent)
        {
            backstabFlash = MakeFlash(parent, "BackstabFlash");
            prestigeFlash = MakeFlash(parent, "PrestigeFlash");
        }

        Image MakeFlash(Transform parent, string name)
        {
            GameObject item = new GameObject(name, typeof(RectTransform), typeof(Image));
            item.transform.SetParent(parent, false);
            Stretch(item.GetComponent<RectTransform>());
            Image image = item.GetComponent<Image>();
            image.sprite = SolidSprite();
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        void StartFlash(Image image, Color color, float seconds)
        {
            if (image == null)
                return;
            color.a = seconds <= 0f ? 0f : 1f;
            image.color = color;
            float until = Time.unscaledTime + seconds;
            if (image == prestigeFlash)
                prestigeFlashUntil = until;
            else
                backstabFlashUntil = until;
        }

        static void FadeFlash(Image image, float until, float seconds)
        {
            if (image == null)
                return;
            Color color = image.color;
            float remain = until - Time.unscaledTime;
            color.a = seconds <= 0f || remain <= 0f ? 0f : Mathf.Clamp01(remain / seconds);
            image.color = color;
        }

        LabeledButton BuildAssassinateButton(Transform parent)
        {
            float height = assassin.ButtonHeight;
            GameObject item = new GameObject("Assassinate", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            item.transform.SetParent(parent, false);
            Image face = item.GetComponent<Image>();
            face.color = Color.white;
            face.sprite = SolidSprite();
            LayoutElement element = item.GetComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            element.flexibleWidth = 1f;
            Button button = item.GetComponent<Button>();
            StyleButton(button);
            button.onClick.AddListener(Assassinate);
            GameObject fillObject = new GameObject("Cooldown", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            fillObject.transform.SetParent(item.transform, false);
            fillObject.GetComponent<LayoutElement>().ignoreLayout = true;
            Stretch(fillObject.GetComponent<RectTransform>());
            assassinateFill = fillObject.GetComponent<Image>();
            assassinateFill.sprite = SolidSprite();
            assassinateFill.type = Image.Type.Filled;
            assassinateFill.fillMethod = Image.FillMethod.Radial360;
            assassinateFill.fillOrigin = (int)Image.Origin360.Top;
            assassinateFill.fillClockwise = true;
            assassinateFill.fillAmount = 0f;
            assassinateFill.raycastTarget = false;
            Color fill = presentation.Critical;
            fill.a = presentation.GlowAlpha;
            assassinateFill.color = fill;
            float labelHeight = height - presentation.Spacing;
            if (labelHeight < presentation.BodyMinHeight)
                labelHeight = presentation.BodyMinHeight;
            Text label = MakeText(item.transform, assassin.LabelSize, labelHeight, presentation.Critical);
            Stretch(label.rectTransform);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.text = assassin.Display;
            label.GetComponent<LayoutElement>().ignoreLayout = true;
            return new LabeledButton
            {
                Button = button,
                Label = label,
                Rect = item.GetComponent<RectTransform>(),
            };
        }

        Sprite SolidSprite()
        {
            if (solidSprite != null)
                return solidSprite;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new Color[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            solidSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                presentation.PixelsPerUnit);
            return solidSprite;
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
