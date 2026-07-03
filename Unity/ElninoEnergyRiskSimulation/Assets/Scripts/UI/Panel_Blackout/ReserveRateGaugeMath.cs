using UnityEngine;

/// <summary>
/// 공급 예비율(%) → 게이지 니들 각도 변환.
/// </summary>
public static class ReserveRateGaugeMath
{
    private static readonly float[] Thresholds = ReserveRateStagePalette.Thresholds;
    private static readonly float[] NeedleAngles = { 72f, 36f, 0f, -36f, -72f };

    private const float NormalRangeUpper = 20f;
    private const float SegmentHalfWidth = 18f;

    public static float ToAngle(float reserveRate)
    {
        reserveRate = Mathf.Max(0f, reserveRate);
        float normalLower = Thresholds[0];

        if (reserveRate >= NormalRangeUpper)
            return SegmentStart(0);

        if (reserveRate >= normalLower)
        {
            float t = Mathf.InverseLerp(normalLower, NormalRangeUpper, reserveRate);
            return Mathf.Lerp(SegmentEnd(0), SegmentStart(0), t);
        }

        for (int i = 0; i < 4; i++)
        {
            float upper = Thresholds[i];
            float lower = Thresholds[i + 1];
            if (reserveRate >= lower)
            {
                int seg = i + 1;
                float t = Mathf.InverseLerp(lower, upper, reserveRate);
                return Mathf.Lerp(SegmentEnd(seg), SegmentStart(seg), t);
            }
        }

        float tCritical = Mathf.InverseLerp(Thresholds[4], Thresholds[3], reserveRate);
        return Mathf.Lerp(SegmentEnd(4), SegmentStart(4), tCritical);
    }

    private static float SegmentStart(int level) => NeedleAngles[level] + SegmentHalfWidth;
    private static float SegmentEnd(int level) => NeedleAngles[level] - SegmentHalfWidth;

    public static int AngleToLevel(float angle)
    {
        angle = NormalizeAngle(angle);
        if (angle >= SegmentEnd(0)) return 0;
        if (angle >= SegmentEnd(1)) return 1;
        if (angle >= SegmentEnd(2)) return 2;
        if (angle >= SegmentEnd(3)) return 3;
        return 4;
    }

    private static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }
}
