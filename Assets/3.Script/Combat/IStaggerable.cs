// 흐트러진 상태(강경직·쓰러짐·공중에 뜸·체간 붕괴·처형 가능)를 알려 주는 대상
// 벽력일섬(Q)이 이 상태의 적을 우선 노리고 즉사·연쇄시킴, ThunderMark가 머리 위에 표시
public interface IStaggerable
{
    bool IsStaggered { get; }
}
