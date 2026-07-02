using UnityEngine;

/// <summary>
/// 공급 예비율 단계별 색상·문구 — BlackoutGaugePanel 세그먼트 색과 동일한 기준을 사용한다.
/// </summary>
public static class ReserveRateStagePalette
{
    public const float DefaultReserveRate = 10f;

    public static readonly float[] Thresholds = { 15f, 10f, 7f, 5f, 0f };

    public static readonly Color SegmentNormal = new(0.25f, 0.55f, 0.85f);
    public static readonly Color SegmentInterest = new(0.40f, 0.75f, 0.90f);
    public static readonly Color SegmentCaution = new(0.95f, 0.78f, 0.10f);
    public static readonly Color SegmentWarning = new(0.93f, 0.46f, 0.10f);
    public static readonly Color SegmentCritical = new(0.85f, 0.15f, 0.20f);

    public static readonly Color ButtonDisabled = new(0.737f, 0.737f, 0.737f, 1f);
    public static readonly Color ButtonActive = new(0.831f, 0f, 0f, 1f);

    public static readonly Color[] SegmentColors =
    {
        SegmentNormal,
        SegmentInterest,
        SegmentCaution,
        SegmentWarning,
        SegmentCritical,
    };

    public static int ToLevel(float reserveRate)
    {
        reserveRate = Mathf.Max(0f, reserveRate);

        for (int i = 0; i < Thresholds.Length; i++)
        {
            if (reserveRate >= Thresholds[i])
                return i;
        }

        return SegmentColors.Length - 1;
    }

    public static Color GetSegmentColor(int level)
    {
        if (level < 0)
            return SegmentNormal;

        return SegmentColors[Mathf.Clamp(level, 0, SegmentColors.Length - 1)];
    }

    public static string GetStageName(int level)
    {
        return level switch
        {
            0 => "정상",
            1 => "관심",
            2 => "주의",
            3 => "경계",
            4 => "심각",
            _ => "정상",
        };
    }

    public static string GetStageTitle(int level) => $"{GetStageName(level)}단계";

    public static string GetStageDescription(int level)
    {
        return level switch
        {
            0 => "(15% 이상)",
            1 => "(10% ~ 15%)",
            2 => "(7% ~ 10%)",
            3 => "(5% ~ 7%)",
            4 => "기준치 하회\n(5% 미만)",
            _ => string.Empty,
        };
    }

    public static bool CanSimulate(int level) => level >= 4;

    public static int FromEmergencyLabel(string label)
    {
        return label switch
        {
            "심각" => 4,
            "경계" => 3,
            "주의" => 2,
            "관심" => 1,
            _ => 0,
        };
    }

    public static Color GetColorForEmergencyLabel(string label) =>
        GetSegmentColor(FromEmergencyLabel(label));
}
