using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI·시뮬레이션 이벤트를 사용자 관점 로그 문장으로 변환해 SimulationLog에 기록한다.
/// API·내부 용어는 사용하지 않는다.
/// </summary>
[AddComponentMenu("UI/Log Event Bridge")]
public class LogEventBridge : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UIController uiController;
    [SerializeField] private MinimapManager minimapManager;
    [SerializeField] private DataManager dataManager;
    [SerializeField] private BlackoutSimulationController simulationController;

    [Header("구역 단전 로그")]
    [SerializeField] private float facilityLogIntervalSeconds = 0.5f;

    private bool _simulationRunning;
    private bool _simulationCompletedNaturally;

    private readonly Dictionary<DistrictType, List<BlackoutBuildingItem>> _blackoutItemsMap = new();
    private Coroutine _facilityLogCoroutine;

    private DistrictType _activeDistrict;
    private bool _facilityLogsDone;
    private bool _blackoutVisualDone;

    private void Awake()
    {
        SceneRefs.Resolve(ref uiController);
        SceneRefs.Resolve(ref minimapManager);
        SceneRefs.Resolve(ref dataManager);
        SceneRefs.Resolve(ref simulationController);
    }

    private void OnEnable()
    {
        if (uiController != null)
        {
            uiController.OnDateSelected += HandleDateSelected;
            uiController.OnOniSliderReleased += HandleOniSliderReleased;
        }

        if (minimapManager != null)
            minimapManager.OnDistrictSelected += HandleDistrictSelected;

        if (dataManager != null)
            dataManager.OnBlackoutItemsParsed += HandleBlackoutItemsParsed;

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled += HandleSimulationToggled;
            simulationController.OnDistrictBlackedOut += HandleDistrictBlackedOut;
            simulationController.OnDistrictBlackoutPhase += HandleDistrictBlackoutPhase;
            simulationController.OnSimulationCompleted += HandleSimulationCompleted;
        }
    }

    private void OnDisable()
    {
        if (uiController != null)
        {
            uiController.OnDateSelected -= HandleDateSelected;
            uiController.OnOniSliderReleased -= HandleOniSliderReleased;
        }

        if (minimapManager != null)
            minimapManager.OnDistrictSelected -= HandleDistrictSelected;

        if (dataManager != null)
            dataManager.OnBlackoutItemsParsed -= HandleBlackoutItemsParsed;

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled -= HandleSimulationToggled;
            simulationController.OnDistrictBlackedOut -= HandleDistrictBlackedOut;
            simulationController.OnDistrictBlackoutPhase -= HandleDistrictBlackoutPhase;
            simulationController.OnSimulationCompleted -= HandleSimulationCompleted;
        }
    }

    private void HandleBlackoutItemsParsed(Dictionary<DistrictType, List<BlackoutBuildingItem>> itemsMap)
    {
        _blackoutItemsMap.Clear();
        if (itemsMap == null) return;

        foreach (var kvp in itemsMap)
            _blackoutItemsMap[kvp.Key] = kvp.Value;
    }

    private void HandleDistrictBlackedOut(DistrictType districtType, double consumptionMwh)
    {
        _activeDistrict = districtType;
        _facilityLogsDone = false;
        _blackoutVisualDone = false;

        if (_facilityLogCoroutine != null)
            StopCoroutine(_facilityLogCoroutine);

        string guName = DataConverter.GetDistrictName(districtType);
        SimulationLog.Write($"{guName} 순환 단전 진행 중", LogLineStyle.Emphasis);
        _facilityLogCoroutine = StartCoroutine(LogFacilityBlackout(districtType));
    }

    private IEnumerator LogFacilityBlackout(DistrictType districtType)
    {
        if (_blackoutItemsMap.TryGetValue(districtType, out var items) && items.Count > 0)
        {
            foreach (var item in items)
            {
                yield return new WaitForSeconds(facilityLogIntervalSeconds);

                string label = string.IsNullOrEmpty(item.buildingType)
                    ? "기타 시설"
                    : item.buildingType;
                SimulationLog.Write($"{label} 전력 차단", LogLineStyle.Muted, indent: 1);
            }
        }
        else
        {
            SimulationLog.Write("차단 대상 시설 정보 없음", LogLineStyle.Muted, indent: 1);
        }

        _facilityLogsDone = true;
        _facilityLogCoroutine = null;
        TryWriteBlackoutComplete();
    }

    private void HandleDistrictBlackoutPhase(DistrictType districtType, DistrictBlackoutPhase phase)
    {
        if (districtType != _activeDistrict)
            return;

        string guName = DataConverter.GetDistrictName(districtType);

        switch (phase)
        {
            case DistrictBlackoutPhase.BlackoutComplete:
                _blackoutVisualDone = true;
                TryWriteBlackoutComplete();
                break;

            case DistrictBlackoutPhase.RestoreStarted:
                SimulationLog.Write($"{guName} 전력 복구 중", LogLineStyle.Emphasis);
                break;

            case DistrictBlackoutPhase.RestoreComplete:
                SimulationLog.Write($"{guName} 구역 복전 완료", LogLineStyle.DistrictComplete);
                break;
        }
    }

    private void TryWriteBlackoutComplete()
    {
        if (!_facilityLogsDone || !_blackoutVisualDone)
            return;

        string guName = DataConverter.GetDistrictName(_activeDistrict);
        SimulationLog.Write($"{guName} 순환 단전 완료", LogLineStyle.DistrictComplete);
    }

    private void HandleDateSelected(string year, string month)
    {
        if (int.TryParse(month, out int m))
            SimulationLog.Write($"시뮬레이션 시점을 {year}년 {m}월로 설정했습니다.", LogLineStyle.Emphasis);
        else
            SimulationLog.Write($"시뮬레이션 시점을 {year}년 {month}월로 설정했습니다.", LogLineStyle.Emphasis);
    }

    private void HandleOniSliderReleased(float oni)
    {
        string phase = OniPhaseLabel(oni);
        SimulationLog.Write($"ONI {oni:F1} ({phase})으로 조정했습니다.");
    }

    private void HandleDistrictSelected(DistrictType districtType)
    {
        if (_simulationRunning)
            return;

        string guName = DataConverter.GetDistrictName(districtType);
        SimulationLog.Write($"지도에서 {guName}을(를) 선택했습니다.");
    }

    private void HandleSimulationToggled(bool isOn)
    {
        if (isOn)
        {
            _simulationCompletedNaturally = false;
            _simulationRunning = true;
            SimulationLog.Write("순환 단전 시뮬레이션을 시작합니다.", LogLineStyle.Emphasis);
            return;
        }

        _simulationRunning = false;

        if (_facilityLogCoroutine != null)
        {
            StopCoroutine(_facilityLogCoroutine);
            _facilityLogCoroutine = null;
        }

        if (!_simulationCompletedNaturally)
            SimulationLog.Write("순환 단전 시뮬레이션을 중단했습니다.");
    }

    private void HandleSimulationCompleted()
    {
        _simulationCompletedNaturally = true;
        _simulationRunning = false;
        SimulationLog.Write("순환 단전 시뮬레이션이 완료되었습니다.", LogLineStyle.Emphasis);
    }

    private static string OniPhaseLabel(float oni)
    {
        if (oni <= -0.5f) return "라니냐";
        if (oni >= 0.5f) return "엘니뇨";
        return "중립";
    }
}
