using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 순환단전 시뮬레이션 시작/중단 버튼.
/// ONI 패널이 열릴 때만 표시되며, 심각 단계(예비율 5% 미만)에서만 활성(빨간색)된다.
/// </summary>
public class RollingBlackoutStartButton : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private UIController uiController;
    [SerializeField] private BlackoutSimulationController simulationController;
    [SerializeField] private ReserveRateStateController reserveRateState;
    [SerializeField] private Button button;
    [SerializeField] private Graphic buttonBackground;
    [SerializeField] private TMP_Text buttonLabel;

    private bool _oniPanelVisible;
    private bool _simOn;
    private int _currentLevel = -1;
    private ReserveRateSnapshot.UiPhase _uiPhase = ReserveRateSnapshot.UiPhase.Normal;
    private CanvasGroup _panelCanvasGroup;

    private void Awake()
    {
        SceneRefs.Resolve(ref uiController);
        SceneRefs.Resolve(ref simulationController);
        SceneRefs.Resolve(ref reserveRateState);

        ResolveReferences();

        if (button != null)
        {
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(HandleClick);
        }
    }

    private void OnEnable()
    {
        SceneRefs.Resolve(ref uiController);
        SceneRefs.Resolve(ref simulationController);
        SceneRefs.Resolve(ref reserveRateState);

        if (!SceneRefs.RequireAll(this,
                (uiController, nameof(uiController)),
                (simulationController, nameof(simulationController)),
                (reserveRateState, nameof(reserveRateState))))
            return;

        uiController.OnOniPanelVisibilityChanged += HandleOniPanelVisibilityChanged;
        HandleOniPanelVisibilityChanged(uiController.IsOniPanelVisible);

        simulationController.OnBlackoutSimulationToggled += HandleSimToggled;

        reserveRateState.OnStateChanged += HandleReserveRateStateChanged;
        HandleReserveRateStateChanged(reserveRateState.Current);
        _simOn = reserveRateState.Current.IsSimulating;
        RefreshVisual();
    }

    private void OnDisable()
    {
        if (uiController != null)
            uiController.OnOniPanelVisibilityChanged -= HandleOniPanelVisibilityChanged;

        if (simulationController != null)
            simulationController.OnBlackoutSimulationToggled -= HandleSimToggled;

        if (reserveRateState != null)
            reserveRateState.OnStateChanged -= HandleReserveRateStateChanged;
    }

    private void ResolveReferences()
    {
        SceneRefs.EnsureOn(ref button, gameObject);
        SceneRefs.EnsureOn(ref _panelCanvasGroup, gameObject);

        if (buttonBackground == null)
            buttonBackground = GetComponent<Graphic>();

        if (buttonLabel == null)
            buttonLabel = GetComponentInChildren<TMP_Text>(true);
    }

    private void HandleOniPanelVisibilityChanged(bool visible)
    {
        _oniPanelVisible = visible;
        RefreshVisual();
    }

    private void HandleReserveRateStateChanged(ReserveRateSnapshot snapshot)
    {
        _currentLevel = snapshot.Level;
        _uiPhase = snapshot.Phase;
        RefreshVisual();
    }

    private void HandleSimToggled(bool isOn)
    {
        _simOn = isOn;
        RefreshVisual();
    }

    private void HandleClick()
    {
        if (_uiPhase == ReserveRateSnapshot.UiPhase.SimCompletedHold)
            return;

        simulationController.RequestToggle(!_simOn);
    }

    private void SetPanelVisible(bool visible)
    {
        if (_panelCanvasGroup == null)
            return;

        _panelCanvasGroup.alpha = visible ? 1f : 0f;
        _panelCanvasGroup.interactable = visible;
        _panelCanvasGroup.blocksRaycasts = visible;
    }

    private void RefreshVisual()
    {
        SetPanelVisible(_oniPanelVisible);

        if (!_oniPanelVisible)
            return;

        bool simCompleted = _uiPhase == ReserveRateSnapshot.UiPhase.SimCompletedHold;
        bool canStart = ReserveRateStagePalette.CanSimulate(_currentLevel);
        bool interactable = !simCompleted && (_simOn || canStart);

        if (button != null)
            button.interactable = interactable;

        if (buttonBackground != null)
        {
            buttonBackground.color = interactable || _simOn
                ? ReserveRateStagePalette.ButtonActive
                : ReserveRateStagePalette.ButtonDisabled;
        }

        if (buttonLabel == null)
            return;

        buttonLabel.color = Color.white;

        if (simCompleted)
            buttonLabel.text = "시뮬레이션 완료";
        else if (_simOn)
            buttonLabel.text = "순환 단전 Stop";
        else
            buttonLabel.text = "순환 단전 Start";
    }
}
