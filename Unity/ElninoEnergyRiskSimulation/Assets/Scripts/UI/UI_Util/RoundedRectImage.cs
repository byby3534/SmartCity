using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 슬라이더로 네 모서리 반경을 한 번에 조절하는 UI Image.
/// UniversalBlurUI 등 커스텀 Material과 함께 사용 가능.
/// </summary>
[AddComponentMenu("UI/Rounded Rect Image")]
public class RoundedRectImage : Image
{
    [Header("Rounded Corners")]
    [SerializeField, Range(0f, 64f)]
    private float cornerRadius = 14f;

    [SerializeField, Range(1, 24)]
    private int cornerSegments = 8;

    public float CornerRadius
    {
        get => cornerRadius;
        set
        {
            cornerRadius = Mathf.Clamp(value, 0f, 64f);
            SetVerticesDirty();
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        cornerSegments = Mathf.Clamp(cornerSegments, 1, 24);
        cornerRadius = Mathf.Clamp(cornerRadius, 0f, 64f);
        SetVerticesDirty();
    }
#endif

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        if (!isActiveAndEnabled)
            return;

        Rect rect = GetPixelAdjustedRect();
        if (rect.width <= 0f || rect.height <= 0f)
        {
            vh.Clear();
            return;
        }

        float radius = Mathf.Min(cornerRadius, rect.width * 0.5f, rect.height * 0.5f);
        if (radius <= 0.001f)
        {
            base.OnPopulateMesh(vh);
            return;
        }

        vh.Clear();

        var outline = new List<Vector2>(cornerSegments * 4 + 4);
        AppendCornerArc(outline, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, 180f, 270f);
        AppendCornerArc(outline, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 270f, 360f);
        AppendCornerArc(outline, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 0f, 90f);
        AppendCornerArc(outline, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 90f, 180f);

        FanTriangulate(vh, rect, outline, color);
    }

    private void AppendCornerArc(List<Vector2> points, Vector2 center, float radius, float startAngle, float endAngle)
    {
        int segments = Mathf.Max(1, cornerSegments);
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float angle = Mathf.Lerp(startAngle, endAngle, t) * Mathf.Deg2Rad;
            points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private static void FanTriangulate(VertexHelper vh, Rect rect, List<Vector2> outline, Color32 color32)
    {
        if (outline.Count < 3)
            return;

        Vector2 center = rect.center;
        int centerIndex = vh.currentVertCount;
        vh.AddVert(center, color32, GetUv(rect, center));

        for (int i = 0; i < outline.Count; i++)
            vh.AddVert(outline[i], color32, GetUv(rect, outline[i]));

        for (int i = 0; i < outline.Count; i++)
        {
            int next = (i + 1) % outline.Count;
            vh.AddTriangle(centerIndex, centerIndex + 1 + i, centerIndex + 1 + next);
        }
    }

    private static Vector2 GetUv(Rect rect, Vector2 position)
    {
        return new Vector2(
            Mathf.InverseLerp(rect.xMin, rect.xMax, position.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, position.y));
    }
}
