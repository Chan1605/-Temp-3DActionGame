using UnityEngine;
using UnityEngine.UI;

// 로딩 씬 화면 (SceneTransition이 Show · SetProgress 호출). UI는 실행할 때 코드로 만듦
//  왼쪽 아래: 다음 장소 이름 + 팁 / 오른쪽: 팁과 맞는 기술 아이콘 (번개처럼 깜빡임) / 맨 아래: 진행 막대 + %
public class LoadingScreen : MonoBehaviour
{
    [SerializeField] Font titleFont;
    [SerializeField] Font bodyFont;

    static readonly Color Gold = new Color(0.95f, 0.8f, 0.45f);
    static readonly Color Bolt = new Color(0.62f, 0.82f, 1f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

    // 씬 이름 → 화면에 보일 장소 이름
    static readonly string[,] Places =
    {
        { "Tutorial", "수련장", "노을 진 수련 마당" },
        { "SampleScene", "산문", "흑뢰의 제단으로 가는 첫 관문" },
        { "Training", "연습장", "모든 기술을 마음껏 시험하는 마당" },
        { "Dungeon", "뇌운 협곡", "봉인 셋을 풀고 수문장을 쓰러뜨려라" },
    };

    // 팁 문구 · 아이콘 이름 (Resources/UI/Icons)
    static readonly string[,] Tips =
    {
        { "적의 칼이 닿기 직전에 회피하면 시간이 느려지고 뇌기 1칸을 얻습니다.", "DodgeDraw" },
        { "저스트 회피 직후 Q를 누르면 뇌기 없이 바로 발도합니다.", "DodgeDraw" },
        { "Q를 길게 누르면 발도 자세. 조준점을 올려 둔 적을 표식 순서대로 벱니다.", "StanceMarks" },
        { "콤보 중 적이 공격하면 우클릭으로 간파해 맞받아칠 수 있습니다.", "KanpaSmash" },
        { "패링으로 무너진 적에게 다가가 F를 누르면 처형합니다. 처형은 뇌기를 크게 채웁니다.", "HudBolt" },
        { "뇌기 2칸 이상에서 R. 각성 중엔 평타마다 낙뢰가 떨어지고 패링이 무료입니다.", "Enchant" },
        { "각성 중 Q는 각성 시간을 떼어 발도 시간을 엽니다. 그동안 표식 수 제한이 없습니다.", "EnchantDraw" },
        { "화톳불에서 쉬면 체력이 회복되고 스킬트리를 열 수 있습니다.", "HudHeart" },
        { "보스는 벽력일섬에 즉사하지 않습니다. 체간을 무너뜨린 뒤에 노리세요.", "ThunderFlash" },
        { "패링 · 간파 · 저스트 회피 · 처형으로 궁극기 게이지가 찹니다. V를 짧게 누르면 낙뢰진, 누르고 있으면 천뢰일섬 → 뇌신강림.", "ThunderFlash" }, // [변경]
    };

    Text placeText, placeSub, tipText, percentText;
    Image barFill, icon;
    float iconBase = 0.14f, flash;
    float flashTimer;

    void Awake() => Build();

    public void Show(string sceneName)
    {
        string place = sceneName, sub = "";
        for (int i = 0; i < Places.GetLength(0); i++)
            if (Places[i, 0] == sceneName) { place = Places[i, 1]; sub = Places[i, 2]; }
        if (sceneName == "SampleScene" && RunState.HasFlag(DungeonDirector.ClearedFlag)) // 던전에서 돌아갈 때
        {
            place = "흑뢰의 제단";
            sub = "협곡을 지나 마지막 제단으로";
        }
        placeText.text = place;
        placeSub.text = sub;

        int tip = Random.Range(0, Tips.GetLength(0));
        tipText.text = Tips[tip, 0];
        Sprite sprite = UiIcons.Get(Tips[tip, 1]);
        icon.sprite = sprite;
        icon.enabled = sprite != null;
        SetProgress(0f);
        flashTimer = 0.4f;
    }

    public void SetProgress(float value)
    {
        value = Mathf.Clamp01(value);
        barFill.rectTransform.anchorMax = new Vector2(value, 1f);
        percentText.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }

    void Update()
    {
        // 아이콘이 가끔 번개처럼 번쩍
        float dt = Time.unscaledDeltaTime;
        flashTimer -= dt;
        if (flashTimer <= 0f)
        {
            flash = 1f;
            flashTimer = Random.Range(1.2f, 2.4f);
        }
        flash = Mathf.MoveTowards(flash, 0f, dt * 3f);
        icon.color = new Color(Bolt.r, Bolt.g, Bolt.b, iconBase + 0.3f * flash * flash);
    }

    void Build()
    {
        Canvas canvas = UIKit.CreateCanvas(transform, "LoadingCanvas", 100);
        Transform r = canvas.transform;

        Image bg = UIKit.Image(r, null, new Color(0.025f, 0.03f, 0.045f, 1f));
        UIKit.Stretch(bg.rectTransform);
        Image side = UIKit.Image(r, UIKit.Gradient(256), new Color(0f, 0f, 0f, 0.6f));
        UIKit.Stretch(side.rectTransform);

        icon = UIKit.Image(r, null, new Color(Bolt.r, Bolt.g, Bolt.b, iconBase));
        icon.preserveAspect = true;
        RectTransform ir = icon.rectTransform;
        ir.anchorMin = ir.anchorMax = new Vector2(1f, 0.5f);
        ir.pivot = new Vector2(1f, 0.5f);
        ir.anchoredPosition = new Vector2(-160f, 40f);
        ir.sizeDelta = new Vector2(560f, 560f);

        Text next = UIKit.Text(r, "다음 장소", bodyFont, 22, Gold, TextAnchor.MiddleLeft);
        UIKit.Place(next.rectTransform, 140f, 690f, 600f, 30f);
        placeText = UIKit.Text(r, "", titleFont, 76, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(placeText.rectTransform, 136f, 760f, 1100f, 96f);
        placeText.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -3f);
        placeSub = UIKit.Text(r, "", bodyFont, 26, Dim, TextAnchor.MiddleLeft);
        UIKit.Place(placeSub.rectTransform, 140f, 825f, 1100f, 36f);

        Image line = UIKit.Image(r, null, new Color(Gold.r, Gold.g, Gold.b, 0.5f));
        UIKit.Place(line.rectTransform, 140f, 870f, 380f, 2f);
        Text tipLabel = UIKit.Text(r, "TIP", titleFont, 22, Gold, TextAnchor.UpperLeft);
        UIKit.Place(tipLabel.rectTransform, 140f, 912f, 80f, 30f);
        tipText = UIKit.Text(r, "", bodyFont, 25, new Color(0.86f, 0.88f, 0.92f), TextAnchor.UpperLeft);
        tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
        UIKit.Place(tipText.rectTransform, 210f, 925f, 1000f, 64f);

        // 진행 막대: 화면 맨 아래 전체 폭
        Image barBg = UIKit.Image(r, null, new Color(1f, 1f, 1f, 0.08f));
        RectTransform br = barBg.rectTransform;
        br.anchorMin = new Vector2(0f, 0f);
        br.anchorMax = new Vector2(1f, 0f);
        br.pivot = new Vector2(0.5f, 0f);
        br.anchoredPosition = Vector2.zero;
        br.sizeDelta = new Vector2(0f, 6f);
        barFill = UIKit.Image(barBg.transform, null, Gold);
        RectTransform fr = barFill.rectTransform;
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = new Vector2(0f, 1f);
        fr.offsetMin = fr.offsetMax = Vector2.zero;

        percentText = UIKit.Text(r, "0%", titleFont, 24, Dim, TextAnchor.MiddleRight);
        RectTransform pr = percentText.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(1f, 0f);
        pr.pivot = new Vector2(1f, 0f);
        pr.anchoredPosition = new Vector2(-60f, 26f);
        pr.sizeDelta = new Vector2(200f, 34f);
    }
}
