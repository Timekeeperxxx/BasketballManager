"""Manual per-player attribute overrides — applied AFTER apply_real_stats_to_unity_database.py.

Rationale: the formula-based generation in apply_real_stats over-relies on FG% for
layup/close_shot, which drags down high-volume three-point shooters who actually finish
well at the rim (Klay, Tatum, Murray, SGA, etc.). This file holds curator overrides
grouped by player_id, justified inline.

Re-run-safe: applies absolute UPDATEs, so running multiple times is idempotent.
"""
from __future__ import annotations

import sqlite3
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"

# Player overrides — format: { player_id: { attribute_name: new_value, "_note": "reason" } }
# Only specify attributes that need correcting; everything else is left untouched.
OVERRIDES: dict[int, dict] = {
    # ==== 2017 Golden State Warriors ====
    1040: {  # Stephen Curry — user explicitly asked 3pt=99
        "three_point": 99, "two_point": 92,
        "_note": "User: Curry's 3PT must be capped at 99. Peak Curry 2PT ~92.",
    },
    1042: {  # Kevin Durant — peak finisher, KD's 3PT was elite
        "three_point": 93, "layup": 96, "close_shot": 95, "two_point": 95,
        "_note": "Peak KD: 3PT bumped to elite, interior finishing to top-tier.",
    },
    1041: {  # Klay Thompson 2017 — elite shooter w/ good finishing
        "layup": 82, "close_shot": 80,
        "_note": "2017 Klay was a competent finisher, not 78/76.",
    },

    # ==== 2022 Golden State Warriors ====
    1001: {  # Klay Thompson 2022 — post-injury but still skilled
        "two_point": 78, "layup": 74, "close_shot": 70,
        "_note": "Post-injury Klay declined but 67/60/55 is too low for any version of him.",
    },
    1002: {  # Andrew Wiggins — mid-range scorer, decent finisher
        "layup": 80, "close_shot": 74, "two_point": 78,
        "_note": "Wiggins' mid-range game and finishing are decent; FG%-based generation under-rated him.",
    },
    1005: {  # Jordan Poole — score-first guard who attacks the rim
        "layup": 82, "close_shot": 74, "two_point": 75,
        "_note": "Poole drives well; 64/58 doesn't match scouting.",
    },
    1003: {  # Draymond Green 2022 — not a scorer but interior finish is OK
        "close_shot": 74, "layup": 74,
        "_note": "Slight bump for interior finishing realism.",
    },

    # ==== 2020 Los Angeles Lakers ====
    1052: {  # Kentavious Caldwell-Pope 2020
        "layup": 76, "close_shot": 70,
        "_note": "KCP's finishing was decent on the title team.",
    },

    # ==== 2019 Toronto Raptors ====
    1072: {  # Pascal Siakam — 2019 3PT was still developing
        "three_point": 65,
        "_note": "2019 Siakam shot 36.9% from 3 on low volume; 80 was overrated.",
    },
    1073: {  # Marc Gasol — declining 2019 but still skilled
        "layup": 72,
        "_note": "Slight bump.",
    },

    # ==== 2021 Milwaukee Bucks ====
    1063: {  # Brook Lopez — stretch big
        "layup": 70, "close_shot": 84,
        "_note": "Lopez finishes well at the rim despite low FG% (heavy 3PT volume).",
    },

    # ==== 2023 Denver Nuggets ====
    1011: {  # Jamal Murray — score-first PG
        "layup": 84, "close_shot": 76, "two_point": 80,
        "_note": "Murray is a fantastic mid-range/floater scorer; 65/59/67 is way off.",
    },
    1014: {  # KCP 2023
        "layup": 72, "close_shot": 66,
        "_note": "Same reasoning as 2020 KCP.",
    },
    1018: {  # Reggie Jackson — all-around scorer
        "layup": 70, "close_shot": 66, "two_point": 68,
        "_note": "Reggie's finishing isn't 55 across the board.",
    },

    # ==== 2024 Boston Celtics ====
    1020: {  # Jayson Tatum — elite interior finisher
        "layup": 88, "close_shot": 82, "two_point": 90,
        "_note": "Tatum is a top-5 interior scorer; 63/61 is completely wrong.",
    },
    1021: {  # Jaylen Brown — slasher
        "close_shot": 78, "two_point": 84,
        "_note": "Brown's mid-range is solid; layup 78 already OK.",
    },
    1022: {  # Jrue Holiday (Celtics version) — veteran scorer
        "layup": 78, "close_shot": 72, "two_point": 78,
        "_note": "Holiday is a competent mid-range/finisher.",
    },
    1023: {  # Derrick White — improved scorer in Boston
        "layup": 76, "close_shot": 70,
        "_note": "White improved finishing in Boston system.",
    },

    # ==== 2025 OKC Thunder ====
    1030: {  # SGA — premier inside-the-arc scorer in the league
        "layup": 96, "close_shot": 92, "two_point": 94, "post_scoring": 65,
        "_note": "SGA is the league's best at finishing in the paint and mid-range. Was massively under-rated.",
    },
    1031: {  # Jalen Williams — slasher / mid-range scorer
        "layup": 82, "close_shot": 76,
        "_note": "J-Dub finishes well at the rim.",
    },
    1032: {  # Chet Holmgren — long finisher
        "layup": 84, "close_shot": 80,
        "_note": "Chet's length makes him a great finisher.",
    },
    1033: {  # Luguentz Dort — limited offense but not terrible at rim
        "layup": 70, "close_shot": 64,
        "_note": "Slight bump for realistic dunker/finisher numbers.",
    },
}


def main() -> None:
    if not DB_PATH.exists():
        print(f"[ERROR] {DB_PATH} not found.")
        return

    con = sqlite3.connect(DB_PATH)
    con.row_factory = sqlite3.Row
    cur = con.cursor()

    changes = 0
    print(f"{'ID':<5} {'Name':<28} {'Attribute':<14} {'Before':>6} {'After':>6} {'Δ':>5}")
    print("-" * 75)

    for pid, overrides in OVERRIDES.items():
        cur.execute(
            "SELECT first_name||' '||last_name AS name FROM players WHERE id = ?",
            (pid,),
        )
        row = cur.fetchone()
        if row is None:
            print(f"[WARN] player_id {pid} not found, skipping.")
            continue
        name = row["name"]

        # Filter out the _note key
        attr_overrides = {k: v for k, v in overrides.items() if not k.startswith("_")}
        if not attr_overrides:
            continue

        # Read current values
        cols = ", ".join(attr_overrides.keys())
        cur.execute(f"SELECT {cols} FROM player_attributes WHERE player_id = ?", (pid,))
        current = cur.fetchone()
        if current is None:
            print(f"[WARN] player_attributes for {name} not found.")
            continue

        # Print diff
        for attr, new_val in attr_overrides.items():
            old_val = current[attr]
            delta = new_val - old_val
            sign = "+" if delta > 0 else ""
            print(f"{pid:<5} {name:<28} {attr:<14} {old_val:>6} {new_val:>6} {sign}{delta:>4}")

        # Apply UPDATE
        set_clause = ", ".join(f"{k} = ?" for k in attr_overrides.keys())
        vals = list(attr_overrides.values()) + [pid]
        cur.execute(f"UPDATE player_attributes SET {set_clause} WHERE player_id = ?", vals)
        changes += cur.rowcount

    con.commit()
    con.close()

    print()
    print(f"[OK] Applied overrides to {changes} player-attribute records.")
    print("[INFO] Now run tools/recalculate_overalls.py to refresh OVR.")


if __name__ == "__main__":
    main()
