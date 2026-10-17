# AGENTS.md — Idle Game Economy Simulation

## What this project is
A mathematical economy model for a mobile idle game. It is NOT a game.
It is a spreadsheet-driven simulator that models cost curves, production
rates, prestige multipliers, offline earning, and monetization gates.
The game engine comes later. The math comes first.

## What the agent should do first
1. Read the economy model files in /model/ before writing any code.
2. Treat the formulas as the source of truth:
   - cost_next = cost_base × growth_rate^owned
   - production_total = production_base × owned × multipliers
   - prestige_reset = f(lifetime_currency, prestige_multiplier)
3. Validate any proposed change against the retention targets below.

## Hard constraints (do not violate)
- D1 retention target: ≥ 35%
- D7 retention target: ≥ 15%
- D30 retention target: ≥ 5%
- Session cadence target: 5–6 check-ins per day
- First prestige wall: ~90 minutes of active play

## Tools and stack
- Economy model: Python (pandas + numpy) for simulation,
  or Google Sheets for interactive tuning.
- Output format: JSON/CSV exported for Unity ScriptableObject consumption.
- Unity integration: ScriptableObjects, data-driven configuration.
- Analytics events: session_begin, upgrade_purchase, prestige_trigger.

## Rules for the agent
- Do not retune economy numbers. The generator curve, prestige, and
  `exports/runs/baseline.json` stay frozen.
- Do not hardcode values in code. Economy numbers live in the economy
  files under /data/. Playtest colors, type, audio ids, and cosmetics live
  in `data/presentation.json`, `data/cosmetics.json`, and `data/season_pass.json`.
- Every formula change must include a before/after simulation run.
- If a change would break a retention constraint, stop and explain why.

## Frozen baseline

`exports/runs/baseline.json` is frozen. It records the single-purchase
curve: dip 0.7, peak at 68 copies, commit threshold
`50 + floor(total_owned / 10)`. Do not regenerate that file, and do not
retune the contract, generator, or prestige numbers behind it, unless a
playtest requires it.

The first-run contract on that curve is unchanged by the relic axis:
take rate 57% (12 of 21), purchase window 3.56 minutes (minute 20.31–23.87),
first wall 97.19 minutes, one prestige point, D1/D7/D30 40.5% / 19.3% / 8.0%.

## Second reward axis

`data/relics.json` defines insight, a currency that is not the generator
currency and does not buy the peak contract. Sources pay it once per run
(owned-copy thresholds, and the moment a run is worth a prestige point).
Relics bought with insight persist across prestige and never add prestige
points. The prestige exchange is the competing sink: its points apply on
the next reset only, so the current run's multiplier stays on the lifetime
formula.

Horizon ROI buys neither relics nor the exchange on the shipped curve.
Offline earning is already at the 0.5 cap. Session-start effects do not
replay in the middle of a run. The extra milestone sits at 75 copies of
one generator, and the wall is reached at 58 of the highest generator, so
that slot never enters the 10 minute horizon. Exchange points land after
the reset, outside the same horizon. The run earns 5 insight and spends
none. The contract window does not get wider, because insight is not an
alternate place to spend generator currency.

The baseline did not break. That is the result of the second axis, not a
reason to go back and retune the contract.

## Verification loop
Verify every economy change against the simulation, not against a hunch.

1. Run `python -m sim.run_simulation` before and after the change.
   Compare prestige-wall time, the D1/D7/D30 projection, and session cadence.
   `python -m sim.compare_runs` diffs the current model against
   `exports/runs/baseline.json` or against another data directory or git ref.
2. Check route shares from the ROI policy alone. ROI scores the production
   change over `roi_horizon_minutes` of active play, or until the next
   prestige, whichever comes first. The horizon lives in `data/simulation.json`.
   The peak contract is the same objective: it can win when its peak lands
   inside that window, and it loses while the dip is all the window can see.
   There is no second purchase policy. A route that holds `dominant_route_max`
   or more of ROI runs is still a failed split. Generator skips are still
   illegible. Both targets live in `data/retention_targets.json`.
3. Regenerate `exports/unity/economy_config.json` and confirm it matches
   `/data/`. If the JSON disagrees with the model, the model is wrong.

The first prestige wall is the first moment in active play when both of
these are true: a reset would grant at least `min_points_to_prestige`,
and the best next purchase takes at least `stall_seconds`. Offline time
does not count toward the 90 minute target.

Retention percentages printed by the simulator are a design-time projection
from those signals. They are not measured cohort retention. If a proposed
change fails a constraint, stop. Do not tune monetization to force the
numbers back into range.

