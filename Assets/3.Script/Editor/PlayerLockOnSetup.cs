using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 락온: PlayerLockOn 컴포넌트 + HUD 락온 표시 + 전투 걷기·달리기 8방향(옆걸음·뒷걸음) 블렌드
// 걷기·달리기 칸의 전투(Stance 1) 클립을 MoveX/MoveZ 2D 트리로 바꿈. 앞 방향은 지금 쓰는 클립 그대로
public static class PlayerLockOnSetup
{
    const string AnimRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";

    // 방향 접미사, 블렌드 위치 (x = 오른쪽, y = 앞)
    static readonly (string suffix, Vector2 pos)[] Directions =
    {
        ("F_L_45", new Vector2(-0.707f, 0.707f)), ("F_R_45", new Vector2(0.707f, 0.707f)),
        ("F_L_90", new Vector2(-1f, 0f)), ("F_R_90", new Vector2(1f, 0f)),
        ("B_180", new Vector2(0f, -1f)), ("B_L_45", new Vector2(-0.707f, -0.707f)), ("B_R_45", new Vector2(0.707f, -0.707f)),
    };

    [MenuItem("Tools/Setup/Player Lock On")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[LockOnSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (locomotion == null || controller == null)
        {
            Debug.LogError("[LockOnSetup] 씬의 PlayerLocomotion 또는 Player.controller가 없습니다.");
            return;
        }

        // 1) 컴포넌트 연결
        GameObject player = locomotion.gameObject;
        var lockOn = player.GetComponent<PlayerLockOn>();
        if (lockOn == null) lockOn = Undo.AddComponent<PlayerLockOn>(player); // 에디터의 GetComponent는 가짜 null이라 ?? 대신
        SetRef(lockOn, "followCamera", Object.FindAnyObjectByType<ThirdPersonCamera>());
        SetRef(lockOn, "locomotion", locomotion);
        SetRef(lockOn, "health", player.GetComponent<PlayerHealth>());
        SetRef(player.GetComponent<PlayerTargeting>(), "lockOn", lockOn);
        SetRef(player.GetComponent<PlayerStance>(), "lockOn", lockOn);

        // 2) 락온 표시 (HUD 캔버스 맨 뒤)
        var hud = Object.FindAnyObjectByType<GameHUD>();
        Canvas canvas = hud != null ? hud.GetComponentInParent<Canvas>() : Object.FindAnyObjectByType<Canvas>();
        if (canvas != null && Object.FindAnyObjectByType<LockOnMarker>() == null)
        {
            var go = new GameObject("LockOnMarker", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Lock On Marker");
            var rt = (RectTransform)go.transform;
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsFirstSibling();
            SetRef(go.AddComponent<LockOnMarker>(), "lockOn", lockOn);
        }
        else if (canvas == null) Debug.LogWarning("[LockOnSetup] HUD 캔버스를 찾지 못해 락온 표시를 만들지 않았습니다.");

        // 3) 걷기(칸 1)·달리기(칸 2)의 전투 클립을 8방향으로
        int changed = 0;
        AnimatorState loco = controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Locomotion");
        if (loco != null && loco.motion is BlendTree top && controller.parameters.Any(p => p.name == "MoveX"))
        {
            ChildMotion[] slots = top.children.OrderBy(c => c.threshold).ToArray();
            for (int i = 1; i <= 2 && i < slots.Length; i++)
            {
                if (!(slots[i].motion is BlendTree stance) || stance.blendParameter != "Stance") continue;
                ChildMotion[] children = stance.children;
                for (int c = 0; c < children.Length; c++)
                {
                    if (children[c].threshold < 0.5f || !(children[c].motion is AnimationClip forward)) continue; // 전투 칸만, 이미 나뉘었으면 건너뜀
                    BlendTree tree = Directional(controller, stance.name + "_Combat8", forward);
                    if (tree == null) continue;
                    children[c].motion = tree;
                    changed++;
                }
                stance.children = children;
                EditorUtility.SetDirty(stance);
            }
        }
        else Debug.LogWarning("[LockOnSetup] Locomotion 트리 또는 MoveX 파라미터가 없어 8방향을 건너뜁니다. (Player Stance → Player Directional 셋업 먼저)");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        EditorSceneManager.MarkSceneDirty(player.scene);
        Debug.Log($"[LockOnSetup] 완료 · PlayerLockOn, 락온 표시, 8방향 트리 {changed}개");
    }

    // 앞 클립 이름(…_F_0_RM)에서 나머지 방향 클립 이름을 만들어 찾음
    static BlendTree Directional(AnimatorController controller, string name, AnimationClip forward)
    {
        if (!forward.name.Contains("_F_0"))
        {
            Debug.LogWarning($"[LockOnSetup] {forward.name}은 방향 클립 이름 규칙(_F_0)이 아니라 건너뜁니다.");
            return null;
        }

        var tree = new BlendTree
        {
            name = name,
            blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX",
            blendParameterY = "MoveZ",
            useAutomaticThresholds = false,
            hideFlags = HideFlags.HideInHierarchy
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(forward, new Vector2(0f, 1f));

        foreach (var (suffix, pos) in Directions)
        {
            AnimationClip clip = FindClip(forward.name.Replace("_F_0", "_" + suffix));
            if (clip == null)
            {
                Debug.LogWarning($"[LockOnSetup] {forward.name.Replace("_F_0", "_" + suffix)}를 찾지 못해 그 방향은 비워 둡니다.");
                continue;
            }
            SetLoop(clip);
            tree.AddChild(clip, pos);
        }
        return tree;
    }

    static AnimationClip FindClip(string clipName)
    {
        return AssetDatabase.FindAssets(clipName + " t:AnimationClip", new[] { AnimRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
            .FirstOrDefault(c => c != null && c.name == clipName);
    }

    static void SetLoop(AnimationClip clip)
    {
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime) return;
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
    }

    static void SetRef(Object target, string field, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        SerializedProperty p = so.FindProperty(field);
        if (p == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
    }
}
