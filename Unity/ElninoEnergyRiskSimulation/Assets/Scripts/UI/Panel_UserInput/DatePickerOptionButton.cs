using UnityEngine;

/// <summary>
/// 팝업 안 선택 버튼 — Inspector에서 value 지정 후 Button과 같은 오브젝트에 붙입니다.
/// </summary>
[RequireComponent(typeof(UnityEngine.UI.Button))]
public class DatePickerOptionButton : MonoBehaviour
{
    [SerializeField] private int value;

    public int Value => value;
}
