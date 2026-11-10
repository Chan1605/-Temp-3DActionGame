using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

// 화톳불 스킬트리: 갈래(일섬·검술·방어)별 노드를 고르면 오른쪽에 미리보기 영상 · 설명 · 비용, 해금 버튼
//  - [변경] 상단 탭 기술 | 수련. 수련(패시브: 체력 · 뇌기 칸)은 영상 대신 "지금 → 해금 후" 수치를 크게 보여 줌
//  - 화톳불(Shrine)에서 E로 열림 · 열려 있는 동안 일시정지 · ESC/Back으로 닫음
//  - 씬에 이 컴포넌트가 있으면 스킬트리가 켜짐(SkillTree.Active) → 해금 전 기능은 잠김
//  - 레벨업마다 스킬 포인트. 화톳불 근처에서는 화면 아래 "E 휴식" 안내
// UI는 실행할 때 코드로 생성 (SettingsMenu와 같은 레이아웃 규칙). 배치·연결은 Tools/Setup/Skill Tree + Shrines
[DefaultExecutionOrder(9000)]
public class SkillTreeMenu : MonoBehaviour
{
    public static SkillTreeMenu Instance { get; private set; }

    [SerializeField] SkillTreeData data;
    [SerializeField] Font titleFont;
    [SerializeField] Font bodyFont;
    [SerializeField] PlayerStats stats;
    [SerializeField] float vitalityAmount = 25f;    // [변경] 체력 단련 한 단계
    [SerializeField] float thunderSlotAmount = 1f;  // [변경] 뇌기 확장 한 단계
    [SerializeField] bool unlockAllAtStart;      // 테스트용
    [SerializeField] bool debugKeys = true;      // F9: 포인트 +5
    [SerializeField] Color accent = new Color(0.91f, 0.78f, 0.55f, 1f);
    [SerializeField] Color dimText = new Color(1f, 1f, 1f, 0.6f);

    public bool IsOpen => menuCanvas != null && menuCanvas.gameObject.activeSelf;

    class NodeView
    {
        public SkillTreeData.Node Node;
        public Image Box, Outline;
        public Text Name, Cost;
    }

    readonly List<NodeView> views = new List<NodeView>();
    readonly List<GameObject> tabRoots = new List<GameObject>(); // [변경]
    readonly List<Text> tabTexts = new List<Text>();
    readonly List<Image> tabLines = new List<Image>();
    int tab;
    PlayerHealth health;
    PlayerThunder thunder;
    Image videoFrame;
    Text statText;
    List<SkillTreeData.Node> nodes;
    Canvas menuCanvas, promptCanvas;
    Text pointsText, previewName, previewDesc, previewInfo, unlockText, noVideoText, promptText;
    Button unlockButton;
    RawImage videoImage;
    VideoPlayer video;
    RenderTexture videoTexture;
    Sprite boxSprite, outlineSprite, gradientSprite;
    NodeView selected;
    bool closeRequested;
    Shrine promptShrine;

    void Awake()
    {
        Instance = this;
        nodes = data != null && data.nodes.Count > 0 ? SkillTreeData.WithMissingDefaults(data.nodes) : SkillTreeData.Defaults(); // [변경] 새 노드가 에셋에 없어도 보이게
        SkillTree.Activate(data != null ? data.startPoints : 1);
        if (unlockAllAtStart) SkillTree.UnlockAll();

        boxSprite = UIKit.RoundRect(64, 64, 10f, 0f);
        outlineSprite = UIKit.RoundRect(64, 64, 10f, 2f);
        gradientSprite = UIKit.Gradient(256);
        UIKit.EnsureEventSystem();
        BuildPrompt();
        BuildMenu();
        menuCanvas.gameObject.SetActive(false);
    }

    // [변경] 패시브를 걸 대상. 처음부터 전부 해금(테스트)이면 패시브도 바로 적용
    void Start()
    {
        health = FindAnyObjectByType<PlayerHealth>();
        thunder = FindAnyObjectByType<PlayerThunder>();
        if (unlockAllAtStart)
            foreach (SkillTreeData.Node n in nodes) ApplyPassive(n.id);
    }

    void OnEnable()
    {
        if (stats == null) stats = FindAnyObjectByType<PlayerStats>();
        if (stats != null) stats.LeveledUp += OnLeveledUp;
        SkillTree.Changed += Refresh;
    }

