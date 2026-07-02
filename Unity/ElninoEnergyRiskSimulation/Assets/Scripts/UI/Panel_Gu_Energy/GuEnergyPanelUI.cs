using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel_EnergyRatio — 선택 구의 "전력 사용 비율" 카드.
/// 서울 전체 대비 사용 비율(Text_Ratio + Bar_Usage)과
/// 용도별 사용량 도넛(Panel_UsgaeDonut/Donut_1~7 + Panel_Legend/Legend_1~7)을 갱신한다.
/// </summary>
public class GuEnergyPanelUI : MonoBehaviour
{
    private static readonly (string Key, string DisplayName)[] UsageTypes =
    {
        ("주택용", "주거"),
        ("일반용", "일반"),
        ("교육용", "교육"),
        ("산업용", "산업"),
        ("농사용", "농사"),
        ("가로등", "가로등"),
        ("심야", "심야"),
    };

    [Header("전력 사용 비율")]
    [SerializeField] private TMP_Text ratioText;
    [SerializeField] private RectTransform barBackground;
    [SerializeField] private RectTransform barFill;

    [Header("용도별 도넛 (사용량 내림차순 1~7위)")]
    [SerializeField] private DonutMeshRenderer[] donutSegments = new DonutMeshRenderer[7];
    [SerializeField] private Image[] legendDots = new Image[7];
    [SerializeField] private TMP_Text[] legendTexts = new TMP_Text[7];

    [Header("데이터")]
    [SerializeField] private DataManager dataManager;
    [SerializeField] private MinimapManager minimapManager;

    private const DistrictType DefaultDistrict = DistrictType.JONGNO;
    private const float DonutStartAngleDeg = 90f;
    private const int SegmentCount = 7;

    private readonly Dictionary<DistrictType, DistrictData> _districtDataMap = new();
    private float _seoulTotalConsumption;
    private bool _hasPredictData;
    private DistrictType _selectedDistrict = DefaultDistrict;
    private CanvasGroup _panelCanvasGroup;

    private void Awake()
    {
        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();
        if (minimapManager == null)
            minimapManager = FindFirstObjectByType<MinimapManager>();

        ResolveReferences();
        EnsureCanvasGroup();
        SetPanelVisible(false);
    }

    private void OnEnable()
    {
        if (dataManager != null)
        {
            dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;
            dataManager.OnDistrictDataUpdated += HandleDistrictDataUpdated;
        }

        if (minimapManager != null)
            minimapManager.OnDistrictSelected += HandleDistrictSelected;
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
            dataManager.OnDistrictDataUpdated -= HandleDistrictDataUpdated;
        }

