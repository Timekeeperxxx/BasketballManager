"""One-shot fix for historical_champion_player_stats.csv column-shift bug.

The original CSV was filled with all stat columns shifted left by 1, starting
from height_cm. That means height_cm got dropped, weight_kg landed in the
height_cm column, age in weight_kg, gp in age, mpg in gp, fg_pct in mpg, etc.

This script shifts every row from height_cm onward right by 1 column, fills
height_cm with a position-based default, and writes the file back.

Run once. Idempotent guard: aborts if first row already has plausible height_cm
(>= 170) so re-running is a no-op.
"""
from __future__ import annotations

import csv
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
CSV_PATH = PROJECT_ROOT / "data" / "source" / "historical_champion_player_stats.csv"

POSITION_HEIGHT_DEFAULT = {
    "PG": 185,
    "SG": 193,
    "SF": 198,
    "PF": 203,
    "C": 210,
}

# Columns to shift right by 1 (everything from height_cm to source_note)
SHIFT_COLUMNS = [
    "height_cm", "weight_kg", "age", "gp", "mpg",
    "fg_pct", "three_pct", "ft_pct",
    "rpg", "apg", "spg", "bpg", "ppg", "source_note",
]


def main() -> None:
    if not CSV_PATH.exists():
        print(f"[ERROR] {CSV_PATH} not found.")
        return

    with open(CSV_PATH, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        fieldnames = reader.fieldnames
        rows = list(reader)

    if not rows:
        print("[INFO] CSV is empty, nothing to do.")
        return

    # Idempotency: if first row already has a plausible height, assume fixed.
    try:
        h0 = int(rows[0]["height_cm"])
        if h0 >= 170:
            print(f"[INFO] First row has height_cm={h0} (>=170). Assuming already fixed. No changes.")
            return
    except (KeyError, ValueError):
        pass

    fixed = 0
    for row in rows:
        # Read original (shifted) values from columns
        original = {col: row.get(col, "") for col in SHIFT_COLUMNS}

        # Shift right by 1: weight_kg <- old height_cm, age <- old weight_kg, ...
        # height_cm gets filled from position default.
        for i in range(len(SHIFT_COLUMNS) - 1, 0, -1):
            row[SHIFT_COLUMNS[i]] = original[SHIFT_COLUMNS[i - 1]]

        pos = (row.get("position") or "").strip()
        row["height_cm"] = str(POSITION_HEIGHT_DEFAULT.get(pos, 198))
        fixed += 1

    with open(CSV_PATH, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    print(f"[OK] Shifted {fixed} rows in {CSV_PATH.name}.")
    print(f"     height_cm filled with position defaults (PG=185, SG=193, SF=198, PF=203, C=210).")
    print(f"     Verify a sample with: head -2 {CSV_PATH}")


if __name__ == "__main__":
    main()
