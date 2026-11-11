using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 화면을 검게 덮고 다음 씬을 불러온 뒤 다시 밝힘
// carryState: 켜면 떠나기 직전 진행 상태를 RunState로 넘기고(수련장 → 본편), 끄면 비움(타이틀 ↔ 게임)
public class SceneTransition : MonoBehaviour
{
    const float FadeOut = 0.7f, FadeIn = 0.8f;

    static SceneTransition current;
    Image cover;

    public static bool Busy => current != null;

    public static void Load(string sceneName, bool carryState = true) // [변경] 타이틀에서 새로 시작할 땐 이어받지 않음
    {
        if (current != null) return;
        var go = new GameObject("SceneTransition");
        DontDestroyOnLoad(go);
        current = go.AddComponent<SceneTransition>();
        current.StartCoroutine(current.Run(sceneName, carryState));
    }

    IEnumerator Run(string sceneName, bool carryState)
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "Fade", 500); // 메뉴보다 위
        cover = UIKit.Image(canvas.transform, null, new Color(0f, 0f, 0f, 0f));
        UIKit.Stretch(cover.rectTransform);

        yield return Fade(0f, 1f, FadeOut);

        if (carryState) RunState.Capture(); // [변경]
        else RunState.Clear();
        if (GamePause.IsPaused) GamePause.Set(false);
        Time.timeScale = 1f;
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        while (!load.isDone) yield return null;
        yield return null; // 새 씬의 첫 프레임(Start)이 끝난 뒤 밝힘

        yield return Fade(1f, 0f, FadeIn);
        current = null;
        Destroy(gameObject);
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            cover.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        cover.color = new Color(0f, 0f, 0f, to);
    }
}