        if (minimapManager != null)
            minimapManager.OnDistrictSelected -= HandleDistrictSelected;
    }

    private void HandlePowerDataUpdated(PowerGridData data)
    {
        _districtDataMap.Clear();
        _seoulTotalConsumption = data.seoulTotalConsumption;
        _hasPredictData = true;
        SetPanelVisible(true);

        if (_districtDataMap.TryGetValue(_selectedDistrict, out DistrictData selected))
            RefreshDisplayFromData(selected);
    }

    // predict의 구역별 데이터 도착 — 현재 선택된 구(기본값 종로구)라면 패널 갱신
    private void HandleDistrictDataUpdated(DistrictData data)
    {
        _districtDataMap[data.districtType] = data;

        if (_hasPredictData && _selectedDistrict == data.districtType)
            RefreshDisplay();
    }

    private void HandleDistrictSelected(DistrictType districtType)
    {
        _selectedDistrict = districtType;

        if (_hasPredictData && _districtDataMap.ContainsKey(districtType))
            RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (!_hasPredictData || !_districtDataMap.TryGetValue(_selectedDistrict, out DistrictData data))
            return;

        RefreshDisplayFromData(data);
    }

    private void RefreshDisplayFromData(DistrictData data)
    {
        float ratio = _seoulTotalConsumption > 0f
            ? (float)data.totalPowerUsage / _seoulTotalConsumption
            : 0f;

        UpdateRatio(ratio);
        UpdateDonutAndLegend(data);
    }

    private void SetPanelVisible(bool visible)
    {
        if (_panelCanvasGroup == null)
            return;

        _panelCanvasGroup.alpha = visible ? 1f : 0f;
        _panelCanvasGroup.interactable = visible;
        _panelCanvasGroup.blocksRaycasts = visible;
    }

    private void EnsureCanvasGroup()
    {
        _panelCanvasGroup = GetComponent<CanvasGroup>();
        if (_panelCanvasGroup == null)
            _panelCanvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void UpdateRatio(float ratio)
    {
        if (ratioText != null)
            ratioText.text = $"{ratio * 100f:0}%";

        if (barFill != null && barBackground != null)
        {
            float trackWidth = barBackground.rect.width;
            var size = barFill.sizeDelta;
            size.x = trackWidth * Mathf.Clamp01(ratio);
            barFill.sizeDelta = size;
        }
    }

    private void UpdateDonutAndLegend(DistrictData data)
    {
        var ranked = GetRankedUsages(data);
        float total = ranked.Sum(u => u.Value);

        float startAngle = DonutStartAngleDeg;

        for (int i = 0; i < donutSegments.Length; i++)
        {
            if (i >= ranked.Count)
            {
                HideSegment(i);
                continue;
            }

            (string displayName, float value) = ranked[i];
            float share = total > 0f ? value / total : 0f;
            float sweep = share * 360f;
            float endAngle = startAngle - sweep;

            Color rankColor = DashboardColors.DonutRankColors[i % DashboardColors.DonutRankColors.Length];

            if (donutSegments[i] != null)
            {
                donutSegments[i].StartAngleDeg = startAngle;
                donutSegments[i].EndAngleDeg = endAngle;
                donutSegments[i].ApplyDisplayColor(rankColor);
            }

            if (legendDots[i] != null)
                legendDots[i].color = rankColor;

            if (legendTexts[i] != null)
                legendTexts[i].text = $"{displayName} {share * 100f:0}%";

            startAngle = endAngle;
        }
    }

    private void HideSegment(int index)
    {
        if (donutSegments[index] != null)
            donutSegments[index].EndAngleDeg = donutSegments[index].StartAngleDeg;

        if (legendTexts[index] != null)
            legendTexts[index].text = string.Empty;
    }

    private static List<(string DisplayName, float Value)> GetRankedUsages(DistrictData data)
    {
        var usages = new List<(string DisplayName, float Value)>();

        if (data.typePowerUsage == null)
            return usages;

        foreach (var (key, displayName) in UsageTypes)
        {
            if (data.typePowerUsage.TryGetValue(key, out float value))
                usages.Add((displayName, value));
        }

        usages.Sort((a, b) => b.Value.CompareTo(a.Value));
        return usages;
    }

    private void ResolveReferences()
    {
        donutSegments = EnsureArrayLength(donutSegments, SegmentCount);
        legendDots = EnsureArrayLength(legendDots, SegmentCount);
        legendTexts = EnsureArrayLength(legendTexts, SegmentCount);

        if (ratioText == null) ratioText = FindText("Text_Ratio", transform);
        if (barBackground == null) barBackground = FindRect("Img_Background");
        if (barFill == null) barFill = FindRect("Img_Fill");

        Transform donutRoot = FindChild(transform, "Panel_UsgaeDonut");
        Transform legendRoot = FindChild(transform, "Panel_Legend");

        for (int i = 0; i < SegmentCount; i++)
        {
            if (donutSegments[i] == null && donutRoot != null)
            {
                Transform donut = donutRoot.Find($"Donut_{i + 1}");
                donutSegments[i] = donut != null ? donut.GetComponent<DonutMeshRenderer>() : null;
            }

            if (legendRoot == null) continue;

            Transform legend = legendRoot.Find($"Legend_{i + 1}");
            if (legend == null) continue;

            if (legendDots[i] == null)
                legendDots[i] = legend.GetComponent<Image>();

            if (legendTexts[i] == null)
                legendTexts[i] = legend.GetComponentInChildren<TMP_Text>(true);
        }
    }

    private static T[] EnsureArrayLength<T>(T[] array, int length)
    {
        if (array != null && array.Length == length)
            return array;

        return new T[length];
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }

        return null;
    }

    private RectTransform FindRect(string name)
    {
        Transform found = FindChild(transform, name);
        return found != null ? found.GetComponent<RectTransform>() : null;
    }

    private static TMP_Text FindText(string objectName, Transform root)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.gameObject.name == objectName)
                return text;
        }

        return null;
    }
}
