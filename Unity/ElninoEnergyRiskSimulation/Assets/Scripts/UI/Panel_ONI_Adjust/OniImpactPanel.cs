using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Panel_ONI_Adjust/Panel_4 — ONI 슬라이더 조작 시 연/월 선택 직후 기준값(baseline) 대비
/// 온도·전력 사용량·전력 공급량 변화량과 현재 예비율을 보여준다.
/// </summary>
public class OniImpactPanel : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private DataManager dataManager;
    [SerializeField] private UIController uiController;

    [Header("4개 변화량 패널")]
    [SerializeField] private TMP_Text temperatureRatioText;
    [SerializeField] private TMP_Text energyUsageRatioText;
    [SerializeField] private TMP_Text energySupplyRatioText;
    [SerializeField] private TMP_Text reserveRateRatioText;

    private readonly List<OniRangeData> _oniRangeEntries = new();
    private OniRangeData _baseline;

    private void Awake()
    {
        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();

        ResolveReferences();
    }

    private void OnEnable()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;

        if (uiController != null)
            uiController.OnOniValueChanged += HandleOniValueChanged;

        if (_oniRangeEntries.Count > 0 && uiController != null)
            HandleOniValueChanged(uiController.GetCurrentOni());
    }

    private void OnDisable()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;

        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniValueChanged;
    }

    // 연/월 선택 → /predict/oni_range 도착 시점 — 슬라이더 초기값을 기준값(baseline)으로 고정한다.
    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        _oniRangeEntries.Clear();
        _baseline = null;

        if (data == null || data.Count == 0)
            return;

        _oniRangeEntries.AddRange(data);

        float oni = uiController != null ? uiController.GetCurrentOni() : 0f;
        _baseline = GetClosestOniEntry(oni);

        ApplyEntry(_baseline);
    }

    // 슬라이더 드래그 중 — 기준값(baseline) 대비 변화량을 갱신한다.
    private void HandleOniValueChanged(float oniValue)
    {
        if (_baseline == null || _oniRangeEntries.Count == 0)
            return;

        ApplyEntry(GetClosestOniEntry(oniValue));
    }

    private void ApplyEntry(OniRangeData entry)
    {
        if (entry == null || _baseline == null)
            return;

        float temperatureDelta = entry.seoulTemperature - _baseline.seoulTemperature;
        float usageDelta = PercentDelta(entry.seoulTotalConsumption, _baseline.seoulTotalConsumption);
        float supplyDelta = PercentDelta(entry.supplyPower, _baseline.supplyPower);

        if (temperatureRatioText != null)
            temperatureRatioText.text = $"{temperatureDelta:+0.0;-0.0;0.0}°C";
        if (energyUsageRatioText != null)
            energyUsageRatioText.text = $"{usageDelta:+0.00;-0.00;0.00}%";
        if (energySupplyRatioText != null)
            energySupplyRatioText.text = $"{supplyDelta:+0.00;-0.00;0.00}%";
        if (reserveRateRatioText != null)
        {
            float reserveRate = Mathf.Max(0f, entry.reserveRate);
            reserveRateRatioText.text = $"{reserveRate:F1}%";
            reserveRateRatioText.color = ReserveRateStagePalette.GetSegmentColor(
                ReserveRateStagePalette.ToLevel(reserveRate));
        }
    }

    private static float PercentDelta(float current, float baseline)
    {
        if (Mathf.Approximately(baseline, 0f))
            return 0f;

        return (current - baseline) / baseline * 100f;
    }

    private OniRangeData GetClosestOniEntry(float oniValue)
    {
        OniRangeData closest = null;
        float minDistance = float.MaxValue;

        foreach (OniRangeData data in _oniRangeEntries)
        {
            float distance = Mathf.Abs(data.oni - oniValue);
            if (distance < minDistance)
            {
                minDistance = distance;
                closest = data;
            }
        }

        return closest;
    }

    private void ResolveReferences()
    {
        if (temperatureRatioText == null)
            temperatureRatioText = FindRatioText("Panel_Temperature");
        if (energyUsageRatioText == null)
            energyUsageRatioText = FindRatioText("Panel_EnergyUsage");
        if (energySupplyRatioText == null)
            energySupplyRatioText = FindRatioText("Panel_EnergySupply");
        if (reserveRateRatioText == null)
            reserveRateRatioText = FindRatioText("Panel_ReserveRate");
        else
        {
            // Inspector에 라벨 TMP가 잘못 연결된 경우 Text_Ratio로 교정
            TMP_Text resolved = FindRatioText("Panel_ReserveRate");
            if (resolved != null && resolved != reserveRateRatioText)
                reserveRateRatioText = resolved;
        }
    }

    private TMP_Text FindRatioText(string panelName)
    {
        Transform panel = FindChildByName(transform, panelName);
        if (panel == null) return null;

        return panel.Find("Text_Ratio")?.GetComponent<TMP_Text>();
    }

    private static Transform FindChildByName(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }

        return null;
    }
}