## Playtest

The simulation phase is finished. Do not retune `data/` and do not
regenerate `exports/runs/baseline.json` to chase a cohort.

`unity/Playtest/` is a Unity 2022 project. Open that folder and load
`Assets/Playtest.unity`. The scene has a camera and one behaviour. Counters
and buttons are built at runtime from
`Assets/StreamingAssets/economy_config.json`. That file must stay
byte-identical to `exports/unity/economy_config.json`. The same folder
also carries `presentation.json`, `cosmetics.json`, and `season_pass.json`,
each byte-identical to the file of the same name in `data/`. Placeholder
rectangles stand in for sprites. There is no finished art, narrative, or
menu, and no analytics SDK. Cosmetics and the season pass do not change
production, and the client does not sell them.

The client reads the single-purchase curve in the export. A branch with a
`structure` field stops the client. The prestige button appears when a
reset would grant `minPointsToPrestige`. `stallSeconds` stays in the
export and is not a second gate on the button. The contract button appears
while `offer_is_open` is true. Leaving the session, or letting that offer
close, writes `contract_declined`. There is no decline button.

`noisy_seed` is not in the export, so the session-start roll is keyed by
the install id. It will not reproduce the simulation seed. The export also
has no offline-rate cap separate from `offlineRate`; relic adds do not
push the client above that published rate. The prestige exchange is in the
export and is not a relic, so this screen does not sell it.

On Android, iOS, and the editor, logs append to `playtest_log.jsonl`.
Android copies that file on pause and quit to
`/sdcard/Android/data/<package>/files/playtest_log.jsonl`. iOS keeps it in
`Application.persistentDataPath`.

WebGL does not use that path. The log stays in memory, with a PlayerPrefs
copy, and a **Log downloaden** button. Quit also asks the browser to
download `playtest_log.jsonl`. Browsers often block a download that did not
start from a click, so the button is the path that works. Unity 2022 has no
`Application.ExternalEval`; the download goes through
`Assets/Plugins/WebGL/PlaytestDownload.jslib`. WebGL `session_begin` events
carry `medium: webgl`.

`python -m sim.analyze_playtest <file-or-dir>` reports purchases, refusals,
prestige, relics, unlock order, and D1/D7/D30. A day rate is null when the
logs do not cover that many days. `python -m sim.compare_playtest_to_sim
--logs <file-or-dir>` prints the numeric difference against the frozen
baseline. With no logs, the playtest side stays blank. Logs marked
`medium: webgl` skip that retention comparison; use the smoke-test output
instead.

The APK icon is `Assets/Icons/AppIcon.png`. In the Editor: Edit > Project Settings > Player > Android icon (Override), or menu Tools/Apply App Icon. Then File > Build Settings > Android > Build. The icon updates on the next APK install; Android caches launcher icons, so uninstall the old app if the phone still shows the Unity logo.

## WebGL-rooktest

The WebGL build is a smoke test, not the retention playtest. It answers
whether the scene renders, the buttons click, the contract appears while
the offer is open, and AnalyticsBridge writes a JSONL file the analyzer can
read. It is not a home-screen icon, it does not send notifications, and a
browser tab is not a seven-day return habit.

D1, D7, and D30 computed from WebGL logs are visit counts. They are not
cohort retention and must not be placed next to `exports/runs/baseline.json`.
The valid WebGL measurements are the take rate, the contract window, and
the `contract_declined` ratio (players who declined and did not buy, divided
by players who were shown the offer). The simulator has no declined ratio.

`.github/workflows/webgl-build.yml` builds `unity/Playtest` with
`unityci/editor:ubuntu-2022.3.62f1-webgl-3` via `game-ci/unity-builder`,
then pushes the player to `gh-pages` with `peaceiris/actions-gh-pages`.
It runs on pushes to `main` that touch `unity/Playtest/**`, and on this
playtest branch so the smoke test can run before merge. The Pages URL is
`https://krelegerdennis-collab.github.io/idle-game--economy-model/` once
Pages serves the `gh-pages` branch. The build turns compression off because
GitHub Pages does not send Brotli or Gzip content-encoding.

The workflow cannot activate Unity by itself. Create these repository
secrets once, then re-run the workflow:

1. Sign in to a Unity account that can use a Personal license.
2. Activate the editor once and collect the `.ulf` file. From a machine
   that already has Unity, that file is the license Unity Hub wrote
   (`Unity_lic.ulf`). From GitHub, run `game-ci/unity-request-activation-file`
   with `UNITY_EMAIL` and `UNITY_PASSWORD`, download the `.alf` artifact,
   upload it at <https://license.unity3d.com/manual>, and save the returned
   `.ulf`.
