using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// StreamingAssets 텍스트 파일을 플랫폼 무관하게 읽는다.
/// WebGL/Android에서는 streamingAssetsPath가 URL/압축 경로라 System.IO.File이 동작하지 않으므로
/// UnityWebRequest로 읽어야 한다.
/// </summary>
public static class StreamingAssetsLoader
{
    /// <summary>
    /// StreamingAssets 내 파일을 텍스트로 읽어 onLoaded(text)로 전달한다.
    /// 실패 시 onLoaded(null) + onError(message).
    /// </summary>
    public static IEnumerator LoadText(string fileName, Action<string> onLoaded, Action<string> onError = null)
    {
        string path = Path.Combine(Application.streamingAssetsPath, fileName);

#if UNITY_WEBGL && !UNITY_EDITOR
        bool useWebRequest = true;
#else
        // 에디터/스탠드얼론은 로컬 파일 경로. jar:/http: 로 시작하면(Android 등) UnityWebRequest 사용.
        bool useWebRequest = path.Contains("://");
#endif

        if (useWebRequest)
        {
            using UnityWebRequest request = UnityWebRequest.Get(path);
            yield return request.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool failed = request.result != UnityWebRequest.Result.Success;
#else
            bool failed = request.isNetworkError || request.isHttpError;
#endif
            if (failed)
            {
                string message = $"[StreamingAssetsLoader] 로드 실패: {path} ({request.error})";
                Debug.LogError(message);
                onError?.Invoke(message);
                onLoaded?.Invoke(null);
                yield break;
            }

            onLoaded?.Invoke(request.downloadHandler.text);
            yield break;
        }

        if (!File.Exists(path))
        {
            string message = $"[StreamingAssetsLoader] 파일 없음: {path}";
            Debug.LogError(message);
            onError?.Invoke(message);
            onLoaded?.Invoke(null);
            yield break;
        }

        onLoaded?.Invoke(File.ReadAllText(path));
    }
}
