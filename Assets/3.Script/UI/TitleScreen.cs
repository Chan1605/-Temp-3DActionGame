using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

// 타이틀 화면: 뒤에는 기술 미리보기 영상을 돌려 틀고, 왼쪽에 로고 + 메뉴
//  - NEW GAME: 수련을 끝낸 적이 없으면 수련장, 있으면 본편(산문)
//  - TRAINING: 수련을 끝낸 뒤에만 보임 (수련장 다시 하기)
//  - SETTINGS: 같은 씬의 SettingsMenu(inGame 꺼짐)를 열고, 닫히면 메뉴로 돌아옴
// UI는 실행할 때 코드로 만듦. 씬·연결은 Tools/Setup/Title Scene
public class TitleScreen : MonoBehaviour
{
    public const string SceneName = "Title";

    [SerializeField] Font titleFont;
    [SerializeField] Font bodyFont;
    [SerializeField] SettingsMenu settings;
    [SerializeField] VideoClip[] backgroundClips;
    [SerializeField] AudioClip bgm;
    [SerializeField] string tutorialScene = "Tutorial";
    [SerializeField] string mainScene = "SampleScene";
    [SerializeField] Color accent = new Color(0.95f, 0.8f, 0.45f);
    [SerializeField] Color bolt = new Color(0.62f, 0.82f, 1f);
    [SerializeField, Range(0f, 1f)] float videoDim = 0.45f;
    [SerializeField] Vector2 flashInterval = new Vector2(4f, 8f);

    static readonly Color Dim = new Color(1f, 1f, 1f, 0.62f);

    CanvasGroup group;
    GameObject menu;
    Text logo, logoGlow;
    Image flash;
    VideoPlayer video;
    RawImage videoImage;
    RenderTexture videoTexture;
    Image videoCover;
    AudioSource music;
    int clipIndex;
    float flashTimer, flashLevel;
    bool leaving;

    void Start()
    {
        Time.timeScale = 1f;
        if (GamePause.IsPaused) GamePause.Set(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        UIKit.EnsureEventSystem();
        Build();
        StartVideo();
        StartMusic();
        if (settings != null) settings.Closed += () => menu.SetActive(true);
        flashTimer = 1.2f; // 화면이 밝아진 직후 첫 번개
        StartCoroutine(FadeIn());
    }

    void OnDestroy()
    {
        AudioOptions.Changed -= ApplyVolume;
        if (videoTexture != null) videoTexture.Release();
    }

    void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "TitleCanvas", 10);
        Transform root = canvas.transform;

        // 배경 영상 + 어둡게 + 왼쪽 그라데이션 (글자가 잘 읽히게)
        videoImage = new GameObject("Video", typeof(RectTransform)).AddComponent<RawImage>();
        videoImage.transform.SetParent(root, false);
        videoImage.raycastTarget = false;
        UIKit.Stretch(videoImage.rectTransform);
        videoCover = UIKit.Image(root, null, new Color(0f, 0f, 0f, videoDim));
        videoCover.raycastTarget = false;
        UIKit.Stretch(videoCover.rectTransform);
        Image side = UIKit.Image(root, UIKit.Gradient(256), new Color(0.02f, 0.025f, 0.04f, 0.92f));
        side.raycastTarget = false;
        side.rectTransform.anchorMin = Vector2.zero;
        side.rectTransform.anchorMax = new Vector2(0.62f, 1f);
        side.rectTransform.offsetMin = side.rectTransform.offsetMax = Vector2.zero;

        flash = UIKit.Image(root, null, new Color(bolt.r, bolt.g, bolt.b, 0f));
        flash.raycastTarget = false;
        UIKit.Stretch(flash.rectTransform);

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(root, false);
        UIKit.Stretch((RectTransform)content.transform);
        group = content.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        Transform c = content.transform;

        // 로고: 뒤에 같은 글자를 하나 더 깔아 번개 칠 때 번쩍이는 빛으로 사용
        logoGlow = UIKit.Text(c, "뇌검", titleFont, 168, new Color(bolt.r, bolt.g, bolt.b, 0f), TextAnchor.MiddleLeft);
        UIKit.Place(logoGlow.rectTransform, 160f, 330f, 700f, 200f);
        logoGlow.gameObject.AddComponent<Outline>().effectDistance = new Vector2(6f, -6f);
        logoGlow.GetComponent<Outline>().effectColor = new Color(bolt.r, bolt.g, bolt.b, 0.5f);
        logo = UIKit.Text(c, "뇌검", titleFont, 168, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(logo.rectTransform, 160f, 330f, 700f, 200f);
        logo.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -3f);

        Text sub = UIKit.Text(c, "雷  劍", bodyFont, 30, accent, TextAnchor.MiddleLeft);
        UIKit.Place(sub.rectTransform, 168f, 450f, 400f, 40f);
        Image line = UIKit.Image(c, null, new Color(accent.r, accent.g, accent.b, 0.6f));
        line.raycastTarget = false;
        UIKit.Place(line.rectTransform, 168f, 490f, 340f, 2f);

