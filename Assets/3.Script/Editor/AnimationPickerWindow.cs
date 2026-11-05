using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 애니메이션 고르기: 팩 클립을 씬의 캐릭터에 바로 재생해 보고(에디트 모드), 마음에 들면 컨트롤러 상태에 적용
// 적용하면 같은 상태를 쓰는 AttackData의 클립도 같이 바꾸고, 클립 정리 + 판정 구간 재측정까지 이어서 함
public class AnimationPickerWindow : EditorWindow
{
    const string DefaultRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid";
    static readonly string[] ControllerPaths =
    {
        "Assets/5.Animation/Player/Player.controller",
        "Assets/5.Animation/Enemy/Swordsman.controller"
    };
    static readonly string[] ControllerLabels = { "플레이어", "적 (검병 공용)" };

    string root = DefaultRoot;
    string search = "";
    int controllerIndex;
    AnimatorController controller;
    AnimatorState targetState;
    int blendChild = -1;
    // 블렌드 트리 안의 트리(평상시/전투 자세)까지 펼친 칸 목록
    readonly List<(BlendTree tree, int index, string label)> leaves = new List<(BlendTree, int, string)>();
    Animator previewTarget;

    readonly List<AnimationClip> clips = new List<AnimationClip>();
    readonly Dictionary<string, bool> folderOpen = new Dictionary<string, bool>();
    AnimationClip candidate;
    AnimationClip playing;
    float time;
    float speed = 1f;
    bool isPlaying;
    double lastTick;
    bool loopSetting;
    bool refit = true;
    Vector2 clipScroll, stateScroll;

    [MenuItem("Tools/Animation/Animation Picker")]
    static void Open() => GetWindow<AnimationPickerWindow>("애니메이션 고르기");

    void OnEnable()
    {
        root = EditorPrefs.GetString("AnimPicker.Root", DefaultRoot);
        controllerIndex = EditorPrefs.GetInt("AnimPicker.Controller", 0);
        RefreshClips();
        LoadController();
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        StopPreview();
    }

