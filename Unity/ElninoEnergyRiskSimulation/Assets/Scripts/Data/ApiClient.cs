using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class ApiClient : MonoBehaviour
{
    [Header("서버 설정")]
    public string serverUrl = "http://localhost:5001";

    public event Action<string> OnError;

    // -----------------------------------------------------------------------
    // 외부 호출 진입점
    // -----------------------------------------------------------------------

    public void FetchHealth(Action<JObject> onSuccess)
    {
        StartCoroutine(GetJObject($"{serverUrl}/health", onSuccess));
    }

    public void FetchOni(int year, int month, Action<JObject> onSuccess)
    {
        string url = $"{serverUrl}/oni?year={year}&month={month}";
        StartCoroutine(GetJObject(url, onSuccess));
    }

    // ONI 슬라이더 조정시 발생되는 API
    public void FetchPredict(int year, int month, float oni, Action<JObject> onSuccess)
    {
        string url = $"{serverUrl}/predict?year={year}&month={month}&oni={oni}";
        StartCoroutine(GetJObject(url, onSuccess));
    }

    // 차트용
    public void FetchOniRange(int year, int month, Action<JObject> onSuccess)
    {
        string url = $"{serverUrl}/predict/oni_range?year={year}&month={month}";
        StartCoroutine(GetJObject(url, onSuccess));
    }
    
    // 위험도 
    public void FetchBlackoutSimulation(int year, int month, float oni, Action<JObject> onSuccess)
    {
        string body = $"{{\"year\":{year},\"month\":{month},\"oni\":{oni}}}";
        StartCoroutine(Post($"{serverUrl}/blackout_simulation", body, onSuccess));
    }

    // 현재 기온 - 0630 예정 추가
    public void FetchCurrentWeather(Action<JObject> onSuccess)
    {
        StartCoroutine(GetJObject($"{serverUrl}/weather/current", onSuccess));
    }


    // 현재 공급예비율 - 0702 추가
    public void FetchCurrentPower(Action<JObject> onSuccess)
    {
        StartCoroutine(GetJObject($"{serverUrl}/power/current", onSuccess));
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