    void OnDisable()
    {
        if (stats != null) stats.LeveledUp -= OnLeveledUp;
        SkillTree.Changed -= Refresh;
        if (IsOpen && GamePause.IsPaused) GamePause.Set(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (videoTexture != null) videoTexture.Release();
    }

    void OnLeveledUp(int level) => SkillTree.AddPoints(data != null ? data.pointsPerLevel : 1);

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();
        if (debugKeys && kb.f9Key.wasPressedThisFrame)
        {
            SkillTree.AddPoints(5);
            Debug.Log($"[SkillTree] 테스트 포인트 +5 → {SkillTree.Points}");
        }
    }

    void LateUpdate()
    {
        // 닫기는 프레임 끝에서 → 같은 프레임 ESC가 설정 패널을 열거나, 클릭이 공격으로 들어가지 않게
        if (closeRequested)
        {
            closeRequested = false;
            menuCanvas.gameObject.SetActive(false);
            if (video != null) video.Stop();
            GamePause.Set(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (IsOpen) Time.timeScale = 0f;
        promptCanvas.gameObject.SetActive(promptShrine != null && !IsOpen && !GamePause.IsPaused);
    }

    // ===== 화톳불에서 호출 =====
    public void SetPrompt(Shrine shrine, bool show)
    {
        if (show) promptShrine = shrine;
        else if (promptShrine == shrine) promptShrine = null;
    }

    public void Open()
    {
        if (IsOpen) return;
        menuCanvas.gameObject.SetActive(true);
        GamePause.Set(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowTab(tab); // [변경]
    }

    public void Close() => closeRequested = true;

    // ===== 상태 =====
    void Refresh()
    {
        if (pointsText == null) return;
        pointsText.text = $"SKILL POINTS  <color=#{ColorUtility.ToHtmlStringRGB(accent)}>{SkillTree.Points}</color>";
        foreach (NodeView v in views)
        {
            bool owned = SkillTree.IsUnlocked(v.Node.id);
            bool ready = RequirementsMet(v.Node);
            bool affordable = ready && SkillTree.Points >= v.Node.cost;
            bool isSelected = v == selected;

            v.Box.color = owned ? new Color(accent.r, accent.g, accent.b, 0.28f) : new Color(1f, 1f, 1f, ready ? 0.07f : 0.03f);
            v.Outline.color = isSelected ? Color.white : owned ? accent : new Color(1f, 1f, 1f, affordable ? 0.55f : 0.18f);
            v.Name.color = owned || isSelected ? Color.white : ready ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 1f, 1f, 0.35f);
            v.Cost.text = owned ? "해금" : $"{v.Node.cost}P";
            v.Cost.color = owned ? accent : affordable ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }
        UpdatePreviewText();
    }

    bool RequirementsMet(SkillTreeData.Node node)
    {
        foreach (SkillId r in node.requires)
            if (!SkillTree.IsUnlocked(r)) return false;
        return true;
    }

    void Select(NodeView view)
    {
        selected = view;
        previewName.text = view.Node.displayName;
        previewDesc.text = view.Node.description;

        // [변경] 수련 노드는 영상 칸 대신 수치 비교
        bool passive = view.Node.tab == 1;
        videoFrame.gameObject.SetActive(!passive);
        videoImage.gameObject.SetActive(!passive);
        statText.gameObject.SetActive(passive);

        VideoClip clip = passive ? null : view.Node.preview;
        noVideoText.gameObject.SetActive(!passive && clip == null);
        videoImage.color = clip != null ? Color.white : new Color(1f, 1f, 1f, 0.05f);
        if (clip != null)
        {
            video.clip = clip;
            video.Play();
        }
        else video.Stop();
        Refresh();
    }

    void UpdatePreviewText()
    {
        if (selected == null) return;
        SkillTreeData.Node node = selected.Node;
        bool owned = SkillTree.IsUnlocked(node.id);
        bool ready = RequirementsMet(node);

        string info = owned ? "해금됨" : $"필요 포인트 {node.cost}";
        if (!owned && !ready)
        {
            var names = new List<string>();
            foreach (SkillId r in node.requires)
                if (!SkillTree.IsUnlocked(r)) names.Add(NameOf(r));
            info += $"  ·  선행: {string.Join(", ", names)}";
        }
        previewInfo.text = info;

        if (node.tab == 1) statText.text = PassiveText(node.id, owned); // [변경]

        bool can = !owned && ready && SkillTree.Points >= node.cost;
        unlockButton.interactable = can;
        unlockText.text = owned ? "해금됨" : $"해금하기 ({node.cost}P)";
    }

    string NameOf(SkillId id)
    {
        foreach (SkillTreeData.Node n in nodes) if (n.id == id) return n.displayName;
        return id.ToString();
    }

    void OnUnlock()
    {
        if (selected == null) return;
        if (!RequirementsMet(selected.Node)) return;
        if (!SkillTree.TryUnlock(selected.Node.id, selected.Node.cost)) return;
        ApplyPassive(selected.Node.id); // [변경]
        UpdatePreviewText();
        Debug.Log($"[SkillTree] 해금 · {selected.Node.displayName} (남은 포인트 {SkillTree.Points})");
    }

    // [변경] 수련 효과 적용 (해금하는 순간 한 번)
    void ApplyPassive(SkillId id)
    {
        switch (id)
        {
            case SkillId.Vitality1:
            case SkillId.Vitality2:
                if (health != null) health.AddMaxHealth(vitalityAmount);
                break;
            case SkillId.ThunderSlot1:
            case SkillId.ThunderSlot2:
                if (thunder != null) thunder.AddMaxGauge(thunderSlotAmount);
                break;
        }
    }

    // [변경] "최대 체력 100 → 125" 형식. 해금한 노드는 현재 값만
    string PassiveText(SkillId id, bool owned)
    {
        string accentHex = ColorUtility.ToHtmlStringRGB(accent);
        bool vitality = id == SkillId.Vitality1 || id == SkillId.Vitality2;
        string label = vitality ? "최대 체력" : "뇌기 칸";
        float now = vitality ? (health != null ? health.MaxHealth : 0f) : (thunder != null ? thunder.MaxGauge : 0f);
        float add = vitality ? vitalityAmount : thunderSlotAmount;
        System.Func<float, string> Format = v => vitality ? Mathf.RoundToInt(v).ToString() : $"{Mathf.RoundToInt(v)}칸";
        if (owned) return $"<size=30>{label}</size>\n<color=#{accentHex}>{Format(now)}</color>";
        return $"<size=30>{label}</size>\n{Format(now)}  →  <color=#{accentHex}>{Format(now + add)}</color>";
    }

    // [변경] 탭 전환: 그 탭의 노드만 보이고 첫 노드를 고름
    void ShowTab(int index)
    {
        tab = Mathf.Clamp(index, 0, tabRoots.Count - 1);
        for (int i = 0; i < tabRoots.Count; i++)
        {
            tabRoots[i].SetActive(i == tab);
            tabTexts[i].color = i == tab ? Color.white : dimText;
            tabLines[i].enabled = i == tab;
        }
        if (selected == null || selected.Node.tab != tab) selected = views.Find(v => v.Node.tab == tab);
        if (selected != null) Select(selected);
        Refresh();
    }

    // ===== 만들기 =====
    void BuildPrompt()
    {
        promptCanvas = UIKit.CreateCanvas(transform, "ShrinePrompt", 150);
        var rect = (RectTransform)promptCanvas.transform;
        Image key = UIKit.Image(rect, outlineSprite, Color.white);
        UIKit.Place(key.rectTransform, 880f, 880f, 44f, 44f);
        Text e = UIKit.Text(key.transform, "E", titleFont, 26, Color.white, TextAnchor.MiddleCenter);
        UIKit.Stretch(e.rectTransform);
        promptText = UIKit.Text(rect, "휴식 · 스킬", bodyFont, 28, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(promptText.rectTransform, 938f, 880f, 300f, 44f);
        promptCanvas.gameObject.SetActive(false);
    }

    void BuildMenu()
    {
        menuCanvas = UIKit.CreateCanvas(transform, "SkillTreeCanvas", 190);
        var root = (RectTransform)menuCanvas.transform;

        Image dim = UIKit.Image(root, null, new Color(0f, 0f, 0f, 0.8f));
        UIKit.Stretch(dim.rectTransform);
        Image side = UIKit.Image(root, gradientSprite, new Color(0f, 0f, 0f, 0.55f));
        side.rectTransform.anchorMin = Vector2.zero;
        side.rectTransform.anchorMax = new Vector2(0f, 1f);
        side.rectTransform.pivot = new Vector2(0f, 0.5f);
        side.rectTransform.sizeDelta = new Vector2(1100f, 0f);

        // 머리글
        Text shrine = UIKit.Text(root, "SHRINE", titleFont, 30, dimText, TextAnchor.MiddleLeft);
        UIKit.Place(shrine.rectTransform, 100f, 97f, 200f, 50f);
        // [변경] 탭: 기술 | 수련 (고른 탭은 하얗게 + 밑줄)
        for (int i = 0; i < SkillTreeData.TabNames.Length; i++)
        {
            int index = i;
            Text t = UIKit.Text(root, SkillTreeData.TabNames[i], titleFont, 30, Color.white, TextAnchor.MiddleLeft);
            UIKit.Place(t.rectTransform, 260f + i * 120f, 97f, 100f, 50f);
            t.raycastTarget = true;
            Button b = t.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => ShowTab(index));
            tabTexts.Add(t);
            Image line = UIKit.Image(root, null, accent);
            UIKit.Place(line.rectTransform, 260f + i * 120f, 128f, 60f, 2f);
            tabLines.Add(line);
        }
        Image headerLine = UIKit.Image(root, null, new Color(1f, 1f, 1f, 0.35f));
        UIKit.Place(headerLine.rectTransform, 520f, 100f, 1030f, 1f);
        pointsText = UIKit.Text(root, "", titleFont, 30, Color.white, TextAnchor.MiddleRight);
        pointsText.supportRichText = true;
        UIKit.Place(pointsText.rectTransform, 1540f, 97f, 300f, 50f);

        BuildTree(root);
        BuildPreview(root);

        // 아래
        Image bottom = UIKit.Image(root, null, new Color(1f, 1f, 1f, 0.35f));
        UIKit.Place(bottom.rectTransform, 70f, 962f, 1780f, 1f);
        Text back = UIKit.Text(root, "Back", titleFont, 32, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(back.rectTransform, 100f, 1002f, 200f, 56f);
        UIKit.TextButton(back, dimText, accent, Close);
        unlockText = UIKit.Text(root, "해금하기", titleFont, 32, Color.white, TextAnchor.MiddleRight);
        UIKit.Place(unlockText.rectTransform, 1420f, 1002f, 400f, 56f);
        unlockButton = UIKit.TextButton(unlockText, Color.white, accent, OnUnlock);
    }

    // 갈래별 세로 열: 제목 + 노드 상자, 선행이 같은 갈래 바로 위 노드면 세로 연결선
    void BuildTree(RectTransform root)
    {
        const float colX0 = 140f, colStep = 300f, boxW = 250f, boxH = 62f, rowY0 = 300f, rowStep = 92f;

        // [변경] 탭마다 노드 묶음을 따로 두고 켜고 끔
        var rowInBranch = new Dictionary<(int, int), int>();
        var lastInBranch = new Dictionary<(int, int), SkillId>();
        for (int t = 0; t < SkillTreeData.TabNames.Length; t++)
        {
            var group = new GameObject(SkillTreeData.TabNames[t], typeof(RectTransform));
            group.transform.SetParent(root, false);
            UIKit.Stretch((RectTransform)group.transform);
            tabRoots.Add(group);

            string[] branches = SkillTreeData.BranchNames[t];
            for (int b = 0; b < branches.Length; b++)
            {
                Text branch = UIKit.Text(group.transform, branches[b], titleFont, 30, accent, TextAnchor.MiddleLeft);
                UIKit.Place(branch.rectTransform, colX0 + b * colStep, 220f, boxW, 50f);
                Image under = UIKit.Image(group.transform, null, new Color(accent.r, accent.g, accent.b, 0.5f));
                UIKit.Place(under.rectTransform, colX0 + b * colStep, 248f, 60f, 2f);
            }
        }

        foreach (SkillTreeData.Node node in nodes)
        {
            int t = Mathf.Clamp(node.tab, 0, tabRoots.Count - 1);
            var parent = (RectTransform)tabRoots[t].transform;
            int b = Mathf.Clamp(node.branch, 0, SkillTreeData.BranchNames[t].Length - 1);
            rowInBranch.TryGetValue((t, b), out int r);
            rowInBranch[(t, b)] = r + 1;
            float x = colX0 + b * colStep;
            float y = rowY0 + r * rowStep;

            // 선행이 같은 갈래의 바로 위 노드면 연결선
            if (r > 0 && lastInBranch.TryGetValue((t, b), out SkillId above) && System.Array.IndexOf(node.requires, above) >= 0)
            {
                Image link = UIKit.Image(parent, null, new Color(1f, 1f, 1f, 0.3f));
                UIKit.Place(link.rectTransform, x + 30f, y - rowStep * 0.5f, 2f, rowStep - boxH);
            }
            lastInBranch[(t, b)] = node.id;

            var view = new NodeView { Node = node };
            view.Box = UIKit.Image(parent, boxSprite, Color.white);
            UIKit.Place(view.Box.rectTransform, x, y, boxW, boxH);
            view.Outline = UIKit.Image(view.Box.transform, outlineSprite, Color.white);
            view.Outline.raycastTarget = false;
            UIKit.Stretch(view.Outline.rectTransform);
            view.Name = UIKit.Text(view.Box.transform, node.displayName, bodyFont, 24, Color.white, TextAnchor.MiddleLeft);
            UIKit.Stretch(view.Name.rectTransform);
            view.Name.rectTransform.offsetMin = new Vector2(18f, 0f);
            view.Cost = UIKit.Text(view.Box.transform, "", titleFont, 22, Color.white, TextAnchor.MiddleRight);
            UIKit.Stretch(view.Cost.rectTransform);
            view.Cost.rectTransform.offsetMax = new Vector2(-16f, 0f);

            Button button = view.Box.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            NodeView captured = view;
            button.onClick.AddListener(() => Select(captured));
            views.Add(view);
        }
    }

    // 오른쪽: 16:9 미리보기 영상 · 이름 · 설명 · 비용/선행
    void BuildPreview(RectTransform root)
    {
        const float x = 1060f, w = 780f, h = w * 9f / 16f;

        videoFrame = UIKit.Image(root, outlineSprite, new Color(1f, 1f, 1f, 0.35f));
        UIKit.Place(videoFrame.rectTransform, x - 4f, 210f + h * 0.5f, w + 8f, h + 8f);

        // [변경] 수련 노드용 수치 비교 (영상 자리)
        statText = UIKit.Text(root, "", titleFont, 64, Color.white, TextAnchor.MiddleCenter);
        statText.supportRichText = true;
        statText.lineSpacing = 1.2f;
        UIKit.Place(statText.rectTransform, x, 210f + h * 0.5f, w, h);
        statText.gameObject.SetActive(false);

        videoTexture = new RenderTexture(854, 480, 0) { name = "SkillPreview" };
        videoImage = new GameObject("Video", typeof(RectTransform)).AddComponent<RawImage>();
        videoImage.transform.SetParent(root, false);
        videoImage.texture = videoTexture;
        videoImage.raycastTarget = false;
        UIKit.Place(videoImage.rectTransform, x, 210f + h * 0.5f, w, h);

        video = videoImage.gameObject.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.isLooping = true;
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = videoTexture;
        video.audioOutputMode = VideoAudioOutputMode.None;
        video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime; // 일시정지(timeScale 0) 중에도 재생

        noVideoText = UIKit.Text(root, "미리보기 영상 준비 중", bodyFont, 24, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter);
        UIKit.Place(noVideoText.rectTransform, x, 210f + h * 0.5f, w, 40f);

        float textY = 210f + h + 50f;
        previewName = UIKit.Text(root, "", titleFont, 38, Color.white, TextAnchor.MiddleLeft);
        UIKit.Place(previewName.rectTransform, x, textY, w, 50f);
        previewDesc = UIKit.Text(root, "", bodyFont, 24, new Color(1f, 1f, 1f, 0.8f), TextAnchor.UpperLeft);
        previewDesc.horizontalOverflow = HorizontalWrapMode.Wrap;
        previewDesc.lineSpacing = 1.15f;
        UIKit.Place(previewDesc.rectTransform, x, textY + 75f, w, 90f);
        previewInfo = UIKit.Text(root, "", bodyFont, 22, accent, TextAnchor.MiddleLeft);
        UIKit.Place(previewInfo.rectTransform, x, textY + 145f, w, 36f);
    }
}
