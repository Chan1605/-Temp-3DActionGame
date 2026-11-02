using UnityEngine;

// 저스트 회피·간파가 "곧 들어올 공격"을 판단할 때 쓰는 적 쪽 정보
public interface IAttackThreat
{
    // [변경] 판정 시작 earlyWindow초 전 ~ 판정 시작 후 lateWindow초 사이(실제 시간)면 true
    bool IsAboutToHit(Vector3 targetPosition, float earlyWindow, float lateWindow);
    bool IsParryable { get; }
    HitResult ReceiveParry(GameObject parrier);
}
