using System.Collections;
using System.Collections.Generic;
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
    [SerializeField] private DataManager dataManager;
    [SerializeField] private UIController uiController;
    [SerializeField] private BlackoutSimulationController simulationController;
    [SerializeField] private Button button;
    [SerializeField] private Graphic buttonBackground;
    [SerializeField] private TMP_Text buttonLabel;

    private readonly List<OniRangeData> _oniRangeEntries = new();

    private int _currentLevel = -1;
    private bool _oniPanelVisible;
    private bool _simOn;
    private bool _simCompleted;
    private bool _naturalCompleteInProgress;
    private Coroutine _completeCoroutine;

    private void Awake()
    {
        if (dataManager == null)
            dataManager = FindFirstObjectByType<DataManager>();
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();
        if (simulationController == null)
            simulationController = FindFirstObjectByType<BlackoutSimulationController>();

        ResolveReferences();

        if (button != null)
        {
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(HandleClick);
        }
    }

    private void Start()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated += HandleOniRangeDataUpdated;

        if (uiController != null)
        {
            uiController.OnOniValueChanged += HandleOniSliderChanged;
            uiController.OnOniPanelVisibilityChanged += HandleOniPanelVisibilityChanged;
            HandleOniPanelVisibilityChanged(uiController.IsOniPanelVisible);
        }

        simulationController.OnBlackoutSimulationToggled += HandleSimToggled;
        simulationController.OnSimulationCompleted += HandleSimCompleted;

        RefreshVisual();
    }

    private void OnDestroy()
    {
        if (dataManager != null)
            dataManager.OniRangeDataUpdated -= HandleOniRangeDataUpdated;
        if (uiController != null)
        {
            uiController.OnOniValueChanged -= HandleOniSliderChanged;
            uiController.OnOniPanelVisibilityChanged -= HandleOniPanelVisibilityChanged;
        }

        simulationController.OnBlackoutSimulationToggled -= HandleSimToggled;
        simulationController.OnSimulationCompleted -= HandleSimCompleted;
    }

    private void ResolveReferences()
    {
        if (button == null)
            button = GetComponent<Button>();

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

    private void HandleOniRangeDataUpdated(List<OniRangeData> data)
    {
        _oniRangeEntries.Clear();

        if (data == null || data.Count == 0)
        {
            _currentLevel = -1;
            RefreshVisual();
            return;
        }

        _oniRangeEntries.AddRange(data);

        float oni = uiController != null ? uiController.GetCurrentOni() : 0f;
        ApplyOniValue(oni);
    }

    private void HandleOniSliderChanged(float oniValue)
    {
        if (_simOn) return;
        ApplyOniValue(oniValue);
    }

    private void ApplyOniValue(float oniValue)
    {
        if (_oniRangeEntries.Count == 0) return;

        OniRangeData entry = GetClosestOniEntry(oniValue);
        if (entry == null) return;

        _currentLevel = ReserveRateStagePalette.ToLevel(entry.reserveRate);
        RefreshVisual();
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

    private void HandleClick()
    {
        if (simulationController == null || _simCompleted || _naturalCompleteInProgress) return;
        simulationController.RequestToggle(!_simOn);
    }

    private void HandleSimToggled(bool isOn)
    {
        _simOn = isOn;

        if (isOn)
        {
            _simCompleted = false;
            _naturalCompleteInProgress = false;
        }

        if (!_naturalCompleteInProgress)
            RefreshVisual();
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
        RefreshVisual();

        yield return new WaitForSeconds(2f);
        yield return new WaitForSeconds(1f);

        _simCompleted = false;
        _naturalCompleteInProgress = false;
        _simOn = false;
        RefreshVisual();

        _completeCoroutine = null;
    }

    private void RefreshVisual()
    {
        gameObject.SetActive(_oniPanelVisible);

        if (!_oniPanelVisible)
            return;

        bool canSimulate = ReserveRateStagePalette.CanSimulate(_currentLevel);
        bool interactable = canSimulate && !_simCompleted && !_naturalCompleteInProgress;

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

        if (_simCompleted)
            buttonLabel.text = "시뮬레이션 완료";
        else if (_simOn)
            buttonLabel.text = "순환 단전 Stop";
        else
            buttonLabel.text = "순환 단전 Start";
    }
}