3. Store the secrets: `UNITY_LICENSE` is the full text of the `.ulf`,
   `UNITY_EMAIL` is the Unity account, `UNITY_PASSWORD` is that account's
   password.
4. Enable GitHub Pages on branch `gh-pages`, folder `/`. The workflow
   tries this after the publish step. If the token cannot change repo
   settings, do it once in the repository settings.

Do not retune `data/` because a WebGL build failed.

## Art and audio pipeline

The playtest theme is data, not a retune of the economy. `data/presentation.json`
holds colors, type sizes, feedback, audio ids, and the placeholder actors.
`data/cosmetics.json` and `data/season_pass.json` are the cosmetic schema.
`sim/run_simulation.py` skips those three files, and `relics.json`, when it
hashes the frozen curve. Do not regenerate `exports/runs/baseline.json`
because a sprite or a track was added. Do not grant currency, insight, or
prestige points from a cosmetic, a season reward, or `is_premium`.

`price_tier` is a label. This client has no store and no in-app purchase.
Premium rows are text only.

Drop finished files in these folders. The code reads ids from the JSON, so
a new file does not need a code change:

- Sprites: `unity/Playtest/Assets/Sprites/Characters/`,
  `unity/Playtest/Assets/Sprites/Enemies/`,
  `unity/Playtest/Assets/Sprites/UI/`. PNG with transparency.
  `sprite_path` in `data/cosmetics.json` is the path under `Assets/Sprites/`.
- Music: `unity/Playtest/Assets/Audio/Music/`. MP3.
- Effects: `unity/Playtest/Assets/Audio/SFX/`. WAV.

The audio id in `presentation.json` is the path without `Assets/` and
without the extension, for example `Audio/Music/idle`. An editor import
writes the clip onto `Assets/Resources/AudioLibrary.asset`. Music files
are MP3 in `unity/Playtest/Assets/Audio/Music/`. Effect files are WAV in
`unity/Playtest/Assets/Audio/SFX/`. `AudioManager` keeps `musicClips` and
`sfxClips` empty until those files exist, and it plays only from the
array that has a clip. The game stays silent until clips are assigned.

Enemies use `Assets/Resources/Animators/PixelActor.controller` with states
Idle, Attack, Celebrate, and Hurt. Each actor is an Image on the UI
canvas, in the middle band described under UI Layout. The hero is one
large Image on the top-right of the fight scene and plays the assassin
sheet from Assassin Animation. Enemies sit
in the left and center of that stage, large enough to read. The backdrop
is the supplied cyberpunk city,
`unity/Playtest/Assets/Sprites/Backgrounds/cyberpunk_city.png`,
drawn as a full-screen Image with preserve aspect off, behind the bands.
The same PNG bytes are copied under StreamingAssets. The
five classes in `art.classes` stay in the data. The client does not draw
them and does not swap the picture. `art.defaultClass` stays `rogue`.
The hero picture is the assassin run cycle, not that class row.
`art.classPrefKey` is
unused and does not change production. Enemy, UI, and background PNGs
are cut from the same sheet into `Assets/Sprites/` and copied to
StreamingAssets. Those textures import with point filtering and
compression set to none. A loaded sprite is drawn at its own colors. The
actor `color` is only the rectangle fallback.

TextMeshPro draws the floating numbers. Unity 2022 still needs its own TMP
package (`com.unity.textmeshpro` in `Packages/manifest.json`). If the font
asset cannot be created, those numbers fall back to the built-in UI font.
The economy counters stay on that built-in font either way.

There is no combat crit in the frozen economy. The critical color marks a
purchase that costs at least `largePurchaseCostShare` of the wallet, or a
generator purchase that lands on an owned-copy milestone from the export.
Confetti plays on that milestone and on prestige. The boss track starts
once lifetime currency reaches `bossApproachRatio` of the lifetime the
exported prestige formula needs for `minPointsToPrestige`.

## Assassin Theme

Assassinate and the timed high-value target are playtest-only. The frozen
simulation does not grant either payout, so playtest currency and lifetime
diverge from the curve once a player uses them. `data/assassin.json` holds
every new number and string. A byte-identical copy sits in StreamingAssets.
`data_fingerprint` skips `assassin.json` next to `presentation.json`, and
`model/load.py` does not list it in `DATA_FILES`. Do not add the payout to
the Python sim, and do not regenerate `exports/runs/baseline.json`.

