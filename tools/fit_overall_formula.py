from __future__ import annotations

import json
import math
import sqlite3
from collections import Counter
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"
OUTPUT_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "rating_formula.json"

FEATURES = [
    "two_point",
    "three_point",
    "layup",
    "close_shot",
    "post_scoring",
    "free_throw",
    "passing",
    "ball_handle",
    "drive",
    "draw_foul",
    "offensive_consistency",
    "perimeter_defense",
    "interior_defense",
    "steal",
    "block",
    "offensive_rebound",
    "defensive_rebound",
    "defensive_consistency",
    "speed",
    "strength",
    "stamina",
]

POSITIONS = ["PG", "SG", "SF", "PF", "C"]
MIN_OVERALL = 40
MAX_OVERALL = 99


def load_samples() -> list[dict]:
    if not DB_PATH.exists():
        raise FileNotFoundError(f"Database not found: {DB_PATH}")

    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    try:
        rows = conn.execute(
            """
            SELECT
                p.id,
                p.position,
                p.overall,
                a.two_point,
                a.three_point,
                a.layup,
                a.close_shot,
                a.post_scoring,
                a.free_throw,
                a.passing,
                a.ball_handle,
                a.drive,
                a.draw_foul,
                a.offensive_consistency,
                a.perimeter_defense,
                a.interior_defense,
                a.steal,
                a.block,
                a.offensive_rebound,
                a.defensive_rebound,
                a.defensive_consistency,
                a.speed,
                a.strength,
                a.stamina
            FROM players p
            JOIN player_attributes a ON a.player_id = p.id
            ORDER BY p.id
            """
        ).fetchall()
    finally:
        conn.close()

    samples: list[dict] = []
    for row in rows:
        sample = {
            "id": int(row["id"]),
            "position": row["position"],
            "overall": float(row["overall"]),
            "features": [float(row[name]) for name in FEATURES],
        }
        samples.append(sample)
    return samples


def mean(values: list[float]) -> float:
    return sum(values) / len(values) if values else 0.0


def clamp(value: float, lower: float, upper: float) -> float:
    return max(lower, min(upper, value))


def normalize_weights(weights: list[float], target_sum: float) -> list[float]:
    total = sum(weights)
    if total <= 1e-8:
        return [target_sum / len(weights)] * len(weights)
    scale = target_sum / total
    return [weight * scale for weight in weights]


def initial_weights(samples: list[dict]) -> tuple[list[float], float]:
    y_values = [sample["overall"] for sample in samples]
    y_mean = mean(y_values)
    covariance_scores: list[float] = []

    for feature_index in range(len(FEATURES)):
        x_values = [sample["features"][feature_index] / 100.0 for sample in samples]
        x_mean = mean(x_values)
        covariance = sum((x - x_mean) * (y - y_mean) for x, y in zip(x_values, y_values))
        covariance_scores.append(max(covariance, 0.0) + 1e-6)

    weights = normalize_weights(covariance_scores, 55.0)
    avg_prediction = mean(
        [sum(weight * (feature / 100.0) for weight, feature in zip(weights, sample["features"])) for sample in samples]
    )
    bias = y_mean - avg_prediction
    return weights, bias


def fit_formula(samples: list[dict]) -> tuple[list[float], float]:
    weights, bias = initial_weights(samples)
    learning_rate = 0.0035
    ridge = 0.0008
    max_weight = 20.0

    for _ in range(9000):
        gradient_weights = [0.0] * len(FEATURES)
        gradient_bias = 0.0

        for sample in samples:
            x_values = [value / 100.0 for value in sample["features"]]
            predicted = bias + sum(weight * x for weight, x in zip(weights, x_values))
            error = predicted - sample["overall"]
            gradient_bias += error
            for index, x_value in enumerate(x_values):
                gradient_weights[index] += error * x_value

        count = float(len(samples))
        gradient_bias /= count
        for index in range(len(weights)):
            gradient_weights[index] = gradient_weights[index] / count + ridge * weights[index]
            weights[index] = clamp(weights[index] - learning_rate * gradient_weights[index], 0.0, max_weight)

        bias -= learning_rate * gradient_bias

    avg_prediction = mean(
        [sum(weight * (feature / 100.0) for weight, feature in zip(weights, sample["features"])) for sample in samples]
    )
    bias = mean([sample["overall"] for sample in samples]) - avg_prediction

    raw_weights = [round(weight / 100.0, 6) for weight in weights]
    return raw_weights, round(bias, 6)


