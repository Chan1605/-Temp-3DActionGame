using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 플레이어 피격 반응 (Player.controller)
//  - Hit_F/B/L/R: 맞은 방향 · HitLarge_*: 강공격 · HitAir_*: 공중에서 맞음
//  - KnockDown_Start → KnockDown_Loop(누워 있음) → Get_Up(PlayerHealth가 시간 맞춰 재생)
// + 돌격병 내려베기(Charger_Strike)에 넉다운 표시. 다시 실행하면 비어 있는 상태만 채움
public static class PlayerHitSetup
{
    const string HitRoot = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/08_Hit/";
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string ChargerStrikePath = "Assets/11.Data/Enemy/Charger_Strike.asset";
    static readonly string[] Dirs = { "F", "B", "L", "R" };

    [MenuItem("Tools/Setup/Player Hit Reactions")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[HitSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[HitSetup] Player.controller가 없습니다.");
            return;
        }

        // 상태, 클립, 루프
        var states = Dirs.SelectMany(d => new[]
        {
            ($"Hit_{d}", HitRoot + $"02_Hit_Combat/Hit_Combat_{d}.anim", false),
            ($"HitLarge_{d}", HitRoot + $"06_Hit_Large_Combat/Hit_Large_Combat_{d}.anim", false),
            ($"HitAir_{d}", HitRoot + $"04_Hit_Combat_Air/Hit_Combat_Air_{d}.anim", false),
        }).Concat(new[]
        {
            ("KnockDown_Start", HitRoot + "11_Knock_Down_Combat/Knock_Down_Combat_Start.anim", false),
            ("KnockDown_Loop", HitRoot + "11_Knock_Down_Combat/Knock_Down_Combat_Loop.anim", true),
            ("Get_Up", HitRoot + "09_Get_Up/Get_Up_Combat.anim", false),
        }).ToArray();

        string missing = states.Select(s => s.Item2).FirstOrDefault(p => AssetDatabase.LoadAssetAtPath<AnimationClip>(p) == null);
        if (missing != null)
        {
            Debug.LogError($"[HitSetup] {missing}를 찾지 못했습니다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        int added = 0;
        foreach (var (name, path, loop) in states)
        {
            AnimatorState state = Find(sm, name);
            if (state == null)
            {
                state = sm.AddState(name);
                added++;
            }
            if (state.motion == null) state.motion = Clip(path, loop); // 애니메이션 고르기 창에서 바꾼 클립은 보존
        }

        // 넘어지는 모션이 끝나면 누운 채 대기 (기상은 코드가 시간 맞춰 전환)
        AnimatorState start = Find(sm, "KnockDown_Start"), lying = Find(sm, "KnockDown_Loop");
        if (!start.transitions.Any(t => t.destinationState == lying))
        {
            AnimatorStateTransition t = start.AddTransition(lying);
            t.hasExitTime = true;
            t.exitTime = 0.95f;
            t.duration = 0.1f;
            t.hasFixedDuration = true;
        }
        EditorUtility.SetDirty(controller);

        var strike = AssetDatabase.LoadAssetAtPath<AttackData>(ChargerStrikePath);
        if (strike != null)
        {
            var so = new SerializedObject(strike);
            so.FindProperty("knockDown").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        AssetDatabase.SaveAssets();
        PlayerClipFixer.Fix(ControllerPath);
        Debug.Log($"[HitSetup] 완료 · 피격 상태 {added}개 추가{(strike != null ? " · 돌격병 내려베기 = 넉다운" : "")}");
    }

    static AnimatorState Find(AnimatorStateMachine sm, string name) =>
        sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

    static AnimationClip Clip(string path, bool loop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        if (settings.loopTime != loop)
        {
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
        return clip;
    }
}
