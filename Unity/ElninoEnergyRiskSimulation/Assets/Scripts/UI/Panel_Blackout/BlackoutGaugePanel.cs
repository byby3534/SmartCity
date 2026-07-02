using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 게이지 패널 시각화.
/// - 니들: 예비율 → 각도
/// - 강조 색: 니들 정지 시 예비율 단계, 애니메이션 중 니들 각도
/// - 패널 텍스트(PowerStatusPanelUI): 동일 규칙 — 애니메이션 중 각도, 정지 시 예비율
/// </summary>
public class BlackoutGaugePanel : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] private DonutMeshRenderer[] segments = new DonutMeshRenderer[5];
    [SerializeField] private RectTransform needle;
    [SerializeField] private TMP_Text reserveRateLabel;
    [SerializeField] private ReserveRateStateController stateController;

    [Header("동작")]
    [SerializeField] private float needleSmoothTime = 0.45f;

    private const float InnerRadius = 40f;
    private const float OuterRadiusNormal = 90f;
    private const float OuterRadiusActive = 100f;

    private float _targetAngle;
    private float _needleAngularVelocity;
    private int _settledLevel = -1;
    private float _settledReserveRate;

    private Coroutine _needleCoroutine;
    private PowerStatusPanelUI _powerStatusPanel;

    private void Awake()
    {
        SceneRefs.Resolve(ref stateController);
        EnsurePowerStatusPanel();
    }

    private void OnEnable()
    {
        SceneRefs.Resolve(ref stateController);

        if (!SceneRefs.Require(this, stateController, nameof(stateController)))
            return;

        stateController.OnStateChanged += HandleStateChanged;
        HandleStateChanged(stateController.Current);
    }

    private void OnDisable()
    {
        if (stateController != null)
            stateController.OnStateChanged -= HandleStateChanged;
    }

    private void EnsurePowerStatusPanel()
    {
        Transform panel = transform.parent;
        while (panel != null && panel.name != "Panel_PowerStatus")
            panel = panel.parent;

        if (panel == null)
            return;

        _powerStatusPanel = panel.GetComponent<PowerStatusPanelUI>();
        if (_powerStatusPanel == null)
            _powerStatusPanel = panel.gameObject.AddComponent<PowerStatusPanelUI>();
    }

    private void HandleStateChanged(ReserveRateSnapshot snapshot)
    {
        _settledReserveRate = snapshot.ReserveRate;
        _settledLevel = snapshot.Level;
        SetReserveRateLabel(snapshot.ReserveRate);

        if (snapshot.AnimateNeedle)
            AnimateNeedleTo(snapshot.NeedleAngle);
        else
            SetNeedleInstant(snapshot.NeedleAngle, snapshot.Level);
    }

    private void SetNeedleInstant(float angle, int level)
    {
        StopNeedleAnimation();
        _targetAngle = angle;
        SetNeedleAngle(angle);
        ApplySettledVisuals(level);
    }

    private void AnimateNeedleTo(float targetAngle)
    {
        _targetAngle = targetAngle;

        if (_needleCoroutine != null)
            StopCoroutine(_needleCoroutine);
        _needleCoroutine = StartCoroutine(AnimateNeedleCoroutine(targetAngle));
    }

    private IEnumerator AnimateNeedleCoroutine(float target)
    {
        while (true)
        {
            float cur = NormalizeAngle(GetNeedleAngle());
            float next = Mathf.SmoothDamp(
                cur, target, ref _needleAngularVelocity, needleSmoothTime, Mathf.Infinity, Time.deltaTime);

            SetNeedleAngle(next);
            ApplyAnimatedVisuals(next);

            if (Mathf.Abs(NormalizeAngle(next - target)) < 0.05f)
                break;

            yield return null;
        }

        SetNeedleAngle(target);
        _needleCoroutine = null;
        ApplySettledVisuals(_settledLevel);
    }

    private void ApplyAnimatedVisuals(float angle)
    {
        int level = ReserveRateGaugeMath.AngleToLevel(angle);
        UpdateSegments(level);
        _powerStatusPanel?.ApplyLevel(level);
    }

    private void ApplySettledVisuals(int level)
    {
        UpdateSegments(level);
        _powerStatusPanel?.ApplyLevel(level);
    }

    private void UpdateSegments(int activeLevel)
    {
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i] == null) continue;
            bool active = i == activeLevel;
            segments[i].ApplyDisplayColor(active ? ReserveRateStagePalette.GetSegmentColor(i) : segments[i].BaseColor);
            segments[i].OuterRadius = active ? OuterRadiusActive : OuterRadiusNormal;
            segments[i].InnerRadius = InnerRadius;
        }
    }

    private void StopNeedleAnimation()
    {
        if (_needleCoroutine != null)
        {
            StopCoroutine(_needleCoroutine);
            _needleCoroutine = null;
        }
        _needleAngularVelocity = 0f;
    }

    private void SetNeedleAngle(float angle)
    {
        if (needle != null)
            needle.localEulerAngles = new Vector3(0f, 0f, angle);
    }

    private float GetNeedleAngle() =>
        needle != null ? needle.localEulerAngles.z : _targetAngle;

    private static float NormalizeAngle(float a)
    {
        if (a > 180f) a -= 360f;
        return a;
    }

    private void SetReserveRateLabel(float rate)
    {
        if (reserveRateLabel != null)
            reserveRateLabel.text = $"공급 예비율  {rate:F1}%";
    }
}
