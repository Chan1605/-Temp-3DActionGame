using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerClipFixer
{
    static readonly string[] ControllerPaths =
    {
        "Assets/5.Animation/Player/Player.controller",
        "Assets/5.Animation/Enemy/Swordsman.controller"
    };

    [MenuItem("Tools/Setup/Fix Player Clips")]
    static void Run()
    {
        foreach (string path in ControllerPaths)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) Fix(path);
        }
    }

    public static void Fix(string controllerPath)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogError($"[ClipFixer] {controllerPath}가 없습니다.");
            return;
        }

        var clips = new HashSet<AnimationClip>();
        foreach (AnimatorControllerLayer layer in controller.layers)
            CollectFromStateMachine(layer.stateMachine, clips);

        var paths = new List<string>();
        foreach (AnimationClip clip in clips)
        {
            string path = AssetDatabase.GetAssetPath(clip);

            // FBX 서브 에셋은 임포터가 관리하므로 대상 아님, 독립 .anim만 다시 저장
            if (!path.EndsWith(".anim")) continue;

            // 같은 설정을 다시 넣으면 클립 내부 데이터가 재구성되고 dirty 표시됨
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            paths.Add(path);
        }

        AssetDatabase.SaveAssets();

        // 디스크에 새 형식으로 다시 기록 → 비어 있던 m_ClipBindingConstant가 채워짐
        if (paths.Count > 0) AssetDatabase.ForceReserializeAssets(paths);
        AssetDatabase.Refresh();

        Debug.Log($"[ClipFixer] {controller.name} · 클립 {clips.Count}개 중 .anim {paths.Count}개 재저장 완료");
    }

    static void CollectFromStateMachine(AnimatorStateMachine machine, HashSet<AnimationClip> clips)
    {
        foreach (ChildAnimatorState child in machine.states)
            CollectFromMotion(child.state.motion, clips);

        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
            CollectFromStateMachine(sub.stateMachine, clips);
    }

    static void CollectFromMotion(Motion motion, HashSet<AnimationClip> clips)
    {
        if (motion is AnimationClip clip)
        {
            clips.Add(clip);
        }
        else if (motion is BlendTree tree)
        {
            foreach (ChildMotion child in tree.children)
                CollectFromMotion(child.motion, clips);
        }
    }
}
