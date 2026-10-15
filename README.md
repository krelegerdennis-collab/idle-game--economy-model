# Idle economy model

Mathematical economy model for a mobile idle game. This is not a game.
It simulates cost curves, production, prestige, offline earning, and
monetization gates, then checks the result against retention targets.

Balance numbers live in [`data/`](data/). Formulas live in [`model/`](model/).
[`AGENTS.md`](AGENTS.md) is the brief for an agent working in this repo.
[`.cursor/rules/economy.mdc`](.cursor/rules/economy.mdc) is loaded every session.

## Formulas

```
cost_next        = cost_base × growth_rate^owned
production_total = production_base × owned × multipliers
prestige_reset   = f(lifetime_currency, prestige_multiplier)
offline_earnings = production × min(time_away, cap) × offline_rate   # offline_rate ≤ 0.5
```

The prestige multiplier is `(1 + point_value) ^ total_points`. A reset clears
the run and keeps the points, so the multiplier compounds. Production stays
linear in copies owned (milestone steps included). Cost is exponential, which
is what keeps the curve from printing infinite currency.

The first prestige wall is the first moment of active play when a reset would
grant at least `min_points_to_prestige` and the best next purchase takes at
least `stall_seconds`. Offline time does not count toward that clock.

## Targets

| Signal | Target |
| --- | --- |
| D1 projection | ≥ 35% |
| D7 projection | ≥ 15% |
| D30 projection | ≥ 5% |
| Session cadence | 5–6 check-ins per day |
| First prestige wall | ~90 minutes of active play (pass band 90–120) |

D1/D7/D30 printed by the simulator are a design-time projection from the
run. They are not measured cohort retention. Session cadence is
`waking_hours / offline cap`: earnings stop at the cap, so that is when a
player comes back. Monetization gates sit on the first prestige wall and
every 3rd prestige. The offer is optional. The free path is the one the
simulator scores.

## Run

```bash
python -m pip install -r requirements.txt
python -m sim.run_simulation
python -m sim.compare_runs
python -m sim.export_unity
python -m pytest
```

`compare_runs` diffs the current model against `exports/runs/baseline.json`.
Pass a data directory or a git ref instead:

```bash
python -m sim.compare_runs --other experiment/higher-cost-exponent
```

If a run breaks a constraint, the simulator prints `STOP` and exits non-zero.
Do not tune the monetization offer to drag a broken curve back into range.

## Layout

```
AGENTS.md                         agent brief
.cursor/rules/economy.mdc         rules loaded every session
model/                            formulas
data/                             tunable values
sim/run_simulation.py             active-play simulation
sim/compare_runs.py               before/after diff
sim/export_unity.py               ScriptableObject JSON and spreadsheet CSV
exports/unity/                    generated Unity payload
exports/spreadsheet/              generated generator table
exports/runs/baseline.json        last passing simulation report
tests/test_economy.py             formulas and constraint checks
```
