using System.Text;
using UnityEditor;
using UnityEngine;

public static class AnimatorReport
{
    [MenuItem("Tools/Debug/Player Animator Report")]
    static void Run()
    {
        var locomotion = Object.FindAnyObjectByType<PlayerLocomotion>();
        if (locomotion == null)
        {
            Debug.LogError("[AnimatorReport] 씬에 PlayerLocomotion이 없습니다.");
            return;
        }

        var animator = locomotion.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            Debug.LogError("[AnimatorReport] Player 아래에 Animator가 없습니다.");
            return;
        }

        var sb = new StringBuilder("[AnimatorReport]\n");
        sb.AppendLine($"Animator 오브젝트: {animator.gameObject.name}");
        sb.AppendLine($"Controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "없음")}");
        sb.AppendLine($"applyRootMotion: {animator.applyRootMotion}");

        Avatar avatar = animator.avatar;
        if (avatar == null) sb.AppendLine("Avatar: 없음");
        else sb.AppendLine($"Avatar: {avatar.name}  valid={avatar.isValid}  human={avatar.isHuman}");

        if (Application.isPlaying)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            sb.AppendLine($"현재 상태 해시: {state.fullPathHash}  normalizedTime={state.normalizedTime:F2}");

            foreach (AnimatorClipInfo info in animator.GetCurrentAnimatorClipInfo(0))
                sb.AppendLine($"  클립: {info.clip.name}  weight={info.weight:F2}  length={info.clip.length:F2}s");

            if (animator.isHuman) sb.AppendLine($"humanScale: {animator.humanScale:F3}  bodyPosition.y: {animator.bodyPosition.y:F3}");
        }
        else
        {
            sb.AppendLine("(현재 클립·bodyPosition은 플레이 모드에서만 기록)");
        }

        // 골반이 루트보다 너무 낮으면(0 근처 이하) 몸이 땅에 박힌 상태
        if (animator.isHuman)
        {
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                float height = hips.position.y - locomotion.transform.position.y;
                sb.AppendLine($"골반 높이(루트 기준): {height:F3}m");
            }
        }

        Debug.Log(sb.ToString(), animator);
    }
}
