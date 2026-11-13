using UnityEngine;

// 손에 든 칼 겉모습 조정: 잡는 자리 · 칼날 길이 · 기울기 (판정 박스는 건드리지 않음)
// 칼 겉모습 오브젝트(SamuraiKatana 또는 ZenitsuKatana)에 붙임. 원점 = 날밑, +Y = 칼날 방향
// 플레이 중 Inspector 슬라이더로 맞추고 "Player 프리팹에 저장" 버튼으로 값만 저장 (Tools/Setup/Katana/Add Grip Tuner)
public class KatanaGrip : MonoBehaviour
{
    [SerializeField, Range(-0.4f, 0.4f)] float grip = 0f;          // + 칼이 칼끝 쪽으로 밀림 / - 손잡이 쪽으로 (손 위치는 그대로)
    [SerializeField, Range(0.7f, 1.6f)] float bladeLength = 1f;    // 칼 축(Y) 방향으로만 늘림 → 두께는 그대로
    [SerializeField, Range(-40f, 40f)] float tiltForward = 0f;      // 칼끝을 앞뒤로
    [SerializeField, Range(-40f, 40f)] float tiltSide = 0f;         // 칼끝을 좌우로
    [SerializeField, Range(-180f, 180f)] float twist = 0f;          // 칼날 면 돌리기

    Vector3 basePosition, baseScale;
    Quaternion baseRotation;
    bool captured;

    void Awake()
    {
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        baseScale = transform.localScale;
        captured = true;
        Apply();
    }

    // 프리팹에 저장된 원래 자세를 기준으로 매번 다시 계산 (값이 누적되지 않음)
    void Apply()
    {
        if (!captured) return;
        Quaternion rotation = baseRotation * Quaternion.Euler(tiltForward, twist, tiltSide);
        transform.localRotation = rotation;
        transform.localPosition = basePosition + rotation * Vector3.up * grip;
        transform.localScale = new Vector3(baseScale.x, baseScale.y * bladeLength, baseScale.z);
    }

    void OnValidate() => Apply(); // 플레이 중 슬라이더를 움직이면 바로 반영

    public void CopyValuesTo(KatanaGrip other)
    {
        other.grip = grip;
        other.bladeLength = bladeLength;
        other.tiltForward = tiltForward;
        other.tiltSide = tiltSide;
        other.twist = twist;
    }
}
