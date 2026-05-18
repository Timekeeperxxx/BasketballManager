from __future__ import annotations
import json
import sqlite3
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"
FORMULA_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "rating_formula.json"

def clamp(value: float, lower: float, upper: float) -> float:
    return max(lower, min(upper, value))

def predict(features: list[float], formula: dict, min_ovr: int, max_ovr: int) -> int:
    bias = formula["bias"]
    weights = [w["value"] for w in formula["weights"]]
    score = bias + sum(weight * feature for weight, feature in zip(weights, features))
    return int(clamp(round(score), min_ovr, max_ovr))

def main():
    if not FORMULA_PATH.exists():
        print(f"[ERROR] Formula not found at {FORMULA_PATH}")
        return

    with open(FORMULA_PATH, "r", encoding="utf-8") as f:
        formula_data = json.load(f)

    min_ovr = formula_data["min_overall"]
    max_ovr = formula_data["max_overall"]
    feature_names = formula_data["features"]
    global_formula = formula_data["global"]
    position_formulas = {p["position"]: p for p in formula_data["positions"]}

    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row

    rows = conn.execute(f"""
        SELECT
            p.id, p.position,
            {', '.join(f'a.{f}' for f in feature_names)}
        FROM players p
        JOIN player_attributes a ON a.player_id = p.id
    """).fetchall()

    updates = []
    for row in rows:
        pid = row["id"]
        pos = row["position"]
        features = [float(row[f]) for f in feature_names]
        
        formula = position_formulas.get(pos, global_formula)
        new_ovr = predict(features, formula, min_ovr, max_ovr)
        
        updates.append((new_ovr, pid))

    cur = conn.cursor()
    cur.executemany("UPDATE players SET overall = ? WHERE id = ?", updates)
    conn.commit()
    conn.close()

    print(f"[INFO] Successfully recalculated overalls for {len(updates)} players.")

if __name__ == "__main__":
    main()
