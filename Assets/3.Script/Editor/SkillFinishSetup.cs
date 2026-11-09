using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 벽력일섬 잔심 자세: 마지막 돌진 뒤 Skill_Finish 상태를 재생하고 납도
// 기본 클립은 발도 자세(Power_Attack_Start·Loop)와 짝인 Power_Attack_End
// 다른 클립을 비교하려면 Tools/Animation/Animation Picker에서 Skill_Finish 상태를 골라 미리보기·적용
public static class SkillFinishSetup
{
    const string ControllerPath = "Assets/5.Animation/Player/Player.controller";
    const string ClipPath = "Assets/ThirdParty/SwordAnimationPack/Animation/Humanoid/02_Attack/16_Power_Attack/Power_Attack_End.anim";
    const string DataPath = "Assets/11.Data/Skill/ThunderFlash.asset";

    [MenuItem("Tools/Setup/Skill Finish Pose")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[SkillFinishSetup] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var data = AssetDatabase.LoadAssetAtPath<SkillData>(DataPath);
        if (controller == null || clip == null || data == null)
        {
            Debug.LogError("[SkillFinishSetup] Player.controller · Power_Attack_End 클립 · ThunderFlash 데이터 중 없는 것이 있습니다.");
            return;
        }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == data.FinishState);
        bool created = state == null;
        if (created) state = sm.AddState(data.FinishState);

        // 이미 있으면 클립은 건드리지 않음 (Animation Picker로 바꾼 값 보존)
        if (state.motion == null) state.motion = clip;
        EditorUtility.SetDirty(controller);

        // 처음 만들 때만: 클립 길이에 맞춰 보여줄 시간 (끝부분은 납도 모션이 이어받음)
        if (created)
        {
            var so = new SerializedObject(data);
            so.FindProperty("finishDuration").floatValue = Mathf.Clamp(((AnimationClip)state.motion).length * 0.75f, 0.4f, 1.2f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SkillFinishSetup] 완료 · {data.FinishState} = {state.motion.name}, 보여줄 시간 {data.FinishDuration:0.00}초");
    }
}
