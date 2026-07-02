using System.Collections;
using CesiumForUnity;
using UnityEngine;

/// <summary>
/// Cesium 화면 하단 크레딧/로고 UI를 숨깁니다.
/// 런타임에 생성되는 CesiumCreditSystemDefault는 HideFlags 때문에 Find로는 잡히지 않습니다.
/// </summary>
public class HideCesiumCredit : MonoBehaviour
{
    [SerializeField] private int retryFrames = 60;

    private void Start()
    {
        StartCoroutine(HideCreditsWhenReady());
    }

    private IEnumerator HideCreditsWhenReady()
    {
        for (int i = 0; i < retryFrames; i++)
        {
            if (HideAllCreditSystems())
                yield break;

            yield return null;
        }

        HideAllCreditSystems();
    }

    private bool HideAllCreditSystems()
    {
        bool found = false;

        foreach (CesiumCreditSystem credit in Resources.FindObjectsOfTypeAll<CesiumCreditSystem>())
        {
            if (credit == null || credit.gameObject == null)
                continue;

            credit.gameObject.SetActive(false);
            found = true;
        }

        return found;
    }
}
