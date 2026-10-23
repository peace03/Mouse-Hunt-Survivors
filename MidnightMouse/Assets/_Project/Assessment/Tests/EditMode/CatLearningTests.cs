using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>피격 순간의 목표와 종류에 따라 서로 다른 대처 요령을 고른다.</summary>
    public sealed class CatLearningTests
    {
        /// <summary>같은 조사 상태라도 미끼 이동과 소리 조사를 구별한다.</summary>
        [Test]
        public void BaitTargetAndEatingShareAdviceButNoiseInvestigationDoesNot()
        {
            var bait = CatLearning.Capture(CatKind.Curious, CatState.Investigate, CatTarget.Bait);
            var meal = CatLearning.Capture(CatKind.Curious, CatState.Eating, CatTarget.None);
            var noise = CatLearning.Capture(CatKind.Curious, CatState.Investigate, CatTarget.Normal);
            Assert.That(bait, Is.EqualTo(meal));
            Assert.That(bait.cause, Does.Contain("미끼"));
            Assert.That(noise.cause, Does.Contain("소리 조사"));
            Assert.That(noise.tip, Is.Not.EqualTo(bait.tip));
        }

        /// <summary>게으름냥의 기상 질주와 추적은 원인과 대응을 각각 설명한다.</summary>
        [Test]
        public void LazyWakeAndChaseHaveDifferentAdvice()
        {
            var wake = CatLearning.Capture(CatKind.Lazy, CatState.WakeRush, CatTarget.WakePoint);
            var chase = CatLearning.Capture(CatKind.Lazy, CatState.Chase, CatTarget.Player);
            Assert.That(wake.cause, Does.Contain("게으름냥").And.Contain("기상 질주"));
            Assert.That(chase.cause, Does.Contain("게으름냥").And.Contain("추적"));
            Assert.That(wake.tip, Is.Not.EqualTo(chase.tip));
            Assert.That(chase.tip, Does.Contain("잔상"));
        }
    }
}
