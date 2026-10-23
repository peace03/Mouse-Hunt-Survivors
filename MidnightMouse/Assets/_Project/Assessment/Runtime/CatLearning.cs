namespace MidnightMouse
{
    /// <summary>고양이별 반응과 마지막 피격의 대처 요령을 제공하며 씬에는 붙이지 않는다.</summary>
    public static class CatLearning
    {
        /// <summary>도움말에 표시할 종류별 이름과 세 가지 생존 요령을 반환한다.</summary>
        public static string Summary(CatKind kind) => kind switch
        {
            CatKind.Curious => "호기심냥\n소리가 난 곳을 조사해요.\n미끼는 추적도 끊어요. 먹은 뒤 × 동안은 무시해요.\n추적 끊기 · 미끼 또는 대시 잔상",
            CatKind.Lazy => "게으름냥\n발소리 원 안에서 잠이 줄고 밖에서는 천천히 회복해요. 접촉·원 안 대시는 즉시 깨워요.\n큰 소리엔 질주해요. 미끼로 다시 자요(마지막 단계 제외). 수면 중 피해 2배.\n추적 끊기 · 잔상, 또는 멀리 달아나 조용히",
            CatKind.Hunter => "사냥꾼냥\n미끼에 속지 않고 공 소리를 먼저 쫓아요.\n멈추거나 다가오면 덮쳐요. 웅크리면 착지 표시를 보세요.\n추적 끊기 · 공, 또는 덮치기 피하기",
            _ => ""
        };

        /// <summary>마지막 하트를 잃을 때의 상태와 목표로 원인 한 줄·요령 한 줄을 고른다.</summary>
        public static (string cause, string tip) Capture(CatKind kind, CatState state, CatTarget target)
        {
            if (kind == CatKind.Curious)
            {
                if (state == CatState.Eating || target == CatTarget.Bait)
                    return ("호기심냥 · 미끼로 가거나 먹는 중", "미끼를 먹는 고양이도 바로 옆이면 잡아요. 한 걸음 떨어져 기다리세요.");
                if (state == CatState.Chase)
                    return ("호기심냥 · 추적", "달리기만으로는 떨어지지 않아요. 미끼나 대시 잔상으로 추적을 끊으세요.");
                return ("호기심냥 · 소리 조사·순찰", "소리 원이 닿은 곳으로 와요. 가까이 있을 때는 살금살금 움직이세요.");
            }
            if (kind == CatKind.Lazy)
            {
                if (state == CatState.WakeRush)
                    return ("게으름냥 · 기상 질주", "큰 소리가 난 곳으로 달려와요. 공이나 함정을 쓴 뒤 그 자리에서 벗어나세요.");
                if (state == CatState.Chase)
                    return ("게으름냥 · 추적", "대시 잔상으로 추적을 끊으세요. 멀리 달아나 조용히 해도 놓쳐요.");
                if (state == CatState.Eating || target == CatTarget.Bait)
                    return ("게으름냥 · 미끼로 가거나 먹는 중", "미끼를 먹는 동안에도 잡혀요. 한 걸음 떨어져 기다리세요.");
                return ("게으름냥 · 소리 조사·귀가", "깨어 있을 때 미끼를 먹으면 그 자리에서 잠들어요. 먹는 동안은 떨어져 계세요.");
            }
            if (kind == CatKind.Hunter)
            {
                if (state == CatState.PounceReady || state == CatState.Pouncing)
                    return ("사냥꾼냥 · 덮치기", "웅크리면 착지 표시를 보고 꺾거나 대시하세요. 피하면 추적이 끝나요.");
                if (state == CatState.Chase)
                    return ("사냥꾼냥 · 추적", "달리기만으로는 떨어지지 않아요. 공을 차거나 덮치기를 피하세요.");
                return ("사냥꾼냥 · 소리 조사", "미끼에 속지 않아요. 공 소리를 가장 먼저 쫓아요.");
            }
            return ("고양이와 접촉", "고양이 가까이에서는 거리를 두고 움직이세요.");
        }
    }
}
