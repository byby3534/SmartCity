using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Panel_ONI_Adjust/Panel_4 — /oni로 받은 실제 ONI를 baseline으로 삼고,
/// /predict/oni_range 캐시에서 현재 슬라이더 ONI의 변화량을 표시한다.
/// 온도·사용·공급은 baseline 대비 delta, 예비율은 oni_range 절대값.
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

    private readonly List<OniRangeData> _entries = new();
    private OniRangeData _baseline;

    private void Awake()
    {
        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();

        ResolveReferences();
        WarnMissingReferences();
        ResetToZero();

        if (uiController != null)
            uiController.OnDateSelected += HandleDateSelected;
    }

    private void OnDestroy()
    {
        if (uiController != null)
            uiController.OnDateSelected -= HandleDateSelected;
    }

    private void WarnMissingReferences()
    {
        WarnIfNull(temperatureRatioText, "Panel_Temperature/Text_Ratio");
        WarnIfNull(energyUsageRatioText, "Panel_EnergyUsage/Text_Ratio");
        WarnIfNull(energySupplyRatioText, "Panel_EnergySupply/Text_Ratio");
        WarnIfNull(reserveRateRatioText, "Panel_ReserveRate/Text_Ratio");
    }

    private void WarnIfNull(TMP_Text text, string expectedPath)
    {
        if (text != null)
            return;

        Debug.LogWarning(
            $"[{nameof(OniImpactPanel)}] TMP_Text가 연결되지 않았습니다. " +
            $"인스펙터에서 연결하거나 하이어라키에 {expectedPath}가 있는지 확인하세요.",
            this);
    }

    private void OnEnable()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;

        if (uiController != null)
            uiController.OnOniValueChanged += HandleOniValueChanged;

        if (_baseline == null)
            ResetToZero();
    }

    private void OnDisable()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;

        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniValueChanged;
    }

    private void HandleDateSelected(string year, string month)
    {
        ClearState();
        ResetToZero();
    }

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        ClearState();

        if (data == null || data.Count == 0)
        {
            ResetToZero();
            return;
        }

        _entries.AddRange(data);
        _baseline = FindClosest(uiController != null ? uiController.GetCurrentOni() : 0f);
        ApplyEntry(_baseline);
    }

    private void HandleOniValueChanged(float oniValue)
    {
        if (_baseline == null || _entries.Count == 0)
            return;

        ApplyEntry(FindClosest(oniValue));
    }

    private void ClearState()
    {
        _entries.Clear();
        _baseline = null;
    }

    private void ResetToZero()
    {
        if (temperatureRatioText != null)
            temperatureRatioText.text = "0.0°C";
        if (energyUsageRatioText != null)
            energyUsageRatioText.text = "0.00%";
        if (energySupplyRatioText != null)
            energySupplyRatioText.text = "0.00%";
        if (reserveRateRatioText != null)
        {
            reserveRateRatioText.text = "0.0%";
            reserveRateRatioText.color = Color.white;
        }
    }

    private void ApplyEntry(OniRangeData entry)
    {
        if (entry == null || _baseline == null)
            return;

        float temperatureDelta = entry.seoulTemperature - _baseline.seoulTemperature;
        float usageDelta = PercentDelta(entry.seoulTotalConsumption, _baseline.seoulTotalConsumption);
        float supplyDelta = PercentDelta(entry.supplyPower, _baseline.supplyPower);
        float reserveRate = Mathf.Max(0f, entry.reserveRate);

        if (temperatureRatioText != null)
            temperatureRatioText.text = $"{temperatureDelta:+0.0;-0.0;0.0}°C";
        if (energyUsageRatioText != null)
            energyUsageRatioText.text = $"{usageDelta:+0.00;-0.00;0.00}%";
        if (energySupplyRatioText != null)
            energySupplyRatioText.text = $"{supplyDelta:+0.00;-0.00;0.00}%";
        if (reserveRateRatioText != null)
        {
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

    private OniRangeData FindClosest(float oniValue)
    {
        OniRangeData closest = null;
        float minDistance = float.MaxValue;

        foreach (OniRangeData entry in _entries)
        {
            float distance = Mathf.Abs(entry.oni - oniValue);
            if (distance >= minDistance)
                continue;

            minDistance = distance;
            closest = entry;
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
        if (panel == null)
        {
            Debug.LogWarning(
                $"[{nameof(OniImpactPanel)}] 하이어라키에서 '{panelName}'을(를) 찾지 못했습니다.",
                this);
            return null;
        }

        TMP_Text text = panel.Find("Text_Ratio")?.GetComponent<TMP_Text>();
        if (text == null)
        {
            Debug.LogWarning(
                $"[{nameof(OniImpactPanel)}] '{panelName}/Text_Ratio' TMP_Text를 찾지 못했습니다.",
                this);
        }

        return text;
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
