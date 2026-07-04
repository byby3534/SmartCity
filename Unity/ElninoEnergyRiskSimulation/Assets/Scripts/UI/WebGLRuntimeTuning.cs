using System.Collections.Generic;
using NovaSamples.Effects;
using UnityEngine;

/// <summary>
/// WebGL 브라우저 실행 시 메모리·블러 부하를 줄인다.
/// </summary>
public static class WebGLRuntimeTuning
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.targetFrameRate = 60;
#endif
    }

    public static void TuneBlurGroups()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var groups = new List<BackgroundBlurGroup>(
            Object.FindObjectsByType<BackgroundBlurGroup>(FindObjectsSortMode.None));
        if (groups.Count == 0)
            return;

        // 좌/우 각각 BackgroundBlurGroup이 있으면 Cesium 전체를 두 번 캡처한다 → RT 메모리 2배.
        BackgroundBlurGroup primary = groups[0];
        for (int i = 1; i < groups.Count; i++)
        {
            var duplicate = groups[i];
            if (duplicate.BlurEffects != null)
            {
                foreach (BlurEffect effect in duplicate.BlurEffects)
                {
                    if (effect != null && !primary.BlurEffects.Contains(effect))
                        primary.BlurEffects.Add(effect);
                }
            }

            duplicate.enabled = false;
        }

        if (primary.RenderDownscaleFactor < 3)
            primary.RenderDownscaleFactor = 3;

        if (primary.RenderFrameInterval < 2)
            primary.RenderFrameInterval = 2;

        foreach (var effect in Object.FindObjectsByType<BlurEffect>(FindObjectsSortMode.None))
        {
            if (effect.BlurDownscaleFactor < 2)
                effect.BlurDownscaleFactor = 2;

            if (effect.BlurFrameInterval < 2)
                effect.BlurFrameInterval = 2;

            if (effect.BlurRadius > 24)
                effect.BlurRadius = 24;
        }
#endif
    }
}
