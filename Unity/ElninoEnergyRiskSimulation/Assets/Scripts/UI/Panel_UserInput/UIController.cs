using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    [Header("Date Picker")]
    [SerializeField] private SimulationDatePicker datePicker;

    [Header("ONI Slider")]
    [SerializeField] private GameObject oniSliderPanel;
    [SerializeField] private Slider oniSlider;
    [SerializeField] private TMP_Text oniTypeText;  // 중립 / 라니냐 / 엘니뇨
    [SerializeField] private TMP_Text oniNumText;   // 0.0

    [Header("Simulation")]
    [SerializeField] private GameObject simulationStartButton;

    // 연월 드롭다운 변경 → /oni && /predict/oni_range 호출 트리거
    public event Action<string, string> OnDateSelected;

    // 슬라이더 드래그 중 실시간 ONI 값 변경 알림
    public event Action<float> OnOniValueChanged;

    // 슬라이더 버튼 뗄 때 → /predict 재호출 트리거 (ONI 값 전달)
    public event Action<float> OnOniSliderReleased;

    // ONI 패널 표시 여부 변경
    public event Action<bool> OnOniPanelVisibilityChanged;

    public bool IsOniPanelVisible { get; private set; }

    // SimulationDatePicker는 카드/팝업에서 고른 값만 알려주고(OnYearPicked/OnMonthPicked),
    // 연+월이 모두 채워졌는지 판단해 확정하는 것은 UIController가 담당한다.
    private int? _year;
    private int? _month;
    private bool _sliderWired;
    private bool _blackoutSimulationRunning;
    private BlackoutSimulationController _simulationController;

    private void Awake()
    {
        ResolveReferences();

        if (datePicker != null)
        {
            datePicker.OnYearPicked += HandleYearPicked;
            datePicker.OnMonthPicked += HandleMonthPicked;
        }

        EnsureSliderWired();
        EnsureSimulationStartButton();
    }

    private void ResolveReferences()
    {
        if (datePicker == null)
            datePicker = FindFirstObjectByType<SimulationDatePicker>();

        if (oniSliderPanel == null)
            oniSliderPanel = FindUiObject("Panel_ONI_Adjust");

        if (oniSlider == null)
        {
            GameObject sliderPanel = FindUiObject("Panel_ONI_Slider");
            if (sliderPanel != null)
                oniSlider = sliderPanel.GetComponentInChildren<Slider>(true);
        }

        if (oniNumText == null)
        {
            GameObject sliderPanel = FindUiObject("Panel_ONI_Slider");
            if (sliderPanel != null)
            {
                foreach (TMP_Text text in sliderPanel.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name.Contains("Num"))
                    {
                        oniNumText = text;
                        break;
                    }
                }
            }
        }

        if (oniTypeText == null)
        {
            GameObject sliderPanel = FindUiObject("Panel_ONI_Slider");
            if (sliderPanel != null)
            {
                foreach (TMP_Text text in sliderPanel.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name.Contains("Type") || text.name.Contains("Label"))
                    {
                        oniTypeText = text;
                        break;
                    }
                }
            }
        }
    }

    private void EnsureSliderWired()
    {
        if (_sliderWired || oniSlider == null)
            return;

        oniSlider.onValueChanged.AddListener(v =>
        {
            UpdateOniDisplay(v);
            OnOniValueChanged?.Invoke(v);
        });

        var trigger = oniSlider.gameObject.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = oniSlider.gameObject.AddComponent<EventTrigger>();

        for (int i = trigger.triggers.Count - 1; i >= 0; i--)
        {
            if (trigger.triggers[i].eventID == EventTriggerType.PointerUp)
                trigger.triggers.RemoveAt(i);
        }

        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        entry.callback.AddListener(_ => OnOniSliderReleased?.Invoke(oniSlider.value));
        trigger.triggers.Add(entry);
        _sliderWired = true;
    }

    private void EnsureSimulationStartButton()
    {
        if (simulationStartButton == null)
            simulationStartButton = FindUiObject("Btn_BlakcoutSimulation");

        if (simulationStartButton != null &&
            simulationStartButton.GetComponent<RollingBlackoutStartButton>() == null)
        {
            simulationStartButton.AddComponent<RollingBlackoutStartButton>();
        }
    }

    private static GameObject FindUiObject(string objectName)
    {
        foreach (Transform target in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (target.name == objectName)
                return target.gameObject;
        }

        return null;
    }

    private void OnEnable()
    {
        _simulationController = FindFirstObjectByType<BlackoutSimulationController>();
        if (_simulationController != null)
            _simulationController.OnBlackoutSimulationToggled += HandleBlackoutSimulationToggled;
    }

    private void OnDisable()
    {
        if (_simulationController != null)
            _simulationController.OnBlackoutSimulationToggled -= HandleBlackoutSimulationToggled;
    }

    private void OnDestroy()
    {
        if (datePicker != null)
        {
            datePicker.OnYearPicked -= HandleYearPicked;
            datePicker.OnMonthPicked -= HandleMonthPicked;
        }
    }

    private void Start()
    {
        // 초기 상태: 슬라이더 숨김, 드롭다운 선택 없음 (빈 placeholder 상태)
        SetSliderActive(false);

        // 자동 API 호출 없음 — 사용자가 직접 연월을 선택해야 호출됨
    }

    // DataManager에서 /oni 응답 후 호출 — 슬라이더 값 설정 및 활성화
    public void InitSlider(float oniValue, float min = -2.5f, float max = 2.5f)
    {
        ResolveReferences();
        EnsureSliderWired();

        if (oniSlider != null)
        {
            oniSlider.minValue = min;
            oniSlider.maxValue = max;
            oniSlider.value = oniValue;
            UpdateOniDisplay(oniValue);
        }

        SetSliderActive(true);
        datePicker?.SetStatusLoaded(true);
    }

    private void UpdateOniDisplay(float value)
    {
        if (oniNumText != null) oniNumText.text = value.ToString("F1");
        if (oniTypeText != null) oniTypeText.text = OniToType(value);
    }

    private static string OniToType(float v)
    {
        if (v <= -0.5f) return "라니냐";
        if (v >= 0.5f) return "엘니뇨";
        return "중립";
    }

    public string GetSelectedYear() => _year?.ToString();
    public string GetSelectedMonth() => _month?.ToString();
    public float GetCurrentOni() => oniSlider != null ? oniSlider.value : 0f;

    private void HandleYearPicked(int year)
    {
        _year = year;
        TryCommit();
    }

    private void HandleMonthPicked(int month)
    {
        _month = month;
        TryCommit();
    }

    private void TryCommit()
    {
        if (!_year.HasValue || !_month.HasValue) return;

        SetSliderActive(false);
        OnDateSelected?.Invoke(_year.Value.ToString(), _month.Value.ToString());
    }

    private void SetSliderActive(bool active)
    {
        IsOniPanelVisible = active;

        if (oniSliderPanel != null)
        {
            oniSliderPanel.SetActive(active);
            if (active)
                oniSliderPanel.transform.SetAsLastSibling();
        }
        else if (oniSlider != null)
            oniSlider.gameObject.SetActive(active);

        if (oniSlider != null)
            oniSlider.interactable = active && !_blackoutSimulationRunning;

        OnOniPanelVisibilityChanged?.Invoke(active);
    }

    private void HandleBlackoutSimulationToggled(bool isOn)
    {
        _blackoutSimulationRunning = isOn;

        if (oniSlider != null)
            oniSlider.interactable = IsOniPanelVisible && !isOn;
    }
}