The economy contract is unchanged. Dip and peak purchase stay on the
exported branch, and the sim still declines the offer by default. The
contract button keeps the display string `Contract`. It is not the timed
target. That target is a separate widget.

The Assassinate button is the large control at the bottom center. Its
label and the backstab flash use the critical color already loaded from
the presentation theme. The burst is `baseAmount * (1 + total owned
generators * perGenerator)`. A backstab, rolled with `UnityEngine.Random`
against `backstabChance`, multiplies that burst by `backstabMultiplier`,
shakes the screen, and floats `backstabLabel`. The button shows a radial
fill for `cooldownSeconds` and does not accept taps while it is cooling
down. The grant is added to currency and lifetime the same way offline
pay adds them.

The high-value target starts its timer when play begins. After
`firstDelaySeconds` the widget appears at the top right of the fight
scene with the title and the seconds left in `windowSeconds`. Only
Assassinate taps during that window count. Five taps, from `tapsRequired`,
grant `reward` to currency and lifetime, play the existing gold confetti,
and log `hvt_completed`. The next spawn waits `intervalSeconds`. If the
window times out, the widget hides, the log records `hvt_failed`, and the
next spawn waits `failCooldownSeconds`. Appearance logs `hvt_spawned`.
An Assassinate tap logs `assassinate_triggered` with the player id, an
ISO-8601 UTC timestamp, the amount, and whether it was a backstab.
`backstab_triggered` is logged only when the roll hits.

The city image drifts left and right. `backgroundMotion.amplitude` is a
fraction of the canvas width and `periodSeconds` is one full ping-pong.
The picture itself is not a new drawing. A vignette is a runtime texture,
not a painted file: `presentation.Background` darkens the edges at
`vignetteAlpha`, and the center stays clear. Sky tint lerps
`background Image.color` through the `skyTints` stops by total generators
owned (0, 50, 100). The blend is smooth. Prestige does not retune the
economy. A fullscreen white flash lasts `prestigeFlashSeconds`.

Unity Editor was not run for these hooks, so Play mode was not checked.

## Assassin Animation

The hero is a UI Image on the fight-scene canvas. Clips animate
`Image.sprite` (`m_Sprite` on `UnityEngine.UI.Image`). They do not drive a
world sprite renderer, and the hero does not patrol across the screen.

`unity/Playtest/Assets/Sprites/Characters/assassin_sheet.png` is the
supplied sheet with the checkerboard keyed out. The same bytes are copied
to StreamingAssets. The sheet is not a 3x3. Tight crops, also copied
byte-for-byte under `StreamingAssets/Sprites/Characters/`:

- Run, four frames, 12 fps, looping: `assassin_run_0.png` through
  `assassin_run_3.png`. The character runs to the right and the cape moves.
- Attack, three frames, 15 fps, not looping: `assassin_attack_0.png`
  through `assassin_attack_2.png`. Wind-up, the cyan slash, then recovery.
- Idle, two frames, 6 fps, looping: `assassin_idle_0.png` and
  `assassin_idle_1.png`. Standing, with a small breathing change.

Counts and rates live in `data/assassin.json` under `animation`, with a
byte-identical StreamingAssets copy. `AssassinRunner` reads that fps for
the timer that swaps the hero Image when the imported clips are not bound.
The editor clips still use 12, 15, and 6 as their sample rates.

`Tools/Slice Assassin Sheet` is the menu on
`unity/Playtest/Assets/Editor/SpriteSheetSlicer.cs`. It sets Sprite (2D and
UI), Point filtering, and no compression, and writes rects for this 4 / 3 /
2 sheet. An enemy sheet can later use the same slicer with its own rects.

`unity/Playtest/Assets/Animations/AssassinAnimator.controller` has states
Run, Attack, and Idle. Run is the default. `IsRunning` keeps Run while it
is true and Idle while it is false. The playtest starts in Run. The Attack
trigger plays the strike. Attack has exit time, does not loop, and returns
to Run when the clip ends. The Assassinate button calls
`AssassinRunner.Attack`, which sets that trigger. Burst, cooldown,
backstab, and the high-value target are unchanged.

Unity Editor was not run, so press Play in the Editor to see the cycle.

## UI Layout

The playtest is one portrait screen. `PlaytestController` builds a single
Canvas with Render Mode Screen Space - Overlay. The CanvasScaler uses
Scale With Screen Size. Reference resolution and match come from
`data/presentation.json`: `layout.referenceWidth` 1080,
`layout.referenceHeight` 1920, and `layout.matchWidthOrHeight` 0.5.
C# reads those fields. It does not hardcode them.

