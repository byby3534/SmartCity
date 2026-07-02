using UnityEngine;
using UnityEngine.UI;

public class SwitchToggle : MonoBehaviour
{
    // ON/OFF 값을 관리하는 Unity 기본 Toggle
    [SerializeField] private Toggle toggle;

    // 좌우로 움직이는 동그란 핸들
    [SerializeField] private RectTransform handle;

    // 색을 바꿀 핸들의 Image 컴포넌트
    [SerializeField] private Image handleImage;

    // OFF 상태일 때 핸들 위치
    [SerializeField] private Vector2 offPosition = new Vector2(-60f, 0f);

    // ON 상태일 때 핸들 위치
    [SerializeField] private Vector2 onPosition = new Vector2(60f, 0f);

    // OFF 상태일 때 핸들 색
    private readonly Color offHandleColor = Color.gray;

    // ON 상태일 때 핸들 색: #3E84FF
    private readonly Color onHandleColor = new Color(0.243f, 0.518f, 1f, 1f);

    private void Awake()
    {
        // Toggle 기본 색상 전환이 코드 색 변경을 방해하지 않게 끔
        toggle.transition = Selectable.Transition.None;

        // 시작 상태를 OFF로 설정
        toggle.isOn = false;

        // 토글 값이 바뀔 때마다 화면 갱신
        toggle.onValueChanged.AddListener(UpdateVisual);

        // 시작 화면도 OFF 상태로 반영
        UpdateVisual(false);
    }

    private void OnDestroy()
    {
        // 이벤트 연결 해제
        if (toggle != null)
        {
            toggle.onValueChanged.RemoveListener(UpdateVisual);
        }
    }

    private void UpdateVisual(bool isOn)
    {
        // ON이면 오른쪽, OFF면 왼쪽으로 핸들 이동
        handle.anchoredPosition = isOn ? onPosition : offPosition;

        // ON이면 파란색, OFF면 회색
        handleImage.color = isOn ? onHandleColor : offHandleColor;
    }
}