        menu = new GameObject("Menu", typeof(RectTransform));
        menu.transform.SetParent(c, false);
        UIKit.Stretch((RectTransform)menu.transform);
        float y = 580f;
        AddButton("NEW GAME", ref y, NewGame);
        if (TutorialDirector.CompletedBefore) AddButton("TRAINING", ref y, () => Go(tutorialScene));
        AddButton("SETTINGS", ref y, OpenSettings);
        AddButton("QUIT", ref y, Quit);

        Text version = UIKit.Text(c, "Portfolio Build · " + Application.version, bodyFont, 18, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleRight);
        version.rectTransform.anchorMin = version.rectTransform.anchorMax = new Vector2(1f, 0f);
        version.rectTransform.pivot = new Vector2(1f, 0f);
        version.rectTransform.anchoredPosition = new Vector2(-40f, 30f);
        version.rectTransform.sizeDelta = new Vector2(600f, 30f);
    }

    void AddButton(string label, ref float y, System.Action onClick)
    {
        Text t = UIKit.Text(menu.transform, label, titleFont, 36, Dim, TextAnchor.MiddleLeft);
        UIKit.Place(t.rectTransform, 170f, y, 360f, 56f);
        t.color = Color.white; // Button 색 배합이 곱해지므로 원본은 흰색
        UIKit.TextButton(t, Dim, accent, onClick);
        y += 72f;
    }

    // ===== 메뉴 =====
    void NewGame() => Go(TutorialDirector.CompletedBefore ? mainScene : tutorialScene);

    void Go(string scene)
    {
        if (leaving) return;
        leaving = true;
        menu.SetActive(false);
        StartCoroutine(FadeMusic());
        SceneTransition.Load(scene, false); // 새로 시작 → 이전 판의 스킬·레벨을 이어받지 않음
    }

    void OpenSettings()
    {
        if (settings == null) return;
        menu.SetActive(false);
        settings.Open();
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ===== 연출 =====
    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // 번개: 몇 초마다 화면 · 로고가 번쩍 (두 번 깜빡이는 느낌으로 한 번 더 얹음)
        flashTimer -= dt;
        if (flashTimer <= 0f)
        {
            flashLevel = 1f;
            flashTimer = Random.Range(flashInterval.x, flashInterval.y);
            StartCoroutine(SecondFlash());
        }
        flashLevel = Mathf.MoveTowards(flashLevel, 0f, dt * 2.6f);
        float f = flashLevel * flashLevel;
        flash.color = new Color(bolt.r, bolt.g, bolt.b, 0.16f * f);
        logoGlow.color = new Color(bolt.r, bolt.g, bolt.b, 0.85f * f);
        logo.color = Color.Lerp(Color.white, new Color(0.85f, 0.93f, 1f), f);
    }

    IEnumerator SecondFlash()
    {
        yield return new WaitForSecondsRealtime(0.12f);
        flashLevel = Mathf.Max(flashLevel, 0.75f);
    }

    IEnumerator FadeIn()
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.SmoothStep(0f, 1f, t);
            yield return null;
        }
        group.alpha = 1f;
    }

    // 배경 영상: 클립을 차례대로 돌림. 넘어가는 순간은 잠깐 어둡게 덮어 끊김을 가림
    void StartVideo()
    {
        if (backgroundClips == null || backgroundClips.Length == 0)
        {
            videoImage.color = Color.black;
            return;
        }
        var rt = videoTexture = new RenderTexture(1280, 720, 0);
        video = gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.isLooping = false;
        video.audioOutputMode = VideoAudioOutputMode.None;
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = rt;
        video.loopPointReached += _ => StartCoroutine(NextClip());
        videoImage.texture = rt;
        clipIndex = Random.Range(0, backgroundClips.Length);
        video.clip = backgroundClips[clipIndex];
        video.Play();
    }

    IEnumerator NextClip()
    {
        yield return Dip(videoDim, 1f, 0.2f);
        clipIndex = (clipIndex + 1) % backgroundClips.Length;
        video.clip = backgroundClips[clipIndex];
        video.Prepare();
        while (!video.isPrepared) yield return null;
        video.Play();
        yield return Dip(1f, videoDim, 0.35f);
    }

    IEnumerator Dip(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            videoCover.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        videoCover.color = new Color(0f, 0f, 0f, to);
    }

    void StartMusic()
    {
        if (bgm == null) return;
        music = gameObject.AddComponent<AudioSource>();
        music.clip = bgm;
        music.loop = true;
        music.spatialBlend = 0f;
        ApplyVolume();
        AudioOptions.Changed += ApplyVolume;
        music.Play();
    }

    void ApplyVolume()
    {
        if (music != null) music.volume = AudioOptions.BgmVolume;
    }

    IEnumerator FadeMusic()
    {
        if (music == null) yield break;
        float start = music.volume;
        for (float t = 0f; t < 0.7f; t += Time.unscaledDeltaTime)
        {
            music.volume = Mathf.Lerp(start, 0f, t / 0.7f);
            yield return null;
        }
    }
}