def blend_formula(global_formula: tuple[list[float], float], position_formula: tuple[list[float], float]) -> tuple[list[float], float]:
    global_weights, global_bias = global_formula
    local_weights, local_bias = position_formula
    blended_weights = [
        round(local_weight * 0.7 + global_weight * 0.3, 6)
        for local_weight, global_weight in zip(local_weights, global_weights)
    ]
    blended_bias = round(local_bias * 0.7 + global_bias * 0.3, 6)
    return blended_weights, blended_bias


def predict(sample: dict, formula: tuple[list[float], float]) -> int:
    weights, bias = formula
    score = bias + sum(weight * feature for weight, feature in zip(weights, sample["features"]))
    return int(clamp(round(score), MIN_OVERALL, MAX_OVERALL))


def mae(samples: list[dict], formula: tuple[list[float], float]) -> float:
    if not samples:
        return 0.0
    errors = [abs(sample["overall"] - predict(sample, formula)) for sample in samples]
    return sum(errors) / len(errors)


def build_json_formula(global_formula: tuple[list[float], float], position_formulas: dict[str, tuple[list[float], float]]) -> dict:
    def make_formula_entry(bias: float, weights: list[float]) -> dict:
        return {
            "bias": round(bias, 6),
            "weights": [{"feature": feature, "value": round(weight, 6)} for feature, weight in zip(FEATURES, weights)],
        }

    return {
        "version": 1,
        "min_overall": MIN_OVERALL,
        "max_overall": MAX_OVERALL,
        "features": FEATURES,
        "global": make_formula_entry(global_formula[1], global_formula[0]),
        "positions": [
            {
                "position": position,
                **make_formula_entry(position_formulas[position][1], position_formulas[position][0]),
            }
            for position in POSITIONS
        ],
    }


def main() -> None:
    samples = load_samples()
    if not samples:
        raise RuntimeError("No player samples found in database.")

    position_counts = Counter(sample["position"] for sample in samples)
    global_formula = fit_formula(samples)

    position_formulas: dict[str, tuple[list[float], float]] = {}
    for position in POSITIONS:
        position_samples = [sample for sample in samples if sample["position"] == position]
        if len(position_samples) < 5:
            position_formulas[position] = global_formula
            continue

        position_formula = fit_formula(position_samples)
        position_formulas[position] = blend_formula(global_formula, position_formula)

    global_mae = mae(samples, global_formula)
    position_maes: dict[str, float] = {}
    max_error_sample = None
    max_error_value = -1.0

    for position in POSITIONS:
        position_samples = [sample for sample in samples if sample["position"] == position]
        if not position_samples:
            position_maes[position] = 0.0
            continue

        formula = position_formulas[position]
        position_maes[position] = mae(position_samples, formula)

        for sample in position_samples:
            predicted = predict(sample, formula)
            error = abs(sample["overall"] - predicted)
            if error > max_error_value:
                max_error_value = error
                max_error_sample = {
                    "id": sample["id"],
                    "position": sample["position"],
                    "actual": int(sample["overall"]),
                    "predicted": predicted,
                    "error": int(error),
                }

    formula_json = build_json_formula(global_formula, position_formulas)
    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT_PATH.write_text(json.dumps(formula_json, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Loaded players: {len(samples)}")
    print("Position samples:")
    for position in POSITIONS:
        print(f"{position}: {position_counts.get(position, 0)}")

    print()
    print(f"Global MAE: {global_mae:.2f}")
    for position in POSITIONS:
        print(f"{position} MAE: {position_maes[position]:.2f}")

    print()
    print("Max error:")
    if max_error_sample is None:
        print("No samples")
    else:
        print(
            "player_id={id} position={position} actual={actual} predicted={predicted} error={error}".format(
                **max_error_sample
            )
        )

    print()
    print("Saved formula:")
    print(OUTPUT_PATH)


if __name__ == "__main__":
    main()
