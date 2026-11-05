using UnityEngine;

// 처형 당하는 쪽: 가드 붕괴 · 돌격 후 탈진 · 패링당한 경직 동안 "처형 가능" 창이 열림
// 플레이어(PlayerExecution)가 처형을 시작하면 정해진 자리로 옮겨 짝 모션(Execution_Target)을 재생하고, 죽어도 사망 모션으로 바꾸지 않음
public partial class EnemySwordsman
{
    static readonly int[] ExecutedHashes =
    {
        Animator.StringToHash("Executed_1"), Animator.StringToHash("Executed_2"), Animator.StringToHash("Executed_3")
    };

    float executableUntil = -1f;
    bool executed;

    public bool IsExecutable => state == State.Stun && localTime < executableUntil && health > 0f;
    public bool IsBeingExecuted => state == State.Executed;

    public void OpenExecution(float seconds) => executableUntil = Mathf.Max(executableUntil, localTime + seconds);
    public void CloseExecution() => executableUntil = -1f;

    public bool HasExecutedMotion(int index) =>
        animator != null && index >= 0 && index < ExecutedHashes.Length && animator.HasState(0, ExecutedHashes[index]);

    // 핵심: 플레이어 앞 지정 위치·방향으로 순간 이동 → 짝 모션. 피해는 플레이어 모션의 타격 순간에 InstantKill로 들어옴
    public void BeginExecuted(Vector3 position, Quaternion rotation, int index)
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        knockbackTimer = 0f;
        moveSpeed = 0f;
        executableUntil = -1f;
        executed = true;

        bool wasEnabled = controller.enabled;
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = wasEnabled;

        if (HasExecutedMotion(index)) animator.CrossFadeInFixedTime(ExecutedHashes[index], 0.05f, 0, 0f);
        SetState(State.Executed);
        Log("처형당함");
    }
}
