using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BlackoutGaugePanel : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private DonutMeshRenderer[] segments = new DonutMeshRenderer[5];
    [SerializeField] private RectTransform needle;
    [SerializeField] private TMP_Text reserveRateLabel;
    [SerializeField] private DataManager dataManager;
    [SerializeField] private UIController uiController;
    [SerializeField] private BlackoutSimulationController simulationController;

    [Header("동작")]
    [SerializeField] private float needleSmoothTime = 0.45f;
    [SerializeField] private float simulationFullRecoveryRatio = 0.30f;

    private static readonly float[] ReserveRateThresholds = ReserveRateStagePalette.Thresholds;
    private static readonly float[] NeedleAngles = { 72f, 36f, 0f, -36f, -72f };

    private const float NormalRangeUpper = 20f;
    private const float SegmentHalfWidth = 18f;
    private const float InnerRadius = 40f;
    private const float OuterRadiusNormal = 90f;
    private const float OuterRadiusActive = 100f;

    private int _currentLevel = -1;
    private float _targetAngle;
    private float _lastReserveRate;
    private float _simStartNeedleAngle;
    private float _needleAngularVelocity;

    private bool _simOn;
    private bool _simCompleted;
    private bool _naturalCompleteInProgress;
    private float _seoulTotal;
    private float _recoveredConsumption;
    private bool _hasReceivedData;

    private readonly List<OniRangeData> _oniRangeEntries = new();
    private Coroutine _needleCoroutine;
    private Coroutine _completeCoroutine;
    private PowerStatusPanelUI _powerStatusPanel;

    private void Awake()
    {
        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();
        if (simulationController == null)
            simulationController = FindFirstObjectByType<BlackoutSimulationController>();
    }

    private void Start()
    {
        if (dataManager != null)
        {
            dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged += HandleOniSliderChanged;

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled += HandleSimToggled;
            simulationController.OnDistrictBlackedOut += HandleDistrictBlackedOut;
            simulationController.OnSimulationCompleted += HandleSimCompleted;
        }

        EnsurePowerStatusPanel();
        ApplyDefaultReserveRate();
    }

    private void EnsurePowerStatusPanel()
    {
        Transform panel = transform.parent;
        while (panel != null && panel.name != "Panel_PowerStatus")
            panel = panel.parent;

        if (panel == null)
            return;

        _powerStatusPanel = panel.GetComponent<PowerStatusPanelUI>();
        if (_powerStatusPanel == null)
            _powerStatusPanel = panel.gameObject.AddComponent<PowerStatusPanelUI>();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying || _simOn)
            return;

        if (_hasReceivedData)
            ApplyReserveRate(_lastReserveRate, instant: true);
        else
            ApplyDefaultReserveRate();
    }

    private void OnDestroy()
    {
        if (dataManager != null)
        {
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
        }
        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniSliderChanged;

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled -= HandleSimToggled;
            simulationController.OnDistrictBlackedOut -= HandleDistrictBlackedOut;
            simulationController.OnSimulationCompleted -= HandleSimCompleted;
        }
    }

    private void HandlePowerDataUpdated(PowerGridData data)
    {
        if (data == null || _simOn)
            return;

        _hasReceivedData = true;
        _lastReserveRate = data.reserveRate;
        _seoulTotal = data.seoulTotalConsumption;
        ApplyReserveRate(data.reserveRate, instant: true);
    }

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        if (data == null || data.Count == 0)
        {
            if (!_hasReceivedData)
                ApplyDefaultReserveRate();
            return;
        }

        _oniRangeEntries.Clear();
        _oniRangeEntries.AddRange(data);

        float oni = uiController != null ? uiController.GetCurrentOni() : 0f;
        ApplyOniValue(oni, instant: !_hasReceivedData);
    }

    private void HandleOniSliderChanged(float oniValue)
    {
        if (_simOn) return;
        ApplyOniValue(oniValue, instant: false);
    }

    private void ApplyOniValue(float oniValue, bool instant)
    {
        if (_oniRangeEntries.Count == 0) return;

        OniRangeData entry = GetClosestOniEntry(oniValue);
        if (entry == null) return;

        _hasReceivedData = true;
        _lastReserveRate = entry.reserveRate;
        _seoulTotal = entry.seoulTotalConsumption;
        ApplyReserveRate(entry.reserveRate, instant);
    }

    private void ApplyDefaultReserveRate()
    {
        if (_hasReceivedData || _simOn)
            return;

        _lastReserveRate = ReserveRateStagePalette.DefaultReserveRate;
        _seoulTotal = 0f;
        ApplyReserveRate(ReserveRateStagePalette.DefaultReserveRate, instant: true);
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

    private void ApplyReserveRate(float reserveRate, bool instant)
    {
        reserveRate = Mathf.Max(0f, reserveRate);
        int level = ReserveRateStagePalette.ToLevel(reserveRate);
        float angle = ReserveRateToAngle(reserveRate);

        bool needleUnchanged = !instant && !_simOn && level == _currentLevel
            && Mathf.Abs(angle - _targetAngle) < 0.05f;

        _currentLevel = level;
        _targetAngle = angle;

        if (_currentLevel < 4 && _simOn && simulationController != null)
            simulationController.RequestToggle(false);

        if (!_simOn)
        {
            SetReserveRateLabel(reserveRate);
            _powerStatusPanel?.ApplyReserveRate(reserveRate);

            if (needleUnchanged)
                UpdateSegmentsForNeedle(angle, _currentLevel);
            else
                MoveNeedleTo(angle, instant);
        }
    }

    private void HandleSimToggled(bool isOn)
    {
        _simOn = isOn;

        if (isOn)
        {
            _recoveredConsumption = 0f;
            _simCompleted = false;
            _naturalCompleteInProgress = false;
            _simStartNeedleAngle = ReserveRateToAngle(_lastReserveRate);
            SetReserveRateLabel(_lastReserveRate);
        }
        else if (!_naturalCompleteInProgress && !_simCompleted)
        {
            SetReserveRateLabel(_lastReserveRate);
            RestoreNeedleFromLiveData(instant: false);
        }
    }

    private void HandleDistrictBlackedOut(DistrictType districtType, double consumption)
    {
        if (!_simOn || _seoulTotal <= 0f) return;

        _recoveredConsumption += (float)consumption;
        float recoveryRatio = Mathf.Clamp01(
            _recoveredConsumption / (_seoulTotal * simulationFullRecoveryRatio));

        float targetAngle = Mathf.Lerp(_simStartNeedleAngle, SegmentStart(0), recoveryRatio);
        MoveNeedleTo(targetAngle, instant: false);
    }

    private void HandleSimCompleted()
    {
        _simOn = false;
        _naturalCompleteInProgress = true;

        if (_completeCoroutine != null)
            StopCoroutine(_completeCoroutine);
        _completeCoroutine = StartCoroutine(CompleteSequence());
    }

    private IEnumerator CompleteSequence()
    {
        _simCompleted = true;

        yield return new WaitForSeconds(2f);

        RestoreNeedleFromLiveData(instant: false);

        yield return new WaitForSeconds(1f);

        _simCompleted = false;
        _naturalCompleteInProgress = false;
        _simOn = false;
        SetReserveRateLabel(_lastReserveRate);

        _completeCoroutine = null;
    }

    private void RestoreNeedleFromLiveData(bool instant)
    {
        MoveNeedleTo(ReserveRateToAngle(_lastReserveRate), instant);
    }

    private void MoveNeedleTo(float angle, bool instant)
    {
        _targetAngle = angle;

        if (instant)
        {
            StopNeedleAnimation();
            SetNeedleAngle(angle);
            UpdateSegmentsForNeedle(angle, _currentLevel);
        }
        else
        {
            AnimateNeedleTo(angle);
        }
    }

    private void AnimateNeedleTo(float target)
    {
        if (_needleCoroutine != null)
            StopCoroutine(_needleCoroutine);
        _needleCoroutine = StartCoroutine(AnimateNeedleCoroutine(target));
    }

    private void StopNeedleAnimation()
    {
        if (_needleCoroutine != null)
        {
            StopCoroutine(_needleCoroutine);
            _needleCoroutine = null;
        }
        _needleAngularVelocity = 0f;
    }

    private IEnumerator AnimateNeedleCoroutine(float target)
    {
        while (true)
        {
            float cur = NormalizeAngle(GetNeedleAngle());
            float next = Mathf.SmoothDamp(
                cur, target, ref _needleAngularVelocity, needleSmoothTime, Mathf.Infinity, Time.deltaTime);

            SetNeedleAngle(next);
            UpdateSegmentsForNeedle(next);

            if (Mathf.Abs(NormalizeAngle(next - target)) < 0.05f)
            {
                SetNeedleAngle(target);
                UpdateSegmentsForNeedle(target, _currentLevel);
                _needleCoroutine = null;
                yield break;
            }

            yield return null;
        }
    }

    private void UpdateSegmentsForNeedle(float angle, int? forcedLevel = null)
    {
        int activeLevel = forcedLevel ?? AngleToLevel(angle);

        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            bool active = i == activeLevel;
            segments[i].ApplyDisplayColor(active ? ReserveRateStagePalette.GetSegmentColor(i) : segments[i].BaseColor);
            segments[i].OuterRadius = active ? OuterRadiusActive : OuterRadiusNormal;
            segments[i].InnerRadius = InnerRadius;
        }
    }

    private static float ReserveRateToAngle(float reserveRate)
    {
        float normalLower = ReserveRateThresholds[0];

        if (reserveRate >= NormalRangeUpper)
            return SegmentStart(0);

        if (reserveRate >= normalLower)
        {
            float t = Mathf.InverseLerp(normalLower, NormalRangeUpper, reserveRate);
            return Mathf.Lerp(SegmentEnd(0), SegmentStart(0), t);
        }

        for (int i = 0; i < 4; i++)
        {
            float upper = ReserveRateThresholds[i];
            float lower = ReserveRateThresholds[i + 1];
            if (reserveRate >= lower)
            {
                int seg = i + 1;
                float t = Mathf.InverseLerp(lower, upper, reserveRate);
                return Mathf.Lerp(SegmentEnd(seg), SegmentStart(seg), t);
            }
        }

        float tCritical = Mathf.InverseLerp(ReserveRateThresholds[4], ReserveRateThresholds[3], reserveRate);
        return Mathf.Lerp(SegmentEnd(4), SegmentStart(4), tCritical);
    }

    private static int AngleToLevel(float angle)
    {
        angle = NormalizeAngle(angle);
        if (angle >= SegmentEnd(0)) return 0;
        if (angle >= SegmentEnd(1)) return 1;
        if (angle >= SegmentEnd(2)) return 2;
        if (angle >= SegmentEnd(3)) return 3;
        return 4;
    }

    private static float SegmentStart(int level) => NeedleAngles[level] + SegmentHalfWidth;
    private static float SegmentEnd(int level) => NeedleAngles[level] - SegmentHalfWidth;

    private void SetNeedleAngle(float angle)
    {
        if (needle != null)
            needle.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private float GetNeedleAngle() =>
        needle != null ? needle.localEulerAngles.z : _targetAngle;

    private static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }

    private void SetReserveRateLabel(float rate)
    {
        if (reserveRateLabel != null)
            reserveRateLabel.text = $"공급 예비율  {rate:F1}%";
    }
}
