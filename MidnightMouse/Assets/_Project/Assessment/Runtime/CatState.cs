namespace MidnightMouse
{
    /// <summary>고양이 3종 반응표의 상태. 기존 값 뒤에 추가해 직렬화 번호를 유지한다.</summary>
    public enum CatState { Patrol, Investigate, Chase, Eating, Stunned, Retreat, Respawning, Sleeping, WakeRush, Returning, PounceReady, Pouncing, Stumbling, PlayingBall }
}