    // ===== 화면 =====
    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox("플레이 모드에서는 미리보기·적용을 할 수 없습니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        DrawClipList();
        EditorGUILayout.BeginVertical(GUILayout.Width(Mathf.Max(320f, position.width * 0.45f)));
        DrawTarget();
        EditorGUILayout.Space(6f);
        DrawPreview();
        EditorGUILayout.Space(6f);
        DrawApply();
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    void DrawClipList()
    {
        EditorGUILayout.BeginVertical();
        EditorGUILayout.BeginHorizontal();
        string newRoot = EditorGUILayout.TextField(root);
        if (newRoot != root)
        {
            root = newRoot;
            EditorPrefs.SetString("AnimPicker.Root", root);
        }
        if (GUILayout.Button("새로고침", GUILayout.Width(70f))) RefreshClips();
        EditorGUILayout.EndHorizontal();

        search = EditorGUILayout.TextField("검색", search);
        string filter = search.Trim().ToLowerInvariant();

        clipScroll = EditorGUILayout.BeginScrollView(clipScroll);
        foreach (var group in clips.GroupBy(FolderOf))
        {
            var shown = group.Where(c => filter.Length == 0 || c.name.ToLowerInvariant().Contains(filter) || group.Key.ToLowerInvariant().Contains(filter)).ToList();
            if (shown.Count == 0) continue;

            folderOpen.TryGetValue(group.Key, out bool open);
            open = EditorGUILayout.Foldout(open || filter.Length > 0, $"{group.Key}  ({shown.Count})", true);
            if (filter.Length == 0) folderOpen[group.Key] = open;
            if (!open) continue;

            foreach (AnimationClip clip in shown)
            {
                Color prev = GUI.backgroundColor;
                if (clip == candidate) GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
                string tag = clip.name.Contains("_RM") ? "  [RM]" : "";
                if (GUILayout.Button($"{clip.name}   {clip.length:F2}s{tag}", EditorStyles.miniButton))
                {
                    candidate = clip;
                    loopSetting = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                    Play(clip);
                }
                GUI.backgroundColor = prev;
            }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawTarget()
    {
        EditorGUILayout.LabelField("적용 대상", EditorStyles.boldLabel);
        int newIndex = EditorGUILayout.Popup("컨트롤러", controllerIndex, ControllerLabels);
        if (newIndex != controllerIndex)
        {
            controllerIndex = newIndex;
            EditorPrefs.SetInt("AnimPicker.Controller", controllerIndex);
            LoadController();
        }
        if (controller == null)
        {
            EditorGUILayout.HelpBox($"{ControllerPaths[controllerIndex]}가 없습니다.", MessageType.Warning);
            return;
        }

        stateScroll = EditorGUILayout.BeginScrollView(stateScroll, GUILayout.Height(170f));
        foreach (AnimatorState state in controller.layers[0].stateMachine.states.Select(s => s.state).OrderBy(s => s.name))
        {
            Color prev = GUI.backgroundColor;
            if (state == targetState) GUI.backgroundColor = new Color(1f, 0.85f, 0.45f);
            string motion = state.motion is BlendTree ? "블렌드 트리" : state.motion != null ? state.motion.name : "(비어 있음)";
            if (GUILayout.Button($"{state.name}   ←  {motion}", EditorStyles.miniButton))
            {
                targetState = state;
                CollectLeaves();
                blendChild = leaves.Count > 0 ? 0 : -1;
            }
            GUI.backgroundColor = prev;
        }
        EditorGUILayout.EndScrollView();

        // 로코모션 같은 블렌드 트리는 칸 하나를 골라서 바꿈
        if (leaves.Count > 0)
        {
            string[] labels = leaves.Select(l => l.label).ToArray();
            blendChild = Mathf.Clamp(EditorGUILayout.Popup("바꿀 칸", blendChild, labels), 0, labels.Length - 1);
        }

        var attacks = RelatedAttacks().ToList();
        if (attacks.Count > 0)
            EditorGUILayout.HelpBox("같이 바뀌는 공격 데이터: " + string.Join(", ", attacks.Select(a => a.name)), MessageType.None);
    }

    void DrawPreview()
    {
        EditorGUILayout.LabelField("미리보기", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        previewTarget = (Animator)EditorGUILayout.ObjectField(previewTarget, typeof(Animator), true);
        if (GUILayout.Button("플레이어", GUILayout.Width(60f))) previewTarget = FindPreviewTarget(0);
        if (GUILayout.Button("적", GUILayout.Width(36f))) previewTarget = FindPreviewTarget(1);
        EditorGUILayout.EndHorizontal();
        if (previewTarget == null)
        {
            EditorGUILayout.HelpBox("씬에서 재생해 볼 캐릭터(Animator)를 지정하세요.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        AnimationClip current = CurrentClip();
        GUI.enabled = current != null;
        if (GUILayout.Button($"▶ 현재  {(current != null ? current.name : "-")}")) Play(current);
        GUI.enabled = candidate != null;
        if (GUILayout.Button($"▶ 후보  {(candidate != null ? candidate.name : "-")}")) Play(candidate);
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (playing == null) return;
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(isPlaying ? "일시정지" : "재생", GUILayout.Width(70f)))
        {
            isPlaying = !isPlaying;
            lastTick = EditorApplication.timeSinceStartup;
        }
        if (GUILayout.Button("멈춤", GUILayout.Width(50f))) StopPreview();
        if (GUILayout.Button("씬 뷰에서 보기"))
        {
            Selection.activeGameObject = previewTarget.gameObject;
            SceneView.FrameLastActiveSceneView();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        time = EditorGUILayout.Slider($"{playing.name}", time, 0f, playing.length);
        if (EditorGUI.EndChangeCheck())
        {
            isPlaying = false;
            Sample();
        }
        speed = EditorGUILayout.Slider("속도", speed, 0.1f, 2f);
    }

    void DrawApply()
    {
        EditorGUILayout.LabelField("적용", EditorStyles.boldLabel);
        loopSetting = EditorGUILayout.Toggle("반복 재생 (루프)", loopSetting);
        refit = EditorGUILayout.Toggle("판정 구간 다시 측정", refit);

        bool ready = candidate != null && targetState != null;
        GUI.enabled = ready;
        string label = ready ? $"'{candidate.name}' → '{targetState.name}'에 적용" : "후보 클립과 대상 상태를 고르세요";
        if (GUILayout.Button(label, GUILayout.Height(30f))) Apply();
        GUI.enabled = true;
        EditorGUILayout.HelpBox("공격 상태는 보통 루프 끔, 걷기·달리기·가드 자세는 루프 켬. Ctrl+Z로 되돌릴 수 있습니다.", MessageType.None);
    }

    // ===== 미리보기 =====
    void Play(AnimationClip clip)
    {
        if (clip == null) return;
        if (previewTarget == null) previewTarget = FindPreviewTarget(controllerIndex);
        if (previewTarget == null) return;

        playing = clip;
        time = 0f;
        isPlaying = true;
        lastTick = EditorApplication.timeSinceStartup;
        if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
        Sample();
    }

    void Tick()
    {
        if (!isPlaying || playing == null) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || previewTarget == null)
        {
            StopPreview();
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        time += (float)(now - lastTick) * speed;
        lastTick = now;
        if (time > playing.length) time = playing.length > 0f ? time % playing.length : 0f; // 반복해서 보여줌
        Sample();
        Repaint();
    }

    // 에디트 모드에서 씬 캐릭터에 포즈를 입힘. 멈추면 원래 포즈로 돌아감
    void Sample()
    {
        if (previewTarget == null || playing == null || !AnimationMode.InAnimationMode()) return;
        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(previewTarget.gameObject, playing, time);
        AnimationMode.EndSampling();
        SceneView.RepaintAll();
    }

    void StopPreview()
    {
        isPlaying = false;
        playing = null;
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        SceneView.RepaintAll();
    }

    // ===== 적용 =====
    void Apply()
    {
        StopPreview();
        AnimationClip old = CurrentClip();
        var attacks = RelatedAttacks().ToList(); // 바꾸기 전 클립 기준으로 찾아 둠

        if (targetState.motion is BlendTree && blendChild >= 0 && blendChild < leaves.Count)
        {
            BlendTree tree = leaves[blendChild].tree;
            Undo.RecordObject(tree, "Apply Animation");
            ChildMotion[] children = tree.children;
            children[leaves[blendChild].index].motion = candidate;
            tree.children = children;
            EditorUtility.SetDirty(tree);
            CollectLeaves();
        }
        else
        {
            Undo.RecordObject(targetState, "Apply Animation");
            targetState.motion = candidate;
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(candidate);
        if (settings.loopTime != loopSetting)
        {
            settings.loopTime = loopSetting;
            AnimationUtility.SetAnimationClipSettings(candidate, settings);
            EditorUtility.SetDirty(candidate);
        }

        // 공격은 AttackData의 클립 길이로 판정 시간을 계산하므로 상태와 같은 클립이어야 함
        foreach (AttackData attack in attacks)
        {
            var so = new SerializedObject(attack);
            so.FindProperty("clip").objectReferenceValue = candidate;
            so.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPaths[controllerIndex]);
        if (refit && attacks.Count > 0) EditorApplication.ExecuteMenuItem("Tools/Setup/Fit Attack Hit Windows");

        Debug.Log($"[AnimPicker] {targetState.name}: {(old != null ? old.name : "-")} → {candidate.name}" +
                  (attacks.Count > 0 ? $" · 공격 데이터 {attacks.Count}개 갱신" : ""));
    }

    // 같은 상태 이름을 쓰고, 지금 상태의 클립을 들고 있는 AttackData (적 공용 상태는 여러 데이터가 함께 씀)
    IEnumerable<AttackData> RelatedAttacks()
    {
        if (targetState == null || targetState.motion is BlendTree) yield break;
        AnimationClip current = targetState.motion as AnimationClip;
        foreach (string guid in AssetDatabase.FindAssets("t:AttackData"))
        {
            var attack = AssetDatabase.LoadAssetAtPath<AttackData>(AssetDatabase.GUIDToAssetPath(guid));
            if (attack == null) continue;
            var so = new SerializedObject(attack);
            if (so.FindProperty("stateName").stringValue != targetState.name) continue;
            if (attack.Clip == current || attack.Clip == null) yield return attack;
        }
    }

    AnimationClip CurrentClip()
    {
        if (targetState == null) return null;
        if (targetState.motion is BlendTree)
            return blendChild >= 0 && blendChild < leaves.Count ? leaves[blendChild].tree.children[leaves[blendChild].index].motion as AnimationClip : null;
        return targetState.motion as AnimationClip;
    }

    // 클립이 들어 있는 칸만 펼침. 이름 예: "Idle / Stance 1"
    void CollectLeaves()
    {
        leaves.Clear();
        if (targetState != null && targetState.motion is BlendTree tree) CollectLeaves(tree, "");
    }

    void CollectLeaves(BlendTree tree, string prefix)
    {
        ChildMotion[] children = tree.children;
        for (int i = 0; i < children.Length; i++)
        {
            string here = tree.blendType == BlendTreeType.Simple1D
                ? $"{prefix}{tree.blendParameter} {children[i].threshold:0.#}"
                : $"{prefix}{DirectionName(children[i].position)}"; // [변경] 2D 블렌드는 방향 이름으로
            if (children[i].motion is BlendTree sub)
            {
                CollectLeaves(sub, $"{prefix}{sub.name} / ");
                continue;
            }
            string clip = children[i].motion != null ? children[i].motion.name : "비어 있음";
            leaves.Add((tree, i, $"{here}: {clip}"));
        }
    }

    static string DirectionName(Vector2 p)
    {
        if (p.sqrMagnitude < 0.01f) return "제자리";
        if (Mathf.Abs(p.y) >= Mathf.Abs(p.x)) return p.y > 0f ? "앞" : "뒤";
        return p.x > 0f ? "오른쪽" : "왼쪽";
    }

    // ===== 준비 =====
    void RefreshClips()
    {
        clips.Clear();
        if (!AssetDatabase.IsValidFolder(root)) return;
        clips.AddRange(AssetDatabase.FindAssets("t:AnimationClip", new[] { root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .OrderBy(p => p)
            .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
            .Where(c => !c.name.StartsWith("__preview__")));
    }

    void LoadController()
    {
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPaths[controllerIndex]);
        targetState = null;
        blendChild = -1;
        leaves.Clear();
        previewTarget = FindPreviewTarget(controllerIndex);
    }

    static Animator FindPreviewTarget(int index)
    {
        if (index == 0)
        {
            var combat = Object.FindAnyObjectByType<PlayerCombat>();
            return combat != null ? combat.GetComponentInChildren<Animator>() : null;
        }
        var enemy = Object.FindAnyObjectByType<EnemySwordsman>();
        return enemy != null ? enemy.GetComponentInChildren<Animator>() : null;
    }

    string FolderOf(AnimationClip clip)
    {
        string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip))?.Replace('\\', '/') ?? "";
        return dir.StartsWith(root) ? dir.Substring(root.Length).TrimStart('/') : dir;
    }
}
