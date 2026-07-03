using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    [Header("서버 설정")]
    [Tooltip("에디터: http://localhost:5001 | WebGL 배포: /api (nginx same-origin)")]
    public string serverUrl = "http://localhost:5001";

    public event Action<string> OnError;

    private void Awake()
    {
        serverUrl = ResolveServerUrl(serverUrl);
    }

    /// <summary>
    /// WebGL 빌드는 nginx /api 프록시를 사용한다. 에디터는 localhost 직접 호출.
    /// </summary>
    private static string ResolveServerUrl(string configured)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(configured) || configured == "http://localhost:5001")
            return "/api";
#endif
        return configured.TrimEnd('/');
    }

    private string ApiUrl(string path)
    {
        string normalizedPath = path.StartsWith('/') ? path : $"/{path}";
        return $"{serverUrl}{normalizedPath}";
    }

    // -----------------------------------------------------------------------
    // 외부 호출 진입점
    // -----------------------------------------------------------------------

    public void FetchHealth(Action<JObject> onSuccess)
    {
        StartCoroutine(GetJObject(ApiUrl("/health"), onSuccess));
    }

    public void FetchOni(int year, int month, Action<JObject> onSuccess)
    {
        string url = ApiUrl($"/oni?year={year}&month={month}");
        StartCoroutine(GetJObject(url, onSuccess));
    }

    // ONI 슬라이더 조정시 발생되는 API
    public void FetchPredict(int year, int month, float oni, Action<JObject> onSuccess)
    {
        string url = ApiUrl($"/predict?year={year}&month={month}&oni={oni}");
        StartCoroutine(GetJObject(url, onSuccess));
    }

    // 차트용
    public void FetchOniRange(int year, int month, Action<JObject> onSuccess)
    {
        string url = ApiUrl($"/predict/oni_range?year={year}&month={month}");
        StartCoroutine(GetJObject(url, onSuccess));
    }
    
    // 위험도 
    public void FetchBlackoutSimulation(int year, int month, float oni, Action<JObject> onSuccess)
    {
        string body = $"{{\"year\":{year},\"month\":{month},\"oni\":{oni}}}";
        StartCoroutine(Post(ApiUrl("/blackout_simulation"), body, onSuccess));
    }

    // 현재 기온
    public void FetchCurrentWeather(Action<JObject> onSuccess)
    {
        StartCoroutine(GetJObject(ApiUrl("/weather/current"), onSuccess));
    }


    // 현재 공급예비율 - 0702 추가
    public void FetchCurrentPower(Action<JObject> onSuccess)
    {
        // StartCoroutine(GetJObject($"{serverUrl}/power/current", onSuccess));
        StartCoroutine(GetJObject(ApiUrl("/power/current"), onSuccess));

    }

    // -----------------------------------------------------------------------
    // 내부 Coroutine
    // -----------------------------------------------------------------------

    private IEnumerator GetJObject(string url, Action<JObject> onSuccess)
    {
        using UnityWebRequest req = UnityWebRequest.Get(url);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            onSuccess?.Invoke(JObject.Parse(req.downloadHandler.text));
        else
        {
            OnError?.Invoke($"[GET {url}] {req.responseCode} {req.error}");
            onSuccess?.Invoke(null);
        }
    }

    private IEnumerator Post(string url, string jsonBody, Action<JObject> onSuccess)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        using UnityWebRequest req = new(url, "POST");
        req.uploadHandler   = new UploadHandlerRaw(bodyBytes);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
            onSuccess?.Invoke(JObject.Parse(req.downloadHandler.text));
        else
            OnError?.Invoke($"[POST {url}] {req.responseCode} {req.error}");
    }
}
