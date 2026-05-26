using System.Collections.Generic;

namespace BasketballManager.Core.Models
{
    public enum ShotZone
    {
        // Three-point zones
        ThreeLeftCorner,
        ThreeRightCorner,
        ThreeLeftWing,
        ThreeRightWing,
        ThreeTopKey,

        // Mid-range zones
        MidLeftCorner,
        MidRightCorner,
        MidLeftElbow,
        MidRightElbow,
        MidTopKey,

        // Close-range positional zones (surrounding basket)
        CloseLeft,
        CloseCenter,
        CloseRight,

        // Directly under basket
        Basket
    }

    public static class ShotZoneHelper
    {
        public static string ToChinese(this ShotZone zone) => zone switch
        {
            ShotZone.ThreeLeftCorner  => "左底角三分",
            ShotZone.ThreeRightCorner => "右底角三分",
            ShotZone.ThreeLeftWing    => "左斜翼三分",
            ShotZone.ThreeRightWing   => "右斜翼三分",
            ShotZone.ThreeTopKey      => "弧顶三分",
            ShotZone.MidLeftCorner    => "左底角中投",
            ShotZone.MidRightCorner   => "右底角中投",
            ShotZone.MidLeftElbow     => "左肘区中投",
            ShotZone.MidRightElbow    => "右肘区中投",
            ShotZone.MidTopKey        => "中路中投",
            ShotZone.CloseLeft        => "左侧近距离",
            ShotZone.CloseCenter      => "中路近距离",
            ShotZone.CloseRight       => "右侧近距离",
            ShotZone.Basket           => "篮下",
            _                         => "出手"
        };

        /// <summary>
        /// Computes the probability of shooting from each of the 14 zones,
        /// based on the player's tendency values. Values sum to 1.0f.
        /// </summary>
        public static Dictionary<ShotZone, float> ComputeZoneProbabilities(PlayerTendencies t)
        {
            // Distance denominators
            float sumThreeZones = t.ZoneThreeLeftCorner + t.ZoneThreeRightCorner
                                + t.ZoneThreeLeftWing + t.ZoneThreeRightWing + t.ZoneThreeTopKey;
            float sumMidZones   = t.ZoneMidLeftCorner + t.ZoneMidRightCorner
                                + t.ZoneMidLeftElbow + t.ZoneMidRightElbow + t.ZoneMidTopKey;
            float sumCloseZones = t.ZoneCloseLeft + t.ZoneCloseCenter + t.ZoneCloseRight;

            // Overall distance tendencies
            float threeDist = t.ThreeTendency;
            float midDist   = t.TwoPointTendency;
            // Close = drive + post + closeShot combined bucket
            float closeDist = t.DriveTendency + t.PostTendency + t.CloseShotTendency;
            float totalDist = threeDist + midDist + closeDist;
            if (totalDist <= 0) totalDist = 1f;

            float threePct = threeDist / totalDist;
            float midPct   = midDist   / totalDist;
            float closePct = closeDist / totalDist;

            // Within close: surrounding zones vs basket
            float closeTotal = sumCloseZones + t.CloseShotTendency;
            if (closeTotal <= 0) closeTotal = 1f;

            var result = new Dictionary<ShotZone, float>(14);

            // Three-point zones
            float threeBase = sumThreeZones > 0 ? threePct / sumThreeZones : 0f;
            result[ShotZone.ThreeLeftCorner]  = threeBase * t.ZoneThreeLeftCorner;
            result[ShotZone.ThreeRightCorner] = threeBase * t.ZoneThreeRightCorner;
            result[ShotZone.ThreeLeftWing]    = threeBase * t.ZoneThreeLeftWing;
            result[ShotZone.ThreeRightWing]   = threeBase * t.ZoneThreeRightWing;
            result[ShotZone.ThreeTopKey]      = threeBase * t.ZoneThreeTopKey;

            // Mid-range zones
            float midBase = sumMidZones > 0 ? midPct / sumMidZones : 0f;
            result[ShotZone.MidLeftCorner]  = midBase * t.ZoneMidLeftCorner;
            result[ShotZone.MidRightCorner] = midBase * t.ZoneMidRightCorner;
            result[ShotZone.MidLeftElbow]   = midBase * t.ZoneMidLeftElbow;
            result[ShotZone.MidRightElbow]  = midBase * t.ZoneMidRightElbow;
            result[ShotZone.MidTopKey]      = midBase * t.ZoneMidTopKey;

            // Close surrounding zones
            float closeBase = sumCloseZones > 0 ? closePct * (sumCloseZones / closeTotal) / sumCloseZones : 0f;
            result[ShotZone.CloseLeft]   = closeBase * t.ZoneCloseLeft;
            result[ShotZone.CloseCenter] = closeBase * t.ZoneCloseCenter;
            result[ShotZone.CloseRight]  = closeBase * t.ZoneCloseRight;

            // Basket zone
            result[ShotZone.Basket] = closePct * (t.CloseShotTendency / closeTotal);

            return result;
        }
    }
}
