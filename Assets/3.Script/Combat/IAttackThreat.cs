using UnityEngine;

// 저스트 회피·간파가 "곧 들어올 공격"을 판단할 때 쓰는 적 쪽 정보
public interface IAttackThreat
{
    // 판정 시작 earlyWindow초 전 ~ 판정 시작 후 lateWindow초 사이(실제 시간)면 true
    bool IsAboutToHit(Vector3 targetPosition, float earlyWindow, float lateWindow);
    // [변경] 간파 v2: 판정 시작까지 남은 실제 시간(초). 0이면 판정 중, -1이면 들어올 공격 없음 → 부딪히는 순간을 맞추는 데 사용
    float SecondsUntilHit();
    bool IsParryable { get; }
    HitResult ReceiveParry(GameObject parrier);
}
