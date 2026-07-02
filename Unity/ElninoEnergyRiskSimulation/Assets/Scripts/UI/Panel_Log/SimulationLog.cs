using System;
using UnityEngine;

/// <summary>
/// 시뮬레이션 로그 버스. LogPanelUI가 구독하고, Bridge·BlackoutLogger 등이 발행한다.
/// </summary>
public static class SimulationLog
{
    public static event Action<SimulationLogEntry> OnEntry;

    public static void Write(string message, LogLineStyle style = LogLineStyle.Normal, int indent = 0)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        OnEntry?.Invoke(new SimulationLogEntry(message.Trim(), style, indent));
    }

    public static void Clear()
    {
        OnEntry?.Invoke(SimulationLogEntry.ClearSignal);
    }
}

public enum LogLineStyle
{
    Normal,
    Muted,
    Emphasis,
    DistrictComplete,
}

public readonly struct SimulationLogEntry
{
    public static readonly SimulationLogEntry ClearSignal = new(true);

    public readonly bool IsClear;
    public readonly string Message;
    public readonly LogLineStyle Style;
    public readonly int Indent;

    public SimulationLogEntry(string message, LogLineStyle style, int indent)
    {
        IsClear = false;
        Message = message;
        Style = style;
        Indent = Mathf.Max(0, indent);
    }

    private SimulationLogEntry(bool clear)
    {
        IsClear = clear;
        Message = null;
        Style = LogLineStyle.Normal;
        Indent = 0;
    }
}
