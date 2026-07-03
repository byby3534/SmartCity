using UnityEngine;

/// <summary>
/// 씬 싱글톤 참조 해석·검증 공통 패턴.
/// Awake에서 Resolve, OnEnable에서 Require 후 이벤트 구독.
/// </summary>
public static class SceneRefs
{
    public static void Resolve<T>(ref T reference, bool includeInactive = false) where T : Object
    {
        if (reference != null) return;

        reference = includeInactive
            ? Object.FindFirstObjectByType<T>(FindObjectsInactive.Include)
            : Object.FindFirstObjectByType<T>();
    }

    /// <summary>
    /// 같은(또는 지정) GameObject에 붙는 컴포넌트용.
    /// 인스펙터 연결 우선 → GetComponent → 없으면 AddComponent.
    /// </summary>
    public static void EnsureOn<T>(ref T reference, GameObject host, bool includeChildren = false) where T : Component
    {
        if (reference != null) return;

        reference = includeChildren
            ? host.GetComponentInChildren<T>(true)
            : host.GetComponent<T>();

        if (reference != null) return;

        // abstract 컴포넌트(Graphic, TMP_Text 등)는 AddComponent가 불가능하므로 호출부에서 직접 GetComponent 처리한다.
        if (typeof(T).IsAbstract)
        {
            Debug.LogError($"[SceneRefs] {typeof(T).Name}은(는) abstract라 자동 부착할 수 없습니다. 인스펙터에서 연결하세요.", host);
            return;
        }

        reference = host.AddComponent<T>();
    }

    public static bool Require(MonoBehaviour owner, Object reference, string fieldName)
    {
        if (reference != null) return true;

        Debug.LogError($"[{owner.GetType().Name}] {fieldName}가 연결되지 않았습니다.", owner);
        return false;
    }

    public static bool RequireAll(MonoBehaviour owner, params (Object reference, string fieldName)[] fields)
    {
        bool ok = true;

        foreach ((Object reference, string fieldName) field in fields)
        {
            if (field.reference != null) continue;

            Debug.LogError($"[{owner.GetType().Name}] {field.fieldName}가 연결되지 않았습니다.", owner);
            ok = false;
        }

        return ok;
    }
}
