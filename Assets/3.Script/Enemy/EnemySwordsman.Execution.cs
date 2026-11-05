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
    float executionWindow = 1f;
    bool executed;
    // 처형 자리 맞춤: alignTime 동안 현재 자리 → 지정 자리로 미끄러짐. 짝 모션 배속
    float executedSpeed = 1f;
    float alignDuration, alignElapsed;
    Vector3 alignFrom, alignTo;
    Quaternion alignFromRot, alignToRot;

    public bool IsExecutable => state == State.Stun && localTime < executableUntil && health > 0f;
    public bool IsBeingExecuted => state == State.Executed;
    // 처형 기회 남은 비율 1 → 0 (UI 고리)
    public float ExecutionRemaining01 => IsExecutable ? Mathf.Clamp01((executableUntil - localTime) / executionWindow) : 0f;

    public void OpenExecution(float seconds)
    {
        if (localTime + seconds <= executableUntil) return;
        executableUntil = localTime + seconds;
        executionWindow = Mathf.Max(seconds, 0.01f);
    }
    public void CloseExecution()
    {
        if (IsBoss && postureBroken) return; // [변경] 보스 체간 붕괴는 간파 뒤에도 처형 기회 유지
        executableUntil = -1f;
    }

    public bool HasExecutedMotion(int index) =>
        animator != null && index >= 0 && index < ExecutedHashes.Length && animator.HasState(0, ExecutedHashes[index]);

    // 핵심: 플레이어 앞 지정 위치·방향으로 짧게 미끄러지며 짝 모션. 피해는 플레이어 모션의 타격 순간에 InstantKill로 들어옴
    // 순간 이동 대신 alignTime 동안 보간 · 배속 인자 · 처형 동안 CharacterController를 꺼서 밀림 없이 제자리 유지
    public void BeginExecuted(Vector3 position, Quaternion rotation, int index, float speed, float alignTime)
    {
        if (swingActive) EndSwing();
        currentAttack = null;
        knockbackTimer = 0f;
        moveSpeed = 0f;
        executableUntil = -1f;
        executed = true;
        executedSpeed = Mathf.Max(0.05f, speed);

        controller.enabled = false; // 부활(Respawn) 때 다시 켜짐
        alignFrom = transform.position;
        alignFromRot = transform.rotation;
        alignTo = position;
        alignToRot = rotation;
        alignDuration = Mathf.Max(0f, alignTime);
        alignElapsed = 0f;
        if (alignDuration <= 0f) transform.SetPositionAndRotation(position, rotation);

        if (HasExecutedMotion(index)) animator.CrossFadeInFixedTime(ExecutedHashes[index], 0.05f, 0, 0f);
        SetState(State.Executed);
        Log("처형당함");
    }

    void UpdateExecuted(float dt)
    {
        moveSpeed = 0f;
        if (BossExecutedFinished()) // [변경] 보스는 처형으로 죽지 않았으면 짝 모션이 끝난 뒤 일어남
        {
            BossGetUp();
            return;
        }
        if (alignElapsed >= alignDuration) return;
        alignElapsed += dt;
        float k = Mathf.Clamp01(alignElapsed / alignDuration);
        k = 1f - (1f - k) * (1f - k);
        transform.SetPositionAndRotation(Vector3.Lerp(alignFrom, alignTo, k), Quaternion.Slerp(alignFromRot, alignToRot, k));
    }
}
