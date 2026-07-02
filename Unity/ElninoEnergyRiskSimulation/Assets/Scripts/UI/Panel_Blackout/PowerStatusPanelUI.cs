using TMPro;
using UnityEngine;

/// <summary>
/// Panel_PowerStatus — 단계명·Dot·보조 문구를 예비율 단계에 맞게 갱신한다.
/// BlackoutGaugePanel이 예비율을 계산한 뒤 이 컴포넌트를 갱신한다.
/// </summary>
public class PowerStatusPanelUI : MonoBehaviour
{
    [SerializeField] private TMP_Text stageTitleText;
    [SerializeField] private TMP_Text stageDescriptionText;
    [SerializeField] private DonutMeshRenderer statusDot;

    private int _currentLevel = -1;

    private void Awake()
    {
        ResolveReferences();
        ApplyReserveRate(ReserveRateStagePalette.DefaultReserveRate, force: true);
    }

    public void ApplyReserveRate(float reserveRate, bool force = false)
    {
        ApplyLevel(ReserveRateStagePalette.ToLevel(reserveRate), force);
    }

    private void ResolveReferences()
    {
        Transform powerValue = transform.Find("Panel_PowerValue");

        if (stageTitleText == null && powerValue != null)
            stageTitleText = powerValue.Find("Text_PowerStatus")?.GetComponent<TMP_Text>();

        if (statusDot == null && powerValue != null)
            statusDot = powerValue.Find("Dot")?.GetComponent<DonutMeshRenderer>();

        if (stageDescriptionText == null)
            stageDescriptionText = transform.Find("Text_Contents")?.GetComponent<TMP_Text>();
    }

    private void ApplyLevel(int level, bool force = false)
    {
        if (!force && level == _currentLevel)
            return;

        _currentLevel = level;
        Color color = ReserveRateStagePalette.GetSegmentColor(level);

        if (stageTitleText != null)
        {
            stageTitleText.text = ReserveRateStagePalette.GetStageTitle(level);
            stageTitleText.color = color;
        }

        if (stageDescriptionText != null)
            stageDescriptionText.text = ReserveRateStagePalette.GetStageDescription(level);

        if (statusDot != null)
            statusDot.ApplyDisplayColor(color);
    }
}
