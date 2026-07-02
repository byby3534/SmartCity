using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 예비율 단일 상태 소스. 데이터·시뮬 이벤트를 한곳에서 처리하고 스냅샷을 발행한다.
/// HUD는 항상 ReserveRate 기준. 게이지 니들·강조·패널 텍스트는 구독 측에서 애니메이션 규칙을 적용한다.
/// </summary>
public class ReserveRateStateController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private DataManager dataManager;
    [SerializeField] private UIController uiController;
    [SerializeField] private BlackoutSimulationController simulationController;

    [Header("시뮬레이션 회복")]
    [SerializeField] private float simulationFullRecoveryRatio = 0.30f;

    [Header("시뮬 완료 UI")]
    [SerializeField] private float simCompletedHoldSeconds = 2f;
    [SerializeField] private float simCompletedTailSeconds = 1f;

    public event Action<ReserveRateSnapshot> OnStateChanged;

    public ReserveRateSnapshot Current { get; private set; }

    private float _baselineReserveRate = ReserveRateStagePalette.DefaultReserveRate;
    private float _displayReserveRate = ReserveRateStagePalette.DefaultReserveRate;
    private float _seoulTotal;
    private float _simStartReserveRate;
    private float _recoveredConsumption;
    private bool _hasReceivedData;
    private bool _simOn;
    private bool _naturalCompleteInProgress;

    private readonly List<OniRangeData> _oniRangeEntries = new();
    private Coroutine _completeCoroutine;

    private void Awake()
    {
        SceneRefs.Resolve(ref dataManager);
        SceneRefs.Resolve(ref uiController);
        SceneRefs.Resolve(ref simulationController);
    }

    private void OnEnable()
    {
        if (dataManager != null)
        {
            dataManager.OnPowerDataUpdated += HandlePowerDataUpdated;
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;
            dataManager.OnCurrentPowerUpdated += HandleCurrentPowerUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged += HandleOniValueChanged;

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled += HandleSimToggled;
            simulationController.OnDistrictBlackedOut += HandleDistrictBlackedOut;
            simulationController.OnSimulationCompleted += HandleSimCompleted;
        }

        Publish(animateNeedle: false);
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OnPowerDataUpdated -= HandlePowerDataUpdated;
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
            dataManager.OnCurrentPowerUpdated -= HandleCurrentPowerUpdated;
        }

        if (uiController != null)
            uiController.OnOniValueChanged -= HandleOniValueChanged;

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled -= HandleSimToggled;
            simulationController.OnDistrictBlackedOut -= HandleDistrictBlackedOut;
            simulationController.OnSimulationCompleted -= HandleSimCompleted;
        }

        if (_completeCoroutine != null)
        {
            StopCoroutine(_completeCoroutine);
            _completeCoroutine = null;
        }
    }

    private void HandlePowerDataUpdated(PowerGridData data)
    {
        if (data == null || _simOn)
            return;

        _hasReceivedData = true;
        _baselineReserveRate = data.reserveRate;
        _seoulTotal = data.seoulTotalConsumption;
        _displayReserveRate = _baselineReserveRate;
        Publish(animateNeedle: false);
    }

    private void HandleCurrentPowerUpdated(JObject power)
    {
        if (_hasReceivedData || _simOn || power == null)
            return;

        if (power["suppReserveRate"] == null)
            return;

        if (!float.TryParse(power["suppReserveRate"].ToString(), out float reserveRate))
            return;

        _hasReceivedData = true;
        _baselineReserveRate = reserveRate;
        _displayReserveRate = reserveRate;
        _seoulTotal = 0f;
        Publish(animateNeedle: false);
    }

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        _oniRangeEntries.Clear();

        if (data == null || data.Count == 0)
        {
            if (!_hasReceivedData)
            {
                _baselineReserveRate = ReserveRateStagePalette.DefaultReserveRate;
                _displayReserveRate = _baselineReserveRate;
                Publish(animateNeedle: false);
            }
            return;
        }

        _oniRangeEntries.AddRange(data);

        if (_simOn)
            return;

        float oni = uiController != null ? uiController.GetCurrentOni() : 0f;
        ApplyOniReserveRate(oni, animateNeedle: !_hasReceivedData);
    }

    private void HandleOniValueChanged(float oniValue)
    {
        if (_simOn || _oniRangeEntries.Count == 0)
            return;

        ApplyOniReserveRate(oniValue, animateNeedle: true);
    }

    private void ApplyOniReserveRate(float oniValue, bool animateNeedle)
    {
        OniRangeData entry = GetClosestOniEntry(oniValue);
        if (entry == null)
            return;

        _hasReceivedData = true;
        _baselineReserveRate = entry.reserveRate;
        _displayReserveRate = _baselineReserveRate;
        _seoulTotal = entry.seoulTotalConsumption;
        Publish(animateNeedle);
    }

    private void HandleSimToggled(bool isOn)
    {
        _simOn = isOn;

        if (isOn)
        {
            _recoveredConsumption = 0f;
            _simStartReserveRate = _baselineReserveRate;
            _displayReserveRate = _baselineReserveRate;
            _naturalCompleteInProgress = false;
            Publish(animateNeedle: false);
            return;
        }

        if (_naturalCompleteInProgress)
            return;

        _displayReserveRate = _baselineReserveRate;
        Publish(animateNeedle: true);
    }

    private void HandleDistrictBlackedOut(DistrictType districtType, double consumption)
    {
        if (!_simOn || _seoulTotal <= 0f)
            return;

        _recoveredConsumption += (float)consumption;
        float recoveryRatio = Mathf.Clamp01(
            _recoveredConsumption / (_seoulTotal * simulationFullRecoveryRatio));

        _displayReserveRate = Mathf.Lerp(
            _simStartReserveRate, ReserveRateStagePalette.Thresholds[0], recoveryRatio);
        Publish(animateNeedle: true);
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
        Publish(animateNeedle: false, phase: ReserveRateSnapshot.UiPhase.SimCompletedHold);

        yield return new WaitForSeconds(simCompletedHoldSeconds);

        _displayReserveRate = _baselineReserveRate;
        Publish(animateNeedle: true, phase: ReserveRateSnapshot.UiPhase.SimCompletedHold);

        yield return new WaitForSeconds(simCompletedTailSeconds);

        _naturalCompleteInProgress = false;
        Publish(animateNeedle: false, phase: ReserveRateSnapshot.UiPhase.Normal);
        _completeCoroutine = null;
    }

    private void Publish(bool animateNeedle, ReserveRateSnapshot.UiPhase? phase = null)
    {
        float rate = Mathf.Max(0f, _displayReserveRate);
        int level = ReserveRateStagePalette.ToLevel(rate);

        if (level < 4 && _simOn && simulationController != null)
            simulationController.RequestToggle(false);

        var snapshot = new ReserveRateSnapshot
        {
            ReserveRate = rate,
            Level = level,
            NeedleAngle = ReserveRateGaugeMath.ToAngle(rate),
            AnimateNeedle = animateNeedle,
            IsSimulating = _simOn,
            Phase = phase ?? (_naturalCompleteInProgress
                ? ReserveRateSnapshot.UiPhase.SimCompletedHold
                : ReserveRateSnapshot.UiPhase.Normal),
        };

        Current = snapshot;
        OnStateChanged?.Invoke(snapshot);
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
}
