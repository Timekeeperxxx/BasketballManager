using System;
using System.IO;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using UnityEngine;

namespace BasketballManager.Core.Services
{
    public static class RatingCalculator
    {
        private const int DefaultMinOverall = 40;
        private const int DefaultMaxOverall = 99;
        private const string FormulaFileName = "rating_formula.json";

        private static readonly string[] FeatureNames =
        {
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
            "stamina"
        };

        private static RatingFormulaFile _cachedFormula;
        private static string _cachedFormulaPath = string.Empty;
        private static bool _loadAttempted;

        public static int CalculateOverall(Player player)
        {
            if (player == null)
            {
                return DefaultMinOverall;
            }

            return CalculateOverall(player.Position, player.Attributes);
        }

        public static int CalculateOverall(Position position, PlayerAttributes attributes)
        {
            if (attributes == null)
            {
                return DefaultMinOverall;
            }

            var formula = LoadFormula();
            if (formula == null)
            {
                return CalculateFallbackOverall(attributes);
            }

            var positionFormula = ResolvePositionFormula(formula, position);
            var score = CalculateScore(positionFormula, attributes);
            return ClampAndRound(score, formula.min_overall, formula.max_overall);
        }

        private static RatingFormulaFile LoadFormula()
        {
            var formulaPath = Path.Combine(Application.streamingAssetsPath, FormulaFileName);
            if (_loadAttempted && string.Equals(_cachedFormulaPath, formulaPath, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedFormula;
            }

            _loadAttempted = true;
            _cachedFormulaPath = formulaPath;
            _cachedFormula = null;

            try
            {
                if (!File.Exists(formulaPath))
                {
                    Debug.LogWarning($"[RatingCalculator] Formula file not found, using fallback: {formulaPath}");
                    return null;
                }

                var json = File.ReadAllText(formulaPath);
                var formula = JsonUtility.FromJson<RatingFormulaFile>(json);
                if (formula == null || formula.global == null || formula.global.weights == null || formula.global.weights.Length == 0)
                {
                    Debug.LogWarning($"[RatingCalculator] Formula file is invalid, using fallback: {formulaPath}");
                    return null;
                }

                _cachedFormula = formula;
                return _cachedFormula;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RatingCalculator] Failed to load formula, using fallback. {exception.Message}");
                return null;
            }
        }

        private static FormulaDefinition ResolvePositionFormula(RatingFormulaFile formula, Position position)
        {
            if (formula.positions != null)
            {
                var positionName = position.ToString();
                foreach (var entry in formula.positions)
                {
                    if (entry != null && string.Equals(entry.position, positionName, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }

            return formula.global;
        }

        private static float CalculateScore(FormulaDefinition formula, PlayerAttributes attributes)
        {
            var score = formula.bias;
            if (formula.weights == null)
            {
                return score;
            }

            foreach (var weightEntry in formula.weights)
            {
                if (weightEntry == null)
                {
                    continue;
                }

                score += GetAttributeValue(attributes, weightEntry.feature) * weightEntry.value;
            }

            return score;
        }

        private static int CalculateFallbackOverall(PlayerAttributes attributes)
        {
            float total =
                attributes.TwoPoint +
                attributes.ThreePoint +
                attributes.Layup +
                attributes.CloseShot +
                attributes.PostScoring +
                attributes.FreeThrow +
                attributes.Passing +
                attributes.BallHandle +
                attributes.Drive +
                attributes.DrawFoul +
                attributes.PerimeterDefense +
                attributes.InteriorDefense +
                attributes.Steal +
                attributes.Block +
                attributes.OffensiveRebound +
                attributes.DefensiveRebound +
                attributes.Speed +
                attributes.Strength +
                attributes.Stamina +
                attributes.OffensiveConsistency +
                attributes.DefensiveConsistency;

            return ClampAndRound(total / 21f, DefaultMinOverall, DefaultMaxOverall);
        }

        private static int ClampAndRound(float score, int minOverall, int maxOverall)
        {
            var min = minOverall <= 0 ? DefaultMinOverall : minOverall;
            var max = maxOverall <= 0 ? DefaultMaxOverall : maxOverall;
            return Mathf.Clamp(Mathf.RoundToInt(score), min, max);
        }

        private static float GetAttributeValue(PlayerAttributes attributes, string featureName)
        {
            return featureName switch
            {
                "two_point" => attributes.TwoPoint,
                "three_point" => attributes.ThreePoint,
                "layup" => attributes.Layup,
                "close_shot" => attributes.CloseShot,
                "post_scoring" => attributes.PostScoring,
                "free_throw" => attributes.FreeThrow,
                "passing" => attributes.Passing,
                "ball_handle" => attributes.BallHandle,
                "drive" => attributes.Drive,
                "draw_foul" => attributes.DrawFoul,
                "offensive_consistency" => attributes.OffensiveConsistency,
                "perimeter_defense" => attributes.PerimeterDefense,
                "interior_defense" => attributes.InteriorDefense,
                "steal" => attributes.Steal,
                "block" => attributes.Block,
                "offensive_rebound" => attributes.OffensiveRebound,
                "defensive_rebound" => attributes.DefensiveRebound,
                "defensive_consistency" => attributes.DefensiveConsistency,
                "speed" => attributes.Speed,
                "strength" => attributes.Strength,
                "stamina" => attributes.Stamina,
                _ => 0f
            };
        }

        [Serializable]
        private sealed class RatingFormulaFile
        {
            public int version;
            public int min_overall = DefaultMinOverall;
            public int max_overall = DefaultMaxOverall;
            public string[] features = FeatureNames;
            public FormulaDefinition global;
            public PositionFormulaDefinition[] positions;
        }

        [Serializable]
        private class FormulaDefinition
        {
            public float bias;
            public WeightEntry[] weights;
        }

        [Serializable]
        private sealed class PositionFormulaDefinition : FormulaDefinition
        {
            public string position = string.Empty;
        }

        [Serializable]
        private sealed class WeightEntry
        {
            public string feature = string.Empty;
            public float value;
        }
    }
}
