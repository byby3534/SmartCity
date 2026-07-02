using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum DistrictBlackoutPhase
{
    BlackoutComplete,
    RestoreStarted,
    RestoreComplete,
}

public class BlackoutSimulationController : MonoBehaviour
{
    [Header("데이터")]
    [SerializeField] private DataManager dataManager;

    public event Action<DistrictType>           OnBlackoutDistrictChanged;
    public event Action<bool>                   OnBlackoutSimulationToggled;
    public event Action<DistrictType, double>   OnDistrictBlackedOut;
    public event Action<DistrictType, DistrictBlackoutPhase> OnDistrictBlackoutPhase;
    public event Action                         OnSimulationCompleted;
    public event Action<DistrictType, DistrictType> OnActiveDistrictsChanged; // current, next

    private List<DistrictType>               _orderedDistricts = new();
    private Dictionary<DistrictType, double> _guConsumption = new();
    private Coroutine                  _simulationCoroutine;
    private bool                       _isOn;
    private bool                       _waitingForFinish;

    public bool IsSimulating => _isOn;

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

        dataManager.OnBlackoutSimulationParsed += HandleBlackoutSimulationParsed;
    }

    private void OnDisable()
    {
        if (dataManager == null) return;

        dataManager.OnBlackoutSimulationParsed -= HandleBlackoutSimulationParsed;
    }

    public void NotifyDistrictBlackoutComplete(DistrictType district)
    {
        OnDistrictBlackoutPhase?.Invoke(district, DistrictBlackoutPhase.BlackoutComplete);
    }

    public void NotifyDistrictRestoreStarted(DistrictType district)
    {
        OnDistrictBlackoutPhase?.Invoke(district, DistrictBlackoutPhase.RestoreStarted);
    }

    public void NotifyDistrictRestoreComplete(DistrictType district)
    {
        OnDistrictBlackoutPhase?.Invoke(district, DistrictBlackoutPhase.RestoreComplete);
        _waitingForFinish = false;
    }

    private void HandleBlackoutSimulationParsed(List<DistrictType> orderedGuNames, Dictionary<DistrictType, double> guConsumption)
    {
        _orderedDistricts = orderedGuNames ?? new List<DistrictType>();
        _guConsumption = guConsumption ?? new Dictionary<DistrictType, double>();
    }

    private IEnumerator RunSimulation()
    {
        for (int i = 0; i < _orderedDistricts.Count; i++)
        {
            DistrictType current = _orderedDistricts[i];
            DistrictType next    = (i + 1 < _orderedDistricts.Count) ? _orderedDistricts[i + 1] : DistrictType.None;

            OnActiveDistrictsChanged?.Invoke(current, next);
            OnBlackoutDistrictChanged?.Invoke(current);

            double consumption = _guConsumption.TryGetValue(current, out double v) ? v : 0.0;
            _waitingForFinish = true;
            OnDistrictBlackedOut?.Invoke(current, consumption);

            yield return new WaitUntil(() => !_waitingForFinish);
        }

        Debug.Log("[BlackoutSimulationController] 시뮬레이션 완료");
        _simulationCoroutine = null;
        OnSimulationCompleted?.Invoke();
        RequestToggle(false);
    }
}
