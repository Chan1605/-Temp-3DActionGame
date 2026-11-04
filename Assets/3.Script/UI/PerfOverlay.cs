using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// 실행하면 자동으로 붙는 성능 도우미 (씬 배치·셋업 불필요)
//  - VSync 켜기 → 티어링(화면 찢어짐) 방지. 에디터에서는 Game 뷰의 VSync 옵션도 켜져 있어야 적용됨
//  - Debug.Log의 스택 트레이스 수집 끄기 → 전투 중 로그가 많을 때 생기는 끊김 완화 (경고·에러는 그대로)
//  - F3: FPS · 평균 프레임 시간 · 최근 2초 최악 프레임 표시 (끊김 확인용)
public class PerfOverlay : MonoBehaviour
{
    const float WorstWindow = 2f;

    static StackTraceLogType savedLogTrace;

    bool show;
    float avgMs = 16.7f;
    float worstMs;
    float worstShown;
    float windowTimer;
    int spikes; // 최근 2초 안 33ms(30fps 미만) 넘은 프레임 수
    int spikesShown;
    readonly StringBuilder sb = new StringBuilder(96);
    GUIStyle style;
    string label = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = -1;

        savedLogTrace = Application.GetStackTraceLogType(LogType.Log);
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.quitting += () => Application.SetStackTraceLogType(LogType.Log, savedLogTrace); // 에디터 설정 원복

        var go = new GameObject("PerfOverlay");
        DontDestroyOnLoad(go);
        go.AddComponent<PerfOverlay>();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) show = !show;

        float ms = Time.unscaledDeltaTime * 1000f;
        avgMs = Mathf.Lerp(avgMs, ms, 0.05f);
        worstMs = Mathf.Max(worstMs, ms);
        if (ms > 33.4f) spikes++;

        windowTimer += Time.unscaledDeltaTime;
        if (windowTimer >= WorstWindow)
        {
            worstShown = worstMs;
            spikesShown = spikes;
            worstMs = 0f;
            spikes = 0;
            windowTimer = 0f;
        }

        if (!show) return;
        sb.Clear();
        sb.Append("FPS ").Append(Mathf.RoundToInt(1000f / Mathf.Max(avgMs, 0.01f)))
          .Append("  (").Append(avgMs.ToString("0.0")).Append("ms)\n최악 ").Append(worstShown.ToString("0.0"))
          .Append("ms · 끊김 ").Append(spikesShown).Append("회/2초\nVSync ").Append(QualitySettings.vSyncCount);
        label = sb.ToString();
    }

    void OnGUI()
    {
        if (!show) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperLeft };
            style.normal.textColor = Color.white;
        }
        GUI.Box(new Rect(10f, 200f, 230f, 70f), label, style); // 나침반 아래
    }
}
