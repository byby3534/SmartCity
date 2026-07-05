using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using NovaSamples.Effects;
using UnityEngine;

/// <summary>
/// WebGL 브라우저 실행 시 메모리·블러·Cesium 부하를 줄인다.
/// </summary>
public static class WebGLRuntimeTuning
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Application.targetFrameRate = 60;
        var host = new GameObject(nameof(WebGLRuntimeTuning));
        Object.DontDestroyOnLoad(host);
        host.AddComponent<WebGLRuntimeTuningHost>();
#endif
    }

    public static void TuneAll()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        TuneCesiumTilesets();
        TuneBlurGroups();
#endif
    }

    public static void TuneBlurGroups()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        var groups = new List<BackgroundBlurGroup>(
            Object.FindObjectsByType<BackgroundBlurGroup>(FindObjectsSortMode.None));
        if (groups.Count == 0)
            return;

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

        primary.RenderDownscaleFactor = Mathf.Max(primary.RenderDownscaleFactor, 4);
        primary.RenderFrameInterval = Mathf.Max(primary.RenderFrameInterval, 3);
        primary.RenderFrequency = UpdateFrequency.ManualOnly;

        foreach (var effect in Object.FindObjectsByType<BlurEffect>(FindObjectsSortMode.None))
        {
            effect.BlurDownscaleFactor = Mathf.Max(effect.BlurDownscaleFactor, 3);
            effect.BlurFrameInterval = Mathf.Max(effect.BlurFrameInterval, 4);
            effect.BlurFrequency = UpdateFrequency.ManualOnly;

            if (effect.BlurRadius > 20)
                effect.BlurRadius = 20;
        }
#endif
    }

    private static void TuneCesiumTilesets()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        foreach (var tileset in Object.FindObjectsByType<Cesium3DTileset>(FindObjectsSortMode.None))
        {
            if (tileset.maximumScreenSpaceError < 48f)
                tileset.maximumScreenSpaceError = 48f;

            if (tileset.maximumCachedBytes > 64L * 1024 * 1024)
                tileset.maximumCachedBytes = 64L * 1024 * 1024;

            if (tileset.maximumSimultaneousTileLoads > 8)
                tileset.maximumSimultaneousTileLoads = 8;

            tileset.preloadAncestors = false;
            tileset.preloadSiblings = false;
        }
#endif
    }
}

#if UNITY_WEBGL && !UNITY_EDITOR
internal sealed class WebGLRuntimeTuningHost : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return null;
        WebGLRuntimeTuning.TuneAll();
        Destroy(gameObject);
    }
}
#endif
