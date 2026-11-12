using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 씬 전환: 화면을 검게 덮음 → (로딩 씬) → 다음 씬 → 다시 밝힘
// carryState: 켜면 떠나기 직전 진행 상태를 RunState로 넘기고(수련장 → 본편), 끄면 비움(타이틀 ↔ 게임)
// 로딩 씬(Loading)이 빌드에 있으면 그쪽을 거침: 이전 씬을 먼저 내리고, 다음 씬은 비동기로 받아 진행도를 보여 줌
//        타이틀로 돌아갈 때와 로딩 씬이 없을 때는 예전처럼 바로 불러옴
public class SceneTransition : MonoBehaviour
{
    public const string LoadingScene = "Loading";
    const float FadeOut = 0.7f, FadeIn = 0.8f;
    const float LoadingFade = 0.35f;
    const float MinLoadingTime = 1.6f; // 너무 빨리 지나가 깜빡이지 않게 + 팁을 읽을 시간

    static SceneTransition current;
    Image cover;

    public static bool Busy => current != null;

    public static void Load(string sceneName, bool carryState = true, string spawnAt = null) // [변경] spawnAt: 도착 씬에서 플레이어를 옮길 오브젝트 경로
    {
        if (current != null) return;
        RunState.SpawnAt = spawnAt;
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

        if (carryState) RunState.Capture();
        else RunState.Clear();
        if (GamePause.IsPaused) GamePause.Set(false);
        Time.timeScale = 1f;

        bool useLoading = sceneName != TitleScreen.SceneName && sceneName != LoadingScene
            && Application.CanStreamedLevelBeLoaded(LoadingScene);
        if (useLoading) yield return LoadWithScreen(sceneName);
        else
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            while (!load.isDone) yield return null;
        }
        yield return null; // 새 씬의 첫 프레임(Start)이 끝난 뒤 밝힘

        yield return Fade(1f, 0f, FadeIn);
        current = null;
        Destroy(gameObject);
    }

    // 핵심: 로딩 씬으로 이전 씬을 내리고 → 안 쓰는 리소스 정리 → 다음 씬을 비동기로 받되 다 받을 때까지 켜지 않음
    //        (allowSceneActivation=false면 progress가 0.9에서 멈춤 → 0.9를 100%로 봄)
    IEnumerator LoadWithScreen(string sceneName)
    {
        AsyncOperation toLoading = SceneManager.LoadSceneAsync(LoadingScene);
        while (!toLoading.isDone) yield return null;

        LoadingScreen screen = FindAnyObjectByType<LoadingScreen>();
        if (screen != null) screen.Show(sceneName);
        yield return Fade(1f, 0f, LoadingFade);

        AsyncOperation cleanup = Resources.UnloadUnusedAssets();
        while (!cleanup.isDone) yield return null;

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        load.allowSceneActivation = false;
        float elapsed = 0f;
        while (load.progress < 0.9f || elapsed < MinLoadingTime)
        {
            elapsed += Time.unscaledDeltaTime;
            // 실제 진행도와 최소 시간 중 느린 쪽을 보여 줌 → 막대가 한 번에 튀지 않음
            float shown = Mathf.Min(load.progress / 0.9f, elapsed / MinLoadingTime);
            if (screen != null) screen.SetProgress(shown);
            yield return null;
        }
        if (screen != null) screen.SetProgress(1f);
        yield return new WaitForSecondsRealtime(0.2f);

        yield return Fade(0f, 1f, LoadingFade);
        load.allowSceneActivation = true;
        while (!load.isDone) yield return null;
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
