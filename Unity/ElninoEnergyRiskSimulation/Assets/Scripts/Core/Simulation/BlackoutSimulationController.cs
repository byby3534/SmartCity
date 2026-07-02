using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BlackoutSimulationController : MonoBehaviour
{
    [Header("데이터")]
    [SerializeField] private DataManager dataManager;

    public event Action<DistrictType>           OnBlackoutDistrictChanged;
    public event Action<bool>             OnBlackoutSimulationToggled;
    public event Action<DistrictType, double>   OnDistrictBlackedOut;
    public event Action                   OnSimulationCompleted;

    private List<DistrictType>               _orderedDistricts = new();
    private Dictionary<DistrictType, double> _guConsumption = new();
    private Coroutine                  _simulationCoroutine;
    private bool                       _isOn;
    private bool                       _waitingForFinish;

    private void Awake()
    {
        SceneRefs.Resolve(ref dataManager);
    }

    public void RequestToggle(bool isOn)
    {
        if (_isOn == isOn) return;
        _isOn = isOn;
        OnBlackoutSimulationToggled?.Invoke(isOn);

        if (isOn)
        {
            if (_orderedDistricts.Count == 0)
            {
                Debug.LogWarning("[BlackoutSimulationController] 순회할 구 데이터가 없습니다.");
                _isOn = false;
                OnBlackoutSimulationToggled?.Invoke(false);
                return;
            }

            _simulationCoroutine = StartCoroutine(RunSimulation());
        }
        else
        {
            if (_simulationCoroutine != null)
            {
                StopCoroutine(_simulationCoroutine);
                _simulationCoroutine = null;
            }
        }

        Debug.Log($"[BlackoutSimulationController] 시뮬레이션 {(isOn ? "시작" : "중단")}");
    }

    private void OnEnable()
    {
        if (!SceneRefs.Require(this, dataManager, nameof(dataManager)))
            return;

        dataManager.OnPowerDataUpdated          += HandlePowerDataUpdated;
        dataManager.OnBlackoutSimulationParsed  += HandleBlackoutSimulationParsed;
    }

    private void OnDisable()
    {
        if (dataManager == null) return;

        dataManager.OnPowerDataUpdated          -= HandlePowerDataUpdated;
        dataManager.OnBlackoutSimulationParsed  -= HandleBlackoutSimulationParsed;
    }

    // BlackoutLogger가 한 구의 로그를 모두 출력하면 호출
    public void NotifyDistrictFinished()
    {
        _waitingForFinish = false;
    }

    private void HandlePowerDataUpdated(PowerGridData data)
    {
        if (data.riskLevel < 4 && _isOn)
            RequestToggle(false);
    }

    private void HandleBlackoutSimulationParsed(List<DistrictType> orderedGuNames, Dictionary<DistrictType, double> guConsumption)
    {
        _orderedDistricts = orderedGuNames ?? new List<DistrictType>();
        _guConsumption = guConsumption ?? new Dictionary<DistrictType, double>();
    }

    private IEnumerator RunSimulation()
    {
        foreach (DistrictType districtType in _orderedDistricts)
        {
            OnBlackoutDistrictChanged?.Invoke(districtType);

            double consumption = _guConsumption.TryGetValue(districtType, out double v) ? v : 0.0;
            _waitingForFinish = true;
            OnDistrictBlackedOut?.Invoke(districtType, consumption);

            // BlackoutLogger가 NotifyDistrictFinished()를 호출할 때까지 대기
            yield return new WaitUntil(() => !_waitingForFinish);
        }

        Debug.Log("[BlackoutSimulationController] 시뮬레이션 완료");
        _simulationCoroutine = null;
        OnSimulationCompleted?.Invoke();
        RequestToggle(false);
    }
}
