using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 보스 처치 결과 화면 (RunRecord.Finished). 게임을 멈추고 기록을 한 줄씩 띄운 뒤 랭크를 찍음
//  - CONTINUE: 닫고 이어서 (시련의 터) · MAIN MENU: 타이틀로 (Title 씬이 빌드에 있을 때만)
public class ResultScreen : MonoBehaviour
{
    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color Bolt = new Color(0.62f, 0.82f, 1f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.6f);

    public static bool IsShowing { get; private set; }

    RunRecord record;
    Font titleFont, bodyFont;
    GameObject root;
    Image backdrop, flash;
    readonly List<CanvasGroup> rows = new List<CanvasGroup>();
    Text timeValue, rankText, scoreText;
    CanvasGroup rankGroup, buttonGroup;

    void Start()
    {
        record = GetComponent<RunRecord>();
        if (record != null) record.Finished += Show;
    }

    void OnDestroy()
    {
        IsShowing = false;
        if (record != null) record.Finished -= Show;
    }

    void Show()
    {
        if (IsShowing) return;
        IsShowing = true;
        GamePause.Set(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SettingsMenu settings = FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        if (settings != null) { titleFont = settings.TitleFont; bodyFont = settings.BodyFont; }
        UIKit.EnsureEventSystem();
        Build();
        StartCoroutine(Reveal());
    }

    // 멈춘 사이 히트스톱 등이 timeScale을 되돌려도 다시 멈춤
    void LateUpdate()
    {
        if (IsShowing) Time.timeScale = 0f;
    }

    void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "ResultCanvas", 150); // HUD 위, 설정 창(200) 아래
        root = canvas.gameObject;
        Transform r = canvas.transform;

        backdrop = UIKit.Image(r, null, new Color(0f, 0f, 0f, 0f));
        UIKit.Stretch(backdrop.rectTransform);
        flash = UIKit.Image(r, null, new Color(Bolt.r, Bolt.g, Bolt.b, 0f));
        flash.raycastTarget = false;
        UIKit.Stretch(flash.rectTransform);

        const float x = 560f, w = 480f;
        Text zone = UIKit.Text(r, record.FinalZone, bodyFont, 24, Gold, TextAnchor.MiddleLeft);
        UIKit.Place(zone.rectTransform, x, 200f, 800f, 34f);
        Text title = UIKit.Text(r, "토벌 완료", titleFont, 72, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(title.rectTransform, x, 262f, 800f, 90f);
        title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -3f);
        Image line = UIKit.Image(r, null, new Color(Gold.r, Gold.g, Gold.b, 0.6f));
        line.raycastTarget = false;
        UIKit.Place(line.rectTransform, x, 322f, 800f, 2f);

        float y = 380f;
        timeValue = AddRow(x, w, ref y, "클리어 시간", "00:00", record.NewBestTime ? "신기록" : "");
        AddRow(x, w, ref y, "처치", record.Kills.ToString(), "");
        AddRow(x, w, ref y, "처형", record.Executions.ToString(), "");
        AddRow(x, w, ref y, "패링 · 간파", $"{record.Parries} · {record.Counters}", "");
        AddRow(x, w, ref y, "저스트 회피", record.JustDodges.ToString(), "");
        AddRow(x, w, ref y, "벽력일섬", record.SkillCuts.ToString(), "");
        AddRow(x, w, ref y, "피격", record.HitsTaken.ToString(), "");
        AddRow(x, w, ref y, "재도전", record.Deaths.ToString(), "");
        AddRow(x, w, ref y, "레벨", record.Level.ToString(), "");

        // 랭크 (오른쪽)
        var rankRoot = new GameObject("Rank", typeof(RectTransform));
        rankRoot.transform.SetParent(r, false);
        UIKit.Stretch((RectTransform)rankRoot.transform);
        rankGroup = rankRoot.AddComponent<CanvasGroup>();
        rankGroup.alpha = 0f;
        Text rankLabel = UIKit.Text(rankRoot.transform, "RANK", titleFont, 26, Dim, TextAnchor.MiddleCenter);
        UIKit.Place(rankLabel.rectTransform, 1130f, 420f, 260f, 34f);
        rankText = UIKit.Text(rankRoot.transform, record.Rank, titleFont, 230, RankColor(record.Rank), TextAnchor.MiddleCenter);
        UIKit.Place(rankText.rectTransform, 1130f, 570f, 260f, 260f);
        rankText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        scoreText = UIKit.Text(rankRoot.transform, $"{record.Score} / 100", bodyFont, 26, Dim, TextAnchor.MiddleCenter);
        UIKit.Place(scoreText.rectTransform, 1130f, 715f, 260f, 34f);

