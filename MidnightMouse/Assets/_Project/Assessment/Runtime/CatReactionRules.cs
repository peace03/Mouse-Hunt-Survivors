namespace MidnightMouse
{
    public enum CatTarget { None, Normal, Loud, Afterimage, Bait, Ball, Player, WakePoint }
    public enum CatReaction { Ignore, Investigate, FollowBait, FollowBall, WakeRush, RefreshChase, Stun, WakeChase }
    public enum CatTargetEnd { Observed, LostPlayer, BaitClaimed, BaitGone, StunFinished, MealFinished, WakeFinished, HomeReached, BallFinished, StumbleFinished }

    /// <summary>씬·시간·난수에 접근하지 않고 고양이 반응표와 목표 종료를 판정한다.</summary>
    public static class CatReactionRules
    {
        /// <summary>소리 출처를 목표 종류로 바꾼다. 공 멈춤도 소리는 Loud이며 목표만 Ball이다.</summary>
        public static CatTarget NoiseTarget(CatKind kind, NoiseKind noise, bool fromBall = false) => noise switch
        {
            NoiseKind.Normal => CatTarget.Normal,
            NoiseKind.Loud => fromBall && kind == CatKind.Hunter ? CatTarget.Ball : CatTarget.Loud,
            NoiseKind.Afterimage => CatTarget.Afterimage,
            NoiseKind.Bait => CatTarget.Bait,
            NoiseKind.Ball => CatTarget.Ball,
            _ => CatTarget.None
        };

        /// <summary>기획의 순서만 숫자로 비교한다. 0은 해당 종류가 선택하지 않는 목표다.</summary>
        public static int Priority(CatKind kind, CatTarget target) => kind switch
        {
            CatKind.Curious => target switch
            {
                CatTarget.Normal or CatTarget.Loud => 1, CatTarget.Player => 2,
                CatTarget.Afterimage => 3, CatTarget.Bait => 4, _ => 0
            },
            CatKind.Lazy => target switch
            {
                CatTarget.Normal => 1, CatTarget.Loud => 2, CatTarget.Bait => 3,
                CatTarget.Player or CatTarget.WakePoint => 4, CatTarget.Afterimage => 5, _ => 0
            },
            CatKind.Hunter => target switch
            {
                CatTarget.Normal or CatTarget.Loud => 1, CatTarget.Player => 2, CatTarget.Ball => 3, _ => 0
            },
            _ => 0
        };

        /// <summary>반응표에 있는 대기·이동 상태만 새 자극을 선택한다.</summary>
        private static bool CanReact(CatKind kind, CatState state) => kind switch
        {
            CatKind.Curious or CatKind.Hunter => state == CatState.Patrol || state == CatState.Investigate || state == CatState.Chase,
            CatKind.Lazy => state == CatState.Patrol || state == CatState.Investigate || state == CatState.Chase ||
                state == CatState.Sleeping || state == CatState.WakeRush || state == CatState.Returning,
            _ => false
        };

        /// <summary>들린 소리만 입력받아 새 목표·추적 시간 갱신·무시 중 하나를 반환한다.</summary>
        public static CatReaction EvaluateNoise(CatKind kind, CatState state, CatTarget current, NoiseKind noise,
            bool fromPlayer = false, bool fromBall = false, bool baitAvailable = false, bool baitImmune = false, int stage = 1)
        {
            if (!CanReact(kind, state)) return CatReaction.Ignore;
            if (state == CatState.Sleeping)
            {
                // 모찌의 이동은 실제 이동 시간으로 별도 처리해 달리기 한 번에 깨지 않게 한다.
                if (fromPlayer) return noise == NoiseKind.Afterimage ? CatReaction.WakeChase : CatReaction.Ignore;
                return noise == NoiseKind.Loud || noise == NoiseKind.Afterimage ? CatReaction.WakeRush : CatReaction.Ignore;
            }
            if (noise == NoiseKind.Bait && (!baitAvailable || baitImmune || (kind == CatKind.Lazy && stage >= 5)))
                return CatReaction.Ignore;
            if (noise == NoiseKind.Ball && kind != CatKind.Hunter) return CatReaction.Ignore;
            if (state == CatState.Chase && fromPlayer && (noise == NoiseKind.Normal || noise == NoiseKind.Loud))
                return CatReaction.RefreshChase;

            CatTarget incoming = NoiseTarget(kind, noise, fromBall);
            int priority = Priority(kind, incoming);
            if (priority == 0 || priority < Priority(kind, current)) return CatReaction.Ignore;
            return incoming switch
            {
                CatTarget.Bait => CatReaction.FollowBait,
                CatTarget.Ball => CatReaction.FollowBall,
                _ => CatReaction.Investigate
            };
        }

        /// <summary>근접은 추적과 같은 순위의 새 자극이며 수면이나 고정 동작을 끊지 않는다.</summary>
        public static bool ShouldChase(CatKind kind, CatState state, CatTarget current) =>
            CanReact(kind, state) && state != CatState.Sleeping && Priority(kind, CatTarget.Player) >= Priority(kind, current);

        /// <summary>살아남은 피격의 반응. 기절 중 재피격도 가능하며 잠든 게으름냥만 바로 깬다.</summary>
        public static CatReaction EvaluateTrapHit(CatKind kind, CatState state)
        {
            if (kind < CatKind.Curious || kind > CatKind.Hunter || state < CatState.Patrol || state > CatState.PlayingBall ||
                state == CatState.Retreat || state == CatState.Respawning)
                return CatReaction.Ignore;
            return kind == CatKind.Lazy && state == CatState.Sleeping ? CatReaction.WakeRush : CatReaction.Stun;
        }

        /// <summary>끝난 목표를 다음 상태로 옮기며 정의하지 않은 조합은 현재 상태를 유지한다.</summary>
        public static CatState EndTarget(CatKind kind, CatState state, CatTarget target, CatTargetEnd ending, int stage)
        {
            if (kind < CatKind.Curious || kind > CatKind.Hunter) return state;
            CatState idle = kind == CatKind.Lazy && stage < 5 ? CatState.Returning : CatState.Patrol;
            switch (ending)
            {
                case CatTargetEnd.Observed when state == CatState.Investigate && target != CatTarget.Bait && target != CatTarget.Ball:
                    return idle;
                case CatTargetEnd.LostPlayer when state == CatState.Chase:
                    return CatState.Investigate;
                case CatTargetEnd.BaitClaimed when state == CatState.Investigate && target == CatTarget.Bait:
                    return CatState.Eating;
                case CatTargetEnd.BaitGone when state == CatState.Investigate && target == CatTarget.Bait:
                    return CatState.Investigate;
                case CatTargetEnd.StunFinished when state == CatState.Stunned:
                    return CatState.Investigate;
                case CatTargetEnd.MealFinished when state == CatState.Eating:
                    return kind == CatKind.Lazy && stage < 5 ? CatState.Sleeping : CatState.Patrol;
                case CatTargetEnd.WakeFinished when kind == CatKind.Lazy && state == CatState.WakeRush:
                    return CatState.Investigate;
                case CatTargetEnd.HomeReached when kind == CatKind.Lazy && state == CatState.Returning:
                    return stage < 5 ? CatState.Sleeping : CatState.Patrol;
                case CatTargetEnd.BallFinished when kind == CatKind.Hunter && state == CatState.PlayingBall:
                    return CatState.Patrol;
                case CatTargetEnd.StumbleFinished when kind == CatKind.Hunter && state == CatState.Stumbling:
                    return CatState.Investigate;
                default: return state;
            }
        }
    }
}
