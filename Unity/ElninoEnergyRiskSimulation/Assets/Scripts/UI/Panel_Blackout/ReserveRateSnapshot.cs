/// <summary>
/// 예비율 UI가 공유하는 단일 상태 스냅샷.
/// </summary>
public readonly struct ReserveRateSnapshot
{
    public enum UiPhase
    {
        Normal,
        SimCompletedHold,
    }

    public float ReserveRate { get; init; }
    public int Level { get; init; }
    public float NeedleAngle { get; init; }
    public bool AnimateNeedle { get; init; }
    public bool IsSimulating { get; init; }
    public UiPhase Phase { get; init; }
}
