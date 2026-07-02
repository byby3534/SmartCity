using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 순환단전 시뮬레이션의 단전 상태를 로그로 기록한다.
/// 구가 단전되면 0.5초 간격으로 건물유형을 순서대로 출력하고,
/// 모두 출력한 뒤 BlackoutSimulationController에 완료를 알려 다음 구로 넘어간다.
/// </summary>
public class BlackoutLogger : MonoBehaviour
{
    [SerializeField] private DataManager dataManager;
    [SerializeField] private BlackoutSimulationController simulationController;

    [Header("로그 설정")]
    [SerializeField] private float logIntervalSeconds = 0.5f;

    private Dictionary<DistrictType, List<BlackoutBuildingItem>> _itemsMap = new();
    private Dictionary<DistrictType, double> _consumptionMap = new();

    private Coroutine _logCoroutine;

    private void Awake()
    {
        SceneRefs.Resolve(ref dataManager);
        SceneRefs.Resolve(ref simulationController);
    }

    private void OnEnable()
    {
        if (!SceneRefs.RequireAll(this,
                (dataManager, nameof(dataManager)),
                (simulationController, nameof(simulationController))))
            return;

        dataManager.OnBlackoutSimulationParsed += HandleSimulationParsed;
        dataManager.OnBlackoutItemsParsed += HandleItemsParsed;

        simulationController.OnBlackoutSimulationToggled += HandleToggled;
        simulationController.OnDistrictBlackedOut += HandleDistrictBlackedOut;
        simulationController.OnSimulationCompleted += HandleSimulationCompleted;
    }

    private void OnDisable()
    {
        if (dataManager != null)
        {
            dataManager.OnBlackoutSimulationParsed -= HandleSimulationParsed;
            dataManager.OnBlackoutItemsParsed -= HandleItemsParsed;
        }

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled -= HandleToggled;
            simulationController.OnDistrictBlackedOut -= HandleDistrictBlackedOut;
            simulationController.OnSimulationCompleted -= HandleSimulationCompleted;
        }
    }

    private void HandleSimulationParsed(List<DistrictType> districtsType, Dictionary<DistrictType, double> consumption)
    {
        _consumptionMap = consumption ?? new Dictionary<DistrictType, double>();
    }

    private void HandleItemsParsed(Dictionary<DistrictType, List<BlackoutBuildingItem>> itemsMap)
    {
        _itemsMap = itemsMap ?? new Dictionary<DistrictType, List<BlackoutBuildingItem>>();
    }

    private void HandleToggled(bool isOn)
    {
        if (!isOn && _logCoroutine != null)
        {
            StopCoroutine(_logCoroutine);
            _logCoroutine = null;
        }
    }

    private void HandleDistrictBlackedOut(DistrictType districtType, double consumptionMwh)
    {
        if (_logCoroutine != null)
            StopCoroutine(_logCoroutine);

        _logCoroutine = StartCoroutine(LogDistrictBlackout(districtType, consumptionMwh));
    }

    private IEnumerator LogDistrictBlackout(DistrictType districtType, double consumptionMwh)
    {
        string guName = DataConverter.GetDistrictName(districtType);
        SimulationLog.Write($"{guName} 순환 단전 진행 중", LogLineStyle.Emphasis);

        if (_itemsMap.TryGetValue(districtType, out var items) && items.Count > 0)
        {
            foreach (var item in items)
            {
                yield return new WaitForSeconds(logIntervalSeconds);

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

        SimulationLog.Write($"{guName} 순환 단전 완료", LogLineStyle.DistrictComplete);
        _logCoroutine = null;
    }

    private void HandleSimulationCompleted()
    {
        // 완료 메시지는 LogEventBridge에서 사용자 액션 흐름으로 기록
    }
}
