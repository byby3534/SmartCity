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
    [SerializeField] private BlackoutSimulationController simulationController;

    private bool _simulationRunning;
    private bool _simulationCompletedNaturally;

    private void Awake()
    {
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();
        if (minimapManager == null)
            minimapManager = FindFirstObjectByType<MinimapManager>();
        if (simulationController == null)
            simulationController = FindFirstObjectByType<BlackoutSimulationController>();
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

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled += HandleSimulationToggled;
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

        if (simulationController != null)
        {
            simulationController.OnBlackoutSimulationToggled -= HandleSimulationToggled;
            simulationController.OnSimulationCompleted -= HandleSimulationCompleted;
        }
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
