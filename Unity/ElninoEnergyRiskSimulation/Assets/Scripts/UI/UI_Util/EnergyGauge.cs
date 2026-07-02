using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnergyGauge : MonoBehaviour
{
    [Range(0, 100)]
    public float energy = 80f; // 현재 게이지 값

    [Header("게이지 칸들")]
    public Image[] segments; // 게이지를 구성하는 칸 이미지들

    [Header("퍼센트 텍스트")]
    public TMP_Text textPercent; // 퍼센트 표시 텍스트

    [Header("색상 설정")]
    public Color activeColor = new Color(1f, 0.65f, 0f); // 활성화 색상 (주황)
    public Color inactiveColor = new Color(0.2f, 0.2f, 0.2f); // 비활성화 색상 (회색)

    private void Update()
    {
        // 인스펙터에서 값을 변경하면 실시간 반영
        UpdateGauge(energy);
    }
    // 게이지 값 업데이트
    //<param name="value">0 ~ 100 사이 값</param>
    public void UpdateGauge(float value)
    {
        // 값 제한
        value = Mathf.Clamp(value, 0, 100);

        // 현재 활성화되어야 할 칸 개수 계산
        int activeCount = Mathf.RoundToInt(value / 100f * segments.Length);

        // 각 칸 색상 변경
        for (int i = 0; i < segments.Length; i++)
        {
            // 활성화 구간이면 주황색, 아니면 회색
            segments[i].color = i < activeCount
                ? activeColor
                : inactiveColor;
        }

        // 퍼센트 텍스트 갱신
        if (textPercent != null)
        {
            textPercent.text = $"{value:0}%";
        }
    }
}