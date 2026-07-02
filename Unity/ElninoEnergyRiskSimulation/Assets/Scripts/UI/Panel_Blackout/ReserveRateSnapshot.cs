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

    public float ReserveRate { get; }
    public int Level { get; }
    public float NeedleAngle { get; }
    public bool AnimateNeedle { get; }
    public bool IsSimulating { get; }
    public UiPhase Phase { get; }

    public ReserveRateSnapshot(
        float reserveRate,
        int level,
        float needleAngle,
        bool animateNeedle,
        bool isSimulating,
        UiPhase phase)
    {
        ReserveRate = reserveRate;
        Level = level;
        NeedleAngle = needleAngle;
        AnimateNeedle = animateNeedle;
        IsSimulating = isSimulating;
        Phase = phase;
    }
}
