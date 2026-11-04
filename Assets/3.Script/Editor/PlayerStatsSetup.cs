using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 성장 스탯 + 공격 속도 + 데미지 숫자 · 스탯 패널 연결. HUD는 새로 만들지 않고 기존 HUD 캔버스에 두 오브젝트만 추가(다시 실행하면 교체)
public static class PlayerStatsSetup
{
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string SpeedParam = "AttackSpeed";
    const string FontBold = "Assets/9.Font/public/static/Pretendard-Bold.otf";
    const string FontRegular = "Assets/9.Font/public/static/Pretendard-Medium.otf";
    const float Margin = 56f;

    [MenuItem("Tools/Setup/Player Stats")]
    static void Run()
    {
        var combat = Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null)
        {
            Debug.LogError("[StatsSetup] PlayerCombat이 없습니다.");
            return;
        }

        GameObject player = combat.gameObject;
        PlayerStats stats = player.GetComponent<PlayerStats>();
        if (stats == null) stats = Undo.AddComponent<PlayerStats>(player);

        SetRef(combat, "stats", stats);
        SetRef(player.GetComponent<PlayerSkill>(), "stats", stats);

        int states = SetupAnimator();
        BuildUI(stats);

        EditorSceneManager.MarkSceneDirty(player.scene);
        Selection.activeGameObject = player;
        Debug.Log($"[StatsSetup] 완료 · 공격 상태 {states}개에 AttackSpeed 배속 연결, 데미지 숫자 · 스탯 패널 추가");
    }

    // 핵심: 플레이어 공격(AttackData의 stateName)에 해당하는 상태만 AttackSpeed 파라미터로 재생 속도를 곱함 → 이동·회피·스킬은 그대로
    static int SetupAnimator()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[StatsSetup] {ControllerPath}가 없습니다.");
            return 0;
        }

        if (!controller.parameters.Any(p => p.name == SpeedParam))
            controller.AddParameter(new AnimatorControllerParameter { name = SpeedParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        var attackStates = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:AttackData"))
        {
            var data = AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(guid));
            var so = new SerializedObject(data);
            string stateName = so.FindProperty("stateName").stringValue;
            if (!string.IsNullOrEmpty(stateName)) attackStates.Add(stateName);
        }

        int count = 0;
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (!attackStates.Contains(child.state.name)) continue;
            child.state.speedParameter = SpeedParam;
            child.state.speedParameterActive = true;
            count++;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return count;
    }

    static void BuildUI(PlayerStats stats)
    {
        GameObject hud = GameObject.Find("HUD");
        if (hud == null)
        {
            Debug.LogWarning("[StatsSetup] HUD 캔버스가 없어 UI를 건너뜁니다. Tools/Setup/HUD를 먼저 실행하세요.");
            return;
        }

        var bold = AssetDatabase.LoadAssetAtPath<Font>(FontBold);
        var regular = AssetDatabase.LoadAssetAtPath<Font>(FontRegular);

        foreach (string name in new[] { "StatsPanel", "DamageNumbers", "LevelUp" })
        {
            Transform old = hud.transform.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        // 데미지 숫자: 화면 전체를 덮는 빈 영역 (HUD 요소보다 뒤에 그려지도록 맨 앞 형제로)
        RectTransform numbers = Rect("DamageNumbers", hud.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        numbers.SetAsFirstSibling();
        var dn = numbers.gameObject.AddComponent<DamageNumbers>();
        SetRef(dn, "font", bold);

        // 스탯 패널: 좌하단 체력 묶음(높이 84) 바로 위
        RectTransform panel = Rect("StatsPanel", hud.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin + 96f), new Vector2(460f, 74f));
        var sp = panel.gameObject.AddComponent<StatsPanel>();

        Text level = Label("Level", panel, "Lv 1", 22, new Color(0.95f, 0.8f, 0.45f), TextAnchor.LowerLeft, bold,
            Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 48f), new Vector2(300f, 26f));

        // [변경] 경험치 막대: 레벨 글자와 스탯 사이 얇은 금색 줄
        RectTransform xpBack = Rect("XpBar", panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 42f), new Vector2(220f, 4f));
        AddImage(xpBack.gameObject, new Color(0.04f, 0.05f, 0.07f, 0.6f));
        RectTransform xpFill = Rect("Fill", xpBack, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        AddImage(xpFill.gameObject, new Color(0.95f, 0.8f, 0.45f));
        Text statText = Label("Stats", panel, "공격력 100%   공속 1.15\n치명타 10%   치명 피해 150%", 15, new Color(0.82f, 0.85f, 0.9f, 0.85f), TextAnchor.LowerLeft, regular,
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(460f, 40f));
        statText.lineSpacing = 1.1f;

        // 레벨업 알림: 화면 중앙 위 (구역 돌파 문구 아래)
        RectTransform up = Rect("LevelUp", hud.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(600f, 50f));
        var group = up.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        Text upText = Label("Text", up, "성장  Lv 2", 34, new Color(0.75f, 0.92f, 1f), TextAnchor.MiddleCenter, bold,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

        var so = new SerializedObject(sp);
        so.FindProperty("stats").objectReferenceValue = stats;
        so.FindProperty("levelText").objectReferenceValue = level;
        so.FindProperty("xpFill").objectReferenceValue = xpFill; // [변경]
        so.FindProperty("statText").objectReferenceValue = statText;
        so.FindProperty("levelUpGroup").objectReferenceValue = group;
        so.FindProperty("levelUpText").objectReferenceValue = upText;
        so.ApplyModifiedProperties();
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    static void AddImage(GameObject go, Color color)
    {
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor align, Font font,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 sizeDelta)
    {
        RectTransform rt = Rect(name, parent, anchorMin, anchorMax, pivot, pos, sizeDelta);
        var t = rt.gameObject.AddComponent<Text>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        if (font != null) t.font = font;
        var shadow = rt.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    static void SetRef(Object target, string field, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[StatsSetup] {target.GetType().Name}에 '{field}' 필드가 없습니다.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
