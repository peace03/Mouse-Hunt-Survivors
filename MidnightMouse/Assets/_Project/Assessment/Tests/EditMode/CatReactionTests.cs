using System;
using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>기획 반응표의 기대값을 씬·프레임 없이 검사한다.</summary>
    public sealed class CatReactionTests
    {
        /// <summary>각 종류의 기본 반응과 수면 예외를 기획표의 각 칸으로 검사한다.</summary>
        [TestCase(CatKind.Curious, CatState.Patrol, NoiseKind.Normal, CatReaction.Investigate)]
        [TestCase(CatKind.Curious, CatState.Patrol, NoiseKind.Loud, CatReaction.Investigate)]
        [TestCase(CatKind.Curious, CatState.Chase, NoiseKind.Afterimage, CatReaction.Investigate)]
        [TestCase(CatKind.Curious, CatState.Chase, NoiseKind.Bait, CatReaction.FollowBait)]
        [TestCase(CatKind.Curious, CatState.Patrol, NoiseKind.Ball, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatState.Patrol, NoiseKind.Normal, CatReaction.Investigate)]
        [TestCase(CatKind.Lazy, CatState.Patrol, NoiseKind.Loud, CatReaction.Investigate)]
        [TestCase(CatKind.Lazy, CatState.Chase, NoiseKind.Afterimage, CatReaction.Investigate)]
        [TestCase(CatKind.Lazy, CatState.Patrol, NoiseKind.Bait, CatReaction.FollowBait)]
        [TestCase(CatKind.Lazy, CatState.Chase, NoiseKind.Bait, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatState.Patrol, NoiseKind.Ball, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, NoiseKind.Normal, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, NoiseKind.Loud, CatReaction.WakeRush)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, NoiseKind.Afterimage, CatReaction.WakeRush)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, NoiseKind.Bait, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, NoiseKind.Ball, CatReaction.Ignore)]
        [TestCase(CatKind.Hunter, CatState.Patrol, NoiseKind.Normal, CatReaction.Investigate)]
        [TestCase(CatKind.Hunter, CatState.Patrol, NoiseKind.Loud, CatReaction.Investigate)]
        [TestCase(CatKind.Hunter, CatState.Chase, NoiseKind.Afterimage, CatReaction.Ignore)]
        [TestCase(CatKind.Hunter, CatState.Patrol, NoiseKind.Bait, CatReaction.Ignore)]
        [TestCase(CatKind.Hunter, CatState.Chase, NoiseKind.Ball, CatReaction.FollowBall)]
        public void ReactionTable(CatKind kind, CatState state, NoiseKind noise, CatReaction expected)
        {
            var target = state == CatState.Chase ? CatTarget.Player : CatTarget.None;
            Assert.That(CatReactionRules.EvaluateNoise(kind, state, target, noise, baitAvailable: true), Is.EqualTo(expected));
        }

        /// <summary>잠든 고양이는 발소리 이벤트 대신 연속 시간을 쓰고 대시만 즉시 추적한다.</summary>
        [TestCase(NoiseKind.Normal, CatReaction.Ignore)]
        [TestCase(NoiseKind.Loud, CatReaction.Ignore)]
        [TestCase(NoiseKind.Afterimage, CatReaction.WakeChase)]
        public void SleepingPlayerNoiseUsesTimedFootstepsOrImmediateDash(NoiseKind noise, CatReaction expected) =>
            Assert.That(CatReactionRules.EvaluateNoise(CatKind.Lazy, CatState.Sleeping, CatTarget.None, noise, fromPlayer: true), Is.EqualTo(expected));

        /// <summary>공 멈춤은 큰 소리이되 사냥꾼에게만 공 순위로 추적을 끊는다.</summary>
        [TestCase(CatKind.Curious, CatState.Patrol, CatReaction.Investigate)]
        [TestCase(CatKind.Curious, CatState.Chase, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, CatReaction.WakeRush)]
        [TestCase(CatKind.Lazy, CatState.Returning, CatReaction.Investigate)]
        [TestCase(CatKind.Hunter, CatState.Chase, CatReaction.FollowBall)]
        public void BallStopKeepsSoundKindAndSource(CatKind kind, CatState state, CatReaction expected)
        {
            Assert.That(CatReactionRules.NoiseTarget(kind, NoiseKind.Loud, true),
                Is.EqualTo(kind == CatKind.Hunter ? CatTarget.Ball : CatTarget.Loud));
            Assert.That(CatReactionRules.EvaluateNoise(kind, state, state == CatState.Chase ? CatTarget.Player : CatTarget.None,
                NoiseKind.Loud, fromBall: true), Is.EqualTo(expected));
        }

        /// <summary>먼저 선택한 높은 목표는 유지하고 동순위 새 소리는 교체한다.</summary>
        [TestCase(CatKind.Curious, CatTarget.Bait, NoiseKind.Afterimage, CatReaction.Ignore)]
        [TestCase(CatKind.Curious, CatTarget.Afterimage, NoiseKind.Bait, CatReaction.FollowBait)]
        [TestCase(CatKind.Curious, CatTarget.Loud, NoiseKind.Normal, CatReaction.Investigate)]
        [TestCase(CatKind.Curious, CatTarget.Bait, NoiseKind.Bait, CatReaction.FollowBait)]
        [TestCase(CatKind.Lazy, CatTarget.Loud, NoiseKind.Normal, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatTarget.Bait, NoiseKind.Loud, CatReaction.Ignore)]
        [TestCase(CatKind.Lazy, CatTarget.Afterimage, NoiseKind.Bait, CatReaction.Ignore)]
        [TestCase(CatKind.Hunter, CatTarget.Ball, NoiseKind.Loud, CatReaction.Ignore)]
        [TestCase(CatKind.Hunter, CatTarget.Ball, NoiseKind.Ball, CatReaction.FollowBall)]
        public void NewTargetMustHaveEqualOrHigherPriority(CatKind kind, CatTarget current, NoiseKind noise, CatReaction expected) =>
            Assert.That(CatReactionRules.EvaluateNoise(kind, CatState.Investigate, current, noise, baitAvailable: true), Is.EqualTo(expected));

        /// <summary>기상 질주는 잔상과 근접만 끊을 수 있다.</summary>
        [TestCase(NoiseKind.Normal, CatReaction.Ignore)]
        [TestCase(NoiseKind.Loud, CatReaction.Ignore)]
        [TestCase(NoiseKind.Bait, CatReaction.Ignore)]
        [TestCase(NoiseKind.Ball, CatReaction.Ignore)]
        [TestCase(NoiseKind.Afterimage, CatReaction.Investigate)]
        public void WakeRushKeepsItsPoint(NoiseKind noise, CatReaction expected) =>
            Assert.That(CatReactionRules.EvaluateNoise(CatKind.Lazy, CatState.WakeRush, CatTarget.WakePoint, noise, baitAvailable: true), Is.EqualTo(expected));

        /// <summary>먹기·기절·퇴장 등 고정 상태는 모든 소리와 근접을 무시한다.</summary>
        [TestCase(CatState.Eating)]
        [TestCase(CatState.Stunned)]
        [TestCase(CatState.Retreat)]
        [TestCase(CatState.Respawning)]
        [TestCase(CatState.PounceReady)]
        [TestCase(CatState.Pouncing)]
        [TestCase(CatState.Stumbling)]
        [TestCase(CatState.PlayingBall)]
        public void LockedStatesIgnoreStimuli(CatState state)
        {
            foreach (CatKind kind in Enum.GetValues(typeof(CatKind)))
            {
                foreach (NoiseKind noise in Enum.GetValues(typeof(NoiseKind)))
                    Assert.That(CatReactionRules.EvaluateNoise(kind, state, CatTarget.None, noise, true, true, true), Is.EqualTo(CatReaction.Ignore), kind + "/" + noise);
                Assert.That(CatReactionRules.ShouldChase(kind, state, CatTarget.None), Is.False);
            }
        }

        /// <summary>내성·없는 미끼·게으름냥의 5단계 이후에는 미끼로 목표를 바꾸지 않는다.</summary>
        [TestCase(CatKind.Curious, false, false, 1)]
        [TestCase(CatKind.Curious, true, true, 1)]
        [TestCase(CatKind.Lazy, true, false, 5)]
        [TestCase(CatKind.Lazy, true, false, 6)]
        public void BaitConditions(CatKind kind, bool available, bool immune, int stage) =>
            Assert.That(CatReactionRules.EvaluateNoise(kind, CatState.Investigate, CatTarget.Normal, NoiseKind.Bait,
                baitAvailable: available, baitImmune: immune, stage: stage), Is.EqualTo(CatReaction.Ignore));

        /// <summary>쥐 자리에서 난 일반·큰 소리만 놓침 시간을 갱신한다.</summary>
        [TestCase(CatKind.Curious)]
        [TestCase(CatKind.Lazy)]
        [TestCase(CatKind.Hunter)]
        public void OnlyPlayerNoiseRefreshesChase(CatKind kind)
        {
            foreach (var noise in new[] { NoiseKind.Normal, NoiseKind.Loud })
            {
                Assert.That(CatReactionRules.EvaluateNoise(kind, CatState.Chase, CatTarget.Player, noise, fromPlayer: true), Is.EqualTo(CatReaction.RefreshChase));
                Assert.That(CatReactionRules.EvaluateNoise(kind, CatState.Chase, CatTarget.Player, noise), Is.EqualTo(CatReaction.Ignore));
            }
        }

        /// <summary>근접 감지도 종류별 목표 순서를 따른다.</summary>
        [TestCase(CatKind.Curious, CatState.Patrol, CatTarget.None, true)]
        [TestCase(CatKind.Curious, CatState.Investigate, CatTarget.Afterimage, false)]
        [TestCase(CatKind.Curious, CatState.Investigate, CatTarget.Bait, false)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, CatTarget.None, false)]
        [TestCase(CatKind.Lazy, CatState.WakeRush, CatTarget.WakePoint, true)]
        [TestCase(CatKind.Lazy, CatState.Returning, CatTarget.None, true)]
        [TestCase(CatKind.Hunter, CatState.Investigate, CatTarget.Ball, false)]
        public void ProximityUsesTheSamePriority(CatKind kind, CatState state, CatTarget target, bool expected) =>
            Assert.That(CatReactionRules.ShouldChase(kind, state, target), Is.EqualTo(expected));

        /// <summary>잠든 게으름냥의 피격은 기절 대신 기상이며 퇴장 슬롯은 맞지 않는다.</summary>
        [TestCase(CatKind.Curious, CatState.Patrol, CatReaction.Stun)]
        [TestCase(CatKind.Lazy, CatState.Sleeping, CatReaction.WakeRush)]
        [TestCase(CatKind.Lazy, CatState.Chase, CatReaction.Stun)]
        [TestCase(CatKind.Hunter, CatState.Stunned, CatReaction.Stun)]
        [TestCase(CatKind.Hunter, CatState.Retreat, CatReaction.Ignore)]
        [TestCase(CatKind.Curious, CatState.Respawning, CatReaction.Ignore)]
        public void TrapReaction(CatKind kind, CatState state, CatReaction expected) =>
            Assert.That(CatReactionRules.EvaluateTrapHit(kind, state), Is.EqualTo(expected));

        /// <summary>목표 종료와 5단계 직전·직후의 수면 경계를 검사한다.</summary>
        [TestCase(CatKind.Curious, CatState.Investigate, CatTarget.Afterimage, CatTargetEnd.Observed, 1, CatState.Patrol)]
        [TestCase(CatKind.Lazy, CatState.Investigate, CatTarget.Normal, CatTargetEnd.Observed, 4, CatState.Returning)]
        [TestCase(CatKind.Lazy, CatState.Investigate, CatTarget.Normal, CatTargetEnd.Observed, 5, CatState.Patrol)]
        [TestCase(CatKind.Curious, CatState.Chase, CatTarget.Player, CatTargetEnd.LostPlayer, 1, CatState.Investigate)]
        [TestCase(CatKind.Curious, CatState.Investigate, CatTarget.Bait, CatTargetEnd.BaitClaimed, 1, CatState.Eating)]
        [TestCase(CatKind.Curious, CatState.Investigate, CatTarget.Bait, CatTargetEnd.BaitGone, 1, CatState.Investigate)]
        [TestCase(CatKind.Curious, CatState.Stunned, CatTarget.None, CatTargetEnd.StunFinished, 1, CatState.Investigate)]
        [TestCase(CatKind.Curious, CatState.Eating, CatTarget.None, CatTargetEnd.MealFinished, 1, CatState.Patrol)]
        [TestCase(CatKind.Lazy, CatState.Eating, CatTarget.None, CatTargetEnd.MealFinished, 4, CatState.Sleeping)]
        [TestCase(CatKind.Lazy, CatState.Eating, CatTarget.None, CatTargetEnd.MealFinished, 5, CatState.Patrol)]
        [TestCase(CatKind.Lazy, CatState.WakeRush, CatTarget.WakePoint, CatTargetEnd.WakeFinished, 4, CatState.Investigate)]
        [TestCase(CatKind.Lazy, CatState.Returning, CatTarget.None, CatTargetEnd.HomeReached, 4, CatState.Sleeping)]
        [TestCase(CatKind.Lazy, CatState.Returning, CatTarget.None, CatTargetEnd.HomeReached, 5, CatState.Patrol)]
        [TestCase(CatKind.Hunter, CatState.PlayingBall, CatTarget.Ball, CatTargetEnd.BallFinished, 5, CatState.Patrol)]
        [TestCase(CatKind.Hunter, CatState.Stumbling, CatTarget.Player, CatTargetEnd.StumbleFinished, 5, CatState.Investigate)]
        public void TargetEndTable(CatKind kind, CatState state, CatTarget target, CatTargetEnd ending, int stage, CatState expected) =>
            Assert.That(CatReactionRules.EndTarget(kind, state, target, ending, stage), Is.EqualTo(expected));

        /// <summary>정의하지 않은 조합은 임의로 상태를 전환하지 않는다.</summary>
        [Test]
        public void UndefinedCombinationsKeepCurrentBehavior()
        {
            Assert.That(CatReactionRules.EvaluateNoise(CatKind.Hunter, CatState.Sleeping, CatTarget.None, NoiseKind.Loud), Is.EqualTo(CatReaction.Ignore));
            Assert.That(CatReactionRules.EvaluateNoise(CatKind.Curious, CatState.Patrol, CatTarget.None, (NoiseKind)99), Is.EqualTo(CatReaction.Ignore));
            Assert.That(CatReactionRules.EndTarget(CatKind.Curious, CatState.Eating, CatTarget.None, CatTargetEnd.Observed, 1), Is.EqualTo(CatState.Eating));
            Assert.That(CatReactionRules.EndTarget(CatKind.Lazy, CatState.Investigate, CatTarget.Bait, CatTargetEnd.Observed, 1), Is.EqualTo(CatState.Investigate));
        }
    }
}
