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
writes the clip onto `Assets/Resources/AudioLibrary.asset`. Until that clip
exists, `AudioManager` plays a short generated tone from the frequencies in
`presentation.json`.

Actors use `Assets/Resources/Animators/PixelActor.controller` with states
Idle, Attack, Celebrate, and Hurt. Enemies stay placeholder rectangles.
The hero reads `actors.hero.sprite_path` and loads that PNG from
StreamingAssets. The same bytes live in `Assets/Sprites/Characters/`.
A loaded sprite is drawn at its own colors. The actor `color` is only the
rectangle fallback.

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
