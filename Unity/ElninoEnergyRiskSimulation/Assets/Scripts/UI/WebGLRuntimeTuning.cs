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
        foreach (var group in Object.FindObjectsByType<BackgroundBlurGroup>(FindObjectsSortMode.None))
        {
            if (group.RenderDownscaleFactor < 2)
                group.RenderDownscaleFactor = 2;
        }

        foreach (var effect in Object.FindObjectsByType<BlurEffect>(FindObjectsSortMode.None))
        {
            if (effect.BlurDownscaleFactor < 1)
                effect.BlurDownscaleFactor = 1;
        }
#endif
    }
}
