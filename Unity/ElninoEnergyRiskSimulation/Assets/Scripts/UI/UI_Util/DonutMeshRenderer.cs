using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 임의 각도 구간의 도넛 호를 그리는 Graphic 컴포넌트.
///
/// StartAngleDeg ~ EndAngleDeg 사이의 호를 채웁니다.
/// (180°=왼쪽, 0°=오른쪽, 반시계 방향)
///
/// Inspector 노출 파라미터:
///   Inner Radius  — 안쪽 반지름
///   Outer Radius  — 바깥 반지름
///   Segments      — 분할 수 (높을수록 부드러움)
///   Start Angle   — 시작 각도 (degree)
///   End Angle     — 끝 각도 (degree)
///   Border Width  — 테두리 두께 (0 = 없음)
///   Border Color  — 테두리 색
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class DonutMeshRenderer : Graphic
{
    [Header("도넛 형태")]
    [SerializeField] private float innerRadius = 40f;
    [SerializeField] private float outerRadius = 90f;
    [Range(4, 256)]
    [SerializeField] private int segments = 64;

    [Header("각도 (degree, 180=왼쪽 0=오른쪽)")]
    [SerializeField] private float startAngleDeg = 180f;
    [SerializeField] private float endAngleDeg = 0f;

    [Header("테두리")]
    [SerializeField] private float borderWidth = 0f;
    [SerializeField] private Color borderColor = Color.white;

    [Header("비활성(원복) 색 — 인스펙터에서 직접 지정, 강조 시에만 color가 덮어씀")]
    [SerializeField] private Color baseColor = Color.white;

    public Color BaseColor => baseColor;

    public void ApplyDisplayColor(Color displayColor)
    {
        color = displayColor;
        SetVerticesDirty();
    }

    // ── 프로퍼티 (코드에서 런타임 제어용) ────────────────────────────────

    public float InnerRadius
    {
        get => innerRadius;
        set { innerRadius = value; SetVerticesDirty(); }
    }

    public float OuterRadius
    {
        get => outerRadius;
        set { outerRadius = value; SetVerticesDirty(); }
    }

    public float StartAngleDeg
    {
        get => startAngleDeg;
        set { startAngleDeg = value; SetVerticesDirty(); }
    }

    public float EndAngleDeg
    {
        get => endAngleDeg;
        set { endAngleDeg = value; SetVerticesDirty(); }
    }

    // ── 메쉬 생성 ─────────────────────────────────────────────────────────

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        DrawArc(vh, innerRadius, outerRadius, startAngleDeg, endAngleDeg, color);

        if (borderWidth > 0f)
        {
            DrawArc(vh, outerRadius, outerRadius + borderWidth, startAngleDeg, endAngleDeg, borderColor);

            // innerRadius=0(채운 원)이면 내경 테두리를 그리지 않음 — 중심에 점이 찍히는 현상 방지
            if (innerRadius > 0.001f)
                DrawArc(vh, innerRadius - borderWidth, innerRadius, startAngleDeg, endAngleDeg, borderColor);
        }
    }

    private void DrawArc(VertexHelper vh, float inner, float outer,
                         float fromDeg, float toDeg, Color col)
    {
        if (Mathf.Approximately(fromDeg, toDeg)) return;

        int baseIndex = vh.currentVertCount;
        float step = (fromDeg - toDeg) / segments;

        for (int i = 0; i <= segments; i++)
        {
            float rad = (fromDeg - step * i) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);

            AddVert(vh, new Vector2(cos * inner, sin * inner), col);
            AddVert(vh, new Vector2(cos * outer, sin * outer), col);
        }

        for (int i = 0; i < segments; i++)
        {
            int b = baseIndex + i * 2;
            vh.AddTriangle(b, b + 1, b + 2);
            vh.AddTriangle(b + 1, b + 3, b + 2);
        }
    }

    private static void AddVert(VertexHelper vh, Vector2 pos, Color col)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = pos;
        v.color = col;
        vh.AddVert(v);
    }

    protected override void Reset()
    {
        base.Reset();
        baseColor = color;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
