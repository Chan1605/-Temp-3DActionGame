using UnityEngine;

// 옛 밸런스 값 보관용 (인스펙터에 안 보임, 코드에서도 안 씀)
// 예전에 PlayerSkill 컴포넌트에서 맞춰둔 값을 Tools/Setup/Move Skill Balance To Data가 SkillData로 옮길 때만 읽음
// 옮긴 뒤엔 이 파일을 지워도 됨
#pragma warning disable 0414
public partial class PlayerSkill
{
    [SerializeField, HideInInspector] float tapChainCost = 0.5f;
    [SerializeField, HideInInspector] float stanceCost = 2f;
    [SerializeField, HideInInspector] float stanceEnterTime = 0.25f;
    [SerializeField, HideInInspector] float stanceDuration = 2.5f;
    [SerializeField, HideInInspector] float stanceEnemyScale = 0.05f;
    [SerializeField, HideInInspector] float stanceRange = 20f;
    [SerializeField, HideInInspector] float markTime = 0.2f;
    [SerializeField, HideInInspector] float reticleRadius = 0.08f;
    [SerializeField, HideInInspector] int maxMarks = 6;
    [SerializeField, HideInInspector] int baseMarks = 3;
    [SerializeField, HideInInspector] int enchantMaxMarks = 0;
    [SerializeField, HideInInspector] float stanceChainDelay = 0.05f;
    [SerializeField, HideInInspector] float dodgeStanceWindow = 0.9f;
    [SerializeField, HideInInspector] float dodgeStanceCost = 0f;
    [SerializeField, HideInInspector] int dodgeAutoMarks = 3;
    [SerializeField, HideInInspector] float dodgeAutoRange = 12f;
    [SerializeField, HideInInspector] float dodgeDrawDelay = 0.12f;
    [SerializeField, HideInInspector] float enchantStanceSeconds = 3.5f;
    [SerializeField, HideInInspector] float freezeTime = 0.08f;
    [SerializeField, HideInInspector] float lastFreezeTime = 0.14f;
    [SerializeField, HideInInspector] float freezeWorldScale = 0.02f;
    [SerializeField, HideInInspector] float freezeBudget = 0.6f;
    [SerializeField, HideInInspector] float holdFreezeScale = 1f;
    [SerializeField, HideInInspector] float enchantFreezeScale = 0.7f;
    [SerializeField, HideInInspector] float dodgeFreezeScale = 0.6f;
    [SerializeField, HideInInspector] float tapFreezeScale = 0f;
    [SerializeField, HideInInspector] float releaseShake = 0.12f;
    [SerializeField, HideInInspector] float lastFreezePunch = 0.3f;
}
#pragma warning restore 0414