        // 버튼
        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(r, false);
        UIKit.Stretch((RectTransform)buttons.transform);
        buttonGroup = buttons.AddComponent<CanvasGroup>();
        buttonGroup.alpha = 0f;
        buttonGroup.interactable = false;
        AddButton(buttons.transform, "CONTINUE", x, Continue);
        if (Application.CanStreamedLevelBeLoaded(TitleScreen.SceneName))
            AddButton(buttons.transform, "MAIN MENU", x + 300f, () => SceneTransition.Load(TitleScreen.SceneName, false));
        Text hint = UIKit.Text(buttons.transform, "CONTINUE: 계속 탐험 · 시련의 터 도전", bodyFont, 20, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
        UIKit.Place(hint.rectTransform, x, 960f, 800f, 30f);
    }

    Text AddRow(float x, float w, ref float y, string label, string value, string tag)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(root.transform, false);
        UIKit.Stretch((RectTransform)go.transform);
        CanvasGroup g = go.AddComponent<CanvasGroup>();
        g.alpha = 0f;
        rows.Add(g);

        Text l = UIKit.Text(go.transform, label, bodyFont, 26, Dim, TextAnchor.MiddleLeft);
        UIKit.Place(l.rectTransform, x, y, w, 40f);
        Text v = UIKit.Text(go.transform, value, titleFont, 30, Color.white, TextAnchor.MiddleRight);
        UIKit.Place(v.rectTransform, x, y, w, 40f);
        if (!string.IsNullOrEmpty(tag))
        {
            Text t = UIKit.Text(go.transform, tag, titleFont, 20, Gold, TextAnchor.MiddleLeft);
            UIKit.Place(t.rectTransform, x + w + 18f, y, 120f, 40f);
        }
        y += 52f;
        return v;
    }

    void AddButton(Transform parent, string label, float x, System.Action onClick)
    {
        Text t = UIKit.Text(parent, label, titleFont, 34, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(t.rectTransform, x, 905f, 280f, 56f);
        UIKit.TextButton(t, Dim, Gold, onClick);
    }

    static Color RankColor(string rank)
    {
        switch (rank)
        {
            case "S": return Gold;
            case "A": return Bolt;
            case "B": return Color.white;
            default: return new Color(0.6f, 0.6f, 0.62f);
        }
    }

    // 배경 어둡게 → 줄마다 밀려 들어옴 (시간은 숫자가 올라감) → 랭크 쾅 + 번쩍 → 버튼
    IEnumerator Reveal()
    {
        yield return Tween(0.5f, t => backdrop.color = new Color(0f, 0f, 0f, 0.8f * t));

        for (int i = 0; i < rows.Count; i++)
        {
            CanvasGroup g = rows[i];
            StartCoroutine(Tween(0.25f, t =>
            {
                g.alpha = t;
                g.transform.localPosition = new Vector3(30f * (1f - t), 0f, 0f);
            }));
            if (i == 0) StartCoroutine(Tween(0.8f, t => timeValue.text = RunRecord.FormatTime(record.PlayTime * t)));
            yield return new WaitForSecondsRealtime(0.09f);
        }
        yield return new WaitForSecondsRealtime(0.6f);

        Transform rank = rankText.transform;
        yield return Tween(0.22f, t =>
        {
            rankGroup.alpha = t;
            rank.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, t * t);
        });
        StartCoroutine(Tween(0.5f, t => flash.color = new Color(Bolt.r, Bolt.g, Bolt.b, 0.35f * (1f - t))));

        yield return new WaitForSecondsRealtime(0.3f);
        yield return Tween(0.3f, t => buttonGroup.alpha = t);
        buttonGroup.interactable = true;
    }

    static IEnumerator Tween(float duration, System.Action<float> step)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            step(Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        step(1f);
    }

    void Continue()
    {
        Destroy(root);
        IsShowing = false;
        GamePause.Set(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
