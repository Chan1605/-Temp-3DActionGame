using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;


[CreateAssetMenu(menuName = "Game/Skill Tree Data", fileName = "SkillTree")]
public class SkillTreeData : ScriptableObject
{
    [Serializable]
    public class Node
    {
        public SkillId id;
        public string displayName;
        [TextArea(2, 4)] public string description;
        public int cost = 1;
        public int tab;              // [변경] 0 기술 · 1 수련(패시브)
        public int branch;           // 기술: 0 일섬 · 1 검술 · 2 방어 / 수련: 0 체력 · 1 뇌기
        public SkillId[] requires = Array.Empty<SkillId>();
        public VideoClip preview;
    }

    public static readonly string[] TabNames = { "기술", "수련" }; // [변경]
    public static readonly string[][] BranchNames = { new[] { "일섬", "검술", "방어" }, new[] { "체력", "뇌기" } };

    public int startPoints = 1;
    public int pointsPerLevel = 1;
    public List<Node> nodes = Defaults();

    public static List<Node> Defaults() => new List<Node>
    {
        N(SkillId.ChainStrike1, "연격 Ⅰ", "탭 Q 돌진 뒤 착지하자마자 Q를 다시 누르면 가까운 적에게 이어서 벤다. (2연)\n이어 벨 때마다 뇌기 0.5칸", 1, 0),
        N(SkillId.ChainStrike2, "연격 Ⅱ", "연격을 3연까지 잇는다.", 2, 0, SkillId.ChainStrike1),
        N(SkillId.ChainStrike3, "연격 Ⅲ", "연격을 4연까지 잇는다.", 2, 0, SkillId.ChainStrike2),
        N(SkillId.StanceMarks, "표식 확장", "발도 자세에서 찍을 수 있는 표식이 3명에서 6명으로 늘어난다.", 1, 0),
        N(SkillId.EnchantDraw, "각성 발도", "각성(R) 중 Q: 각성 시간을 떼어 그동안 비용 없이 발도 자세를 연달아 쓴다.", 2, 0, SkillId.StanceMarks),
        N(SkillId.Launcher, "띄우기", "콤보 중 점프로 적을 띄워 공중 콤보로 잇는다.", 1, 1),
        N(SkillId.ChargeLevel3, "차지 강타 Ⅲ", "우클릭 차지가 3단계까지 차오르고, 놓으면 멀리 돌진해 베는 섬격이 나간다.", 1, 1),
        N(SkillId.KanpaSmash, "뇌락", "간파 성공 직후 우클릭: 뛰어올라 내려찍어 주변을 경직시키고 낙뢰를 떨어뜨린다.", 1, 2),
        N(SkillId.DodgeDraw, "회피 발도", "저스트 회피 직후 Q: 뇌기 없이 가까운 적을 자동으로 표식해 곧바로 발도한다.", 2, 2, SkillId.KanpaSmash),
        // [변경] 수련 탭 (패시브)
        P(SkillId.Vitality1, "체력 단련 Ⅰ", "최대 체력이 25 늘어난다.", 1, 0),
        P(SkillId.Vitality2, "체력 단련 Ⅱ", "최대 체력이 25 더 늘어난다.", 2, 0, SkillId.Vitality1),
        P(SkillId.ThunderSlot1, "뇌기 확장 Ⅰ", "뇌기 칸이 하나 늘어난다. 일섬 · 발도 · 패링을 더 자주 쓸 수 있다.", 2, 1),
        P(SkillId.ThunderSlot2, "뇌기 확장 Ⅱ", "뇌기 칸이 하나 더 늘어난다.", 2, 1, SkillId.ThunderSlot1),
    };

    static Node P(SkillId id, string name, string desc, int cost, int branch, params SkillId[] requires)
    {
        Node node = N(id, name, desc, cost, branch, requires);
        node.tab = 1;
        return node;
    }

    // [변경] 예전에 만든 에셋에 없는 기본 노드를 뒤에 덧붙임 (새 노드를 추가해도 에셋을 지울 필요 없음)
    public static List<Node> WithMissingDefaults(List<Node> nodes)
    {
        var result = new List<Node>(nodes);
        foreach (Node d in Defaults())
            if (!result.Exists(n => n.id == d.id)) result.Add(d);
        return result;
    }

    static Node N(SkillId id, string name, string desc, int cost, int branch, params SkillId[] requires) =>
        new Node { id = id, displayName = name, description = desc, cost = cost, branch = branch, requires = requires };
}
