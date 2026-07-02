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