Three bands divide the screen. Their edges are anchors from the same
`layout` object, so 1080×1920 and a taller 1080×2400 phone keep the same
fractions:

- Top, from `topAnchor` (0.9) to the top of the screen. Marks on
  the left with the coin icon, large and bold (`type.currencySize`).
  The word is `labels.currency` in `data/assassin.json`, not the
  currency id. Session time in the center (`type.bodySize`). Intel on
  the right with the crystal icon (`labels.insight`).
- Middle, from `middleAnchor` (0.15) to `topAnchor` (0.9). This is the
  fight scene. There is no class picker and no row of class Images. The
  hero is the animated assassin, held in the top-right slot. One large
  Image is anchored to the top-right of the stage. Its height is
  `heroScale` (0.85) of that stage, and its width follows `actors.hero`
  width and height so the pixel art keeps its aspect. `actorSplit` (0.5)
  keeps the hero on the right. Enemies are Images in the left and center. On the
  tall portrait stage they stack, so they read as a fight and not as a
  class-select row. They do not sit under the hero or the buttons.
  Generator buys are buttons on this same band, under the stage, in a
  grid. Column count is `layout.generatorColumns` (2). Row height is
  `layout.generatorRowHeight` (72, inside 60–80). Cell inset is
  `layout.spacing`. Six generators are three rows. Each button shows the
  informant display name from `assassin.json` `labels.informants`, the
  owned count, and the cost, tinted with the theme button colors. The
  name sits on the first line and the count and cost on the second, and
  `generatorSize + bodySize + barHeight` stays within the row (28 + 24 +
  18 = 70, under 72). On the 1080×1920 reference the grid is 312
  reference pixels tall: 3×72, two gaps of `spacing` 16, and `padding`
  32 above and below the list. The middle band is 1440 tall. After the
  band's own padding and the gap above the grid (80) and a fight-scene
  reserve of `stageFraction` 0.22 (about 317), 1043 pixels remain, so
  the 312-pixel grid fits. The scroll view does not scroll. The stage
  keeps the rest, about 1048 pixels, so the city, the assassin, and the
  enemies stay on screen with all six buttons. The character Image uses
  preserve aspect. The background Image does not. The list background is
  clear, so the city shows between the buttons.
- Bottom, from `bottomAnchor` (0.0) to `middleAnchor` (0.15). A clear
  strip, not a black panel. The contract button stays on the left and is
  shown only while the offer is open. The large Assassinate button is the
  bottom-center control. Prestige stays on the right, and the shop button
  sits on that same side. Shop opens the relic list in the generator slot.
  It is not a store and it does not sell anything. Season and cosmetic lines
  sit in that list. These controls stay below the hero.

A full-screen background Image is parented to the canvas behind the
bands. It is `Sprites/Backgrounds/cyberpunk_city.png`. Preserve aspect is
off. The rect drifts for the slow parallax in Assassin Theme, and a
vignette sits above the picture and below the bands. Point filtering and
no compression come from the sprite importer. A sprite loaded with
`LoadImage` at runtime uses point filtering too. The top bar uses the theme background color so
the numbers stay readable. The middle and bottom stay clear so the stage
art shows through. Class choice does not change the picture.

`ProjectSettings` `defaultScreenOrientation` is `0`. In Unity 2022.3
`UIOrientation` that value is Portrait. PortraitUpsideDown is 1,
LandscapeRight is 2, LandscapeLeft is 3, and AutoRotation is 4. Portrait
autorotation is allowed. Landscape is not. `activeInputHandler` stays 2.

The generator grid does not scroll on the 1080×1920 reference, because
all six buttons fit under the fight scene. The scroll view stays only
as a fallback when a shorter band cannot hold that grid. The relic list
may scroll. The top bar does not. Padding and spacing inside the bands
come from `layout.padding` and `layout.spacing`.

On-screen words for Marks, Intel, Shadow Mastery, and the six informants
come from `data/assassin.json` `labels`. Assassinate keeps
`assassinate.display`. Internal ids stay in the economy files. The
contract button stays on the left with its cost, and the shop button
stays on the right. Prestige uses the Shadow Mastery label when a reset
is available.

Audio stays silent until real files are assigned: WAV effects in
`unity/Playtest/Assets/Audio/SFX/` and MP3 music in
`unity/Playtest/Assets/Audio/Music/`. `AudioManager` keeps `musicClips`
and `sfxClips` empty until those clips exist, and it does not synthesize
a tone.

Unity Editor was not run for this layout, so Game View and an Android
build were not checked. The anchors are resolution-independent by
construction.
