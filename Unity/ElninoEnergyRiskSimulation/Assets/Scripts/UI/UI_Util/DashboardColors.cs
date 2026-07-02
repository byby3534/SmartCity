using UnityEngine;

/// <summary>
/// Seoul Energy Monitor 대시보드 UI 컬러 (React 목업 C 팔레트 기준).
/// </summary>
public static class DashboardColors
{
    // ── 배경·패널 ──────────────────────────────────────────
    public const string HexBg = "F0F2F7";
    public const string HexPanel = "FFFFFF";          // alpha 0.82 별도 적용
    public const string HexPanelBorder = "000000";    // alpha 0.07 별도 적용

    // ── 강조 (주요 Red) ───────────────────────────────────
    public const string HexAccent = "D93030";
    public const string HexAccentDark = "B91C1C";

    // ── 텍스트 ────────────────────────────────────────────
    public const string HexText = "0F172A";
    public const string HexTextSub = "64748B";
    public const string HexTextMono = "1E3A5F";

    // ── 보조 ──────────────────────────────────────────────
    public const string HexTeal = "0891B2";
    public const string HexPositive = "059669";

    // ── 위험도 단계 ───────────────────────────────────────
    public const string HexGradeStable = "059669";
    public const string HexGradeInterest = "EAB308";
    public const string HexGradeCaution = "F97316";
    public const string HexGradeWarning = "EF4444";
    public const string HexGradeCritical = "B91C1C";

    // ── ONI 범례 ──────────────────────────────────────────
    public const string HexLaNina = "3B82F6";
    public const string HexNeutral = "94A3B8";

    // ── 도넛 차트 (용도별) ─────────────────────────────────
    public const string HexDonutResidential = "D93030";
    public const string HexDonutGeneral = "F97316";
    public const string HexDonutEducation = "EAB308";
    public const string HexDonutIndustrial = "059669";
    public const string HexDonutAgriculture = "0891B2";
    public const string HexDonutStreetlight = "64748B";
    public const string HexDonutMidnight = "1E293B";
    public const string HexDonutOther = "CBD5E1";

    // ── 도넛 차트 (구별 사용량 내림차순 1~7위) ───────────────
    public const string HexDonutRank1 = "D93030";
    public const string HexDonutRank2 = "F97316";
    public const string HexDonutRank3 = "EAB308";
    public const string HexDonutRank4 = "059669";
    public const string HexDonutRank5 = "0891B2";
    public const string HexDonutRank6 = "64748B";
    public const string HexDonutRank7 = "1E293B";

    public static readonly Color Bg = Hex(HexBg);
    public static readonly Color Panel = new Color(1f, 1f, 1f, 0.82f);
    public static readonly Color PanelBorder = new Color(0f, 0f, 0f, 0.07f);

    /// <summary>대시보드 주요 강조 Red (#D93030)</summary>
    public static readonly Color Accent = Hex(HexAccent);
    public static readonly Color AccentGlow = new Color32(0xD9, 0x30, 0x30, 0x1F);
    public static readonly Color AccentDark = Hex(HexAccentDark);

    public static readonly Color Teal = Hex(HexTeal);
    public static readonly Color Text = Hex(HexText);
    public static readonly Color TextSub = Hex(HexTextSub);
    public static readonly Color TextMono = Hex(HexTextMono);
    public static readonly Color Positive = Hex(HexPositive);

    public static readonly Color GradeStable = Hex(HexGradeStable);
    public static readonly Color GradeInterest = Hex(HexGradeInterest);
    public static readonly Color GradeCaution = Hex(HexGradeCaution);
    public static readonly Color GradeWarning = Hex(HexGradeWarning);
    public static readonly Color GradeCritical = Hex(HexGradeCritical);

    public static readonly Color LaNina = Hex(HexLaNina);
    public static readonly Color Neutral = Hex(HexNeutral);

    /// <summary>ONI 슬라이더 트랙 — La Niña → Neutral → El Niño(Accent)</summary>
    public static readonly Color OniTrackLeft = LaNina;
    public static readonly Color OniTrackCenter = Neutral;
    public static readonly Color OniTrackRight = Accent;

    /// <summary>구별 용도 사용량 도넛 — 내림차순 1~7위에 순서대로 적용</summary>
    public static readonly Color[] DonutRankColors =
    {
    Hex(HexDonutRank1),
    Hex(HexDonutRank2),
    Hex(HexDonutRank3),
    Hex(HexDonutRank4),
    Hex(HexDonutRank5),
    Hex(HexDonutRank6),
    Hex(HexDonutRank7),
  };

    public static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString("#" + hex, out Color color))
            return color;

        Debug.LogWarning($"[DashboardColors] Invalid hex: #{hex}");
        return Color.magenta;
    }
}
