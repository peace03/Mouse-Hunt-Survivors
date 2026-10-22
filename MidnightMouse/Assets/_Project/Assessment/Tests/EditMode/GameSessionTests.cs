using System;
using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>순수 세션 규칙의 상태 전이와 경계값을 검증한다.</summary>
    public sealed class GameSessionTests
    {
        /// <summary>준비 중에는 시간·피해·보상이 멈추며 시작 입력으로만 진행한다.</summary>
        [Test]
        public void ReadyRejectsGameplayUntilBeginPlay()
        {
            var session = new GameSession();
            session.BeginPlay();
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Title));
            session.Start(waitForInput: true);
            session.Tick(100f);
            session.AdvanceClock(100f);
            session.CollectCheese(10);
            session.TogglePause();
            Assert.That(session.TryDamage(), Is.False);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(session.Elapsed, Is.Zero);
            Assert.That(session.RealElapsed, Is.Zero);
            Assert.That(session.CheeseCollected, Is.Zero);
            session.BeginPlay();
            session.Tick(1f);
            session.BeginPlay();
            Assert.That(session.Elapsed, Is.EqualTo(1f));
        }

        /// <summary>시작 전에는 시간이 흐르지 않으며 시작하면 초기 상태가 설정된다.</summary>
        [Test]
        public void TitleDoesNotAdvanceAndStartInitializesRun()
        {
            var session = new GameSession();
            session.Tick(20f);
            Assert.That(session.Elapsed, Is.Zero);
            session.Start();
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(session.Hearts, Is.EqualTo(3));
        }

        /// <summary>300초 경계 전에는 진행 중이며 경계를 넘으면 시간을 고정해 승리한다.</summary>
        [Test]
        public void SurvivalWinsExactlyAtDurationAndClampsOvershoot()
        {
            var session = new GameSession();
            session.Start();
            session.Tick(299.5f);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Playing));
            session.Tick(2f);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(session.Elapsed, Is.EqualTo(300f));
            Assert.That(session.TryDamage(), Is.False);
        }

        /// <summary>일시정지에서는 시간과 피격 무적이 함께 멈춘다.</summary>
        [Test]
        public void PauseFreezesTimeAndInvulnerability()
        {
            var session = new GameSession();
            session.Start();
            session.TryDamage();
            session.TogglePause();
            session.Tick(10f);
            Assert.That(session.Elapsed, Is.Zero);
            Assert.That(session.InvulnerabilityRemaining, Is.EqualTo(1.5f));
            Assert.That(session.TryDamage(), Is.False);
            session.TogglePause();
            session.Tick(1.5f);
            Assert.That(session.TryDamage(), Is.True);
        }

        /// <summary>무적이 끝나기 전에는 연속 피해를 받지 않는다.</summary>
        [Test]
        public void DamageRejectsRepeatedHitsUntilInvulnerabilityExpires()
        {
            var session = new GameSession();
            session.Start();
            Assert.That(session.TryDamage(), Is.True);
            Assert.That(session.TryDamage(), Is.False);
            session.Tick(1.49f);
            Assert.That(session.TryDamage(), Is.False);
            session.Tick(0.02f);
            Assert.That(session.TryDamage(), Is.True);
            Assert.That(session.Hearts, Is.EqualTo(1));
        }

        /// <summary>체력이 0인 판은 시간·회복·경험치로 되살아나지 않는다.</summary>
        [Test]
        public void DeathIsTerminalEvenWhenTimeOrRewardsArriveLater()
        {
            var session = new GameSession();
            session.Start();
            session.TryDamage();
            session.Tick(2f);
            session.TryDamage();
            session.Tick(2f);
            session.TryDamage();
            session.Tick(400f);
            session.IncreaseMaxHearts();
            session.CollectCheese(20);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(session.Hearts, Is.Zero);
            Assert.That(session.Elapsed, Is.EqualTo(4f));
            Assert.That(session.CheeseCollected, Is.Zero);
        }

        /// <summary>경험치 10은 4와 6을 차례로 소비하고 선택 화면을 두 번 제공한다.</summary>
        [Test]
        public void ExperienceOverflowQueuesConsecutiveUpgradeChoices()
        {
            var session = new GameSession();
            session.Start();
            session.CollectCheese(10);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Upgrade));
            Assert.That(session.Level, Is.EqualTo(2));
            Assert.That(session.Experience, Is.EqualTo(6));
            session.Tick(100f);
            Assert.That(session.Elapsed, Is.Zero);
            session.CompleteUpgrade();
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Upgrade));
            Assert.That(session.Level, Is.EqualTo(3));
            Assert.That(session.Experience, Is.Zero);
            Assert.That(session.ExperienceToNext, Is.EqualTo(8));
            session.CompleteUpgrade();
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Playing));
        }

        /// <summary>마지막 필요 경험치에 도달하기 전에는 선택이 열리지 않는다.</summary>
        [Test]
        public void ExperienceBelowThresholdRemainsAvailableAfterNextPickup()
        {
            var session = new GameSession();
            session.Start();
            session.CollectCheese(3);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Playing));
            session.CollectCheese(2);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Upgrade));
            Assert.That(session.Experience, Is.EqualTo(1));
            Assert.That(session.CheeseCollected, Is.EqualTo(2));
        }

        /// <summary>최대 체력은 상한을 넘지 않고 상한에서도 회복은 적용된다.</summary>
        [Test]
        public void HeartUpgradeRespectsCapAndStillHealsAtCap()
        {
            var session = new GameSession();
            session.Start();
            session.IncreaseMaxHearts();
            session.IncreaseMaxHearts();
            session.TryDamage();
            session.IncreaseMaxHearts();
            Assert.That(session.MaxHearts, Is.EqualTo(5));
            Assert.That(session.Hearts, Is.EqualTo(5));
        }

        /// <summary>새 판은 성장·점수·피격 무적을 모두 초기화한다.</summary>
        [Test]
        public void RestartClearsAllRunProgress()
        {
            var session = new GameSession();
            session.Start();
            session.Tick(10f);
            session.IncreaseMaxHearts();
            session.TryDamage();
            session.RegisterBanished();
            session.CollectCheese(4);
            session.Start();
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(session.Elapsed, Is.Zero);
            Assert.That(session.Hearts, Is.EqualTo(3));
            Assert.That(session.MaxHearts, Is.EqualTo(3));
            Assert.That(session.Level, Is.EqualTo(1));
            Assert.That(session.Experience, Is.Zero);
            Assert.That(session.CheeseCollected, Is.Zero);
            Assert.That(session.CatsBanished, Is.Zero);
            Assert.That(session.InvulnerabilityRemaining, Is.Zero);
        }

        /// <summary>시작 화면으로 돌아가면 남은 피해나 시간이 진행되지 않는다.</summary>
        [Test]
        public void ReturnToTitleStopsTheRun()
        {
            var session = new GameSession();
            session.Start();
            session.Tick(4f);
            session.ReturnToTitle();
            session.Tick(50f);
            session.TogglePause();
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Title));
            Assert.That(session.Elapsed, Is.EqualTo(4f));
            Assert.That(session.TryDamage(), Is.False);
        }

        /// <summary>잘못된 시간 입력이 판의 상태를 오염시키지 않는다.</summary>
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void InvalidDeltaDoesNotChangeState(float delta)
        {
            var session = new GameSession();
            session.Start();
            session.Tick(delta);
            Assert.That(session.Elapsed, Is.Zero);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Playing));
        }

        /// <summary>잘못된 경험치 보상은 점수와 성장에 반영하지 않는다.</summary>
        [TestCase(0)]
        [TestCase(-2)]
        public void NonPositivePickupIsIgnored(int value)
        {
            var session = new GameSession();
            session.Start();
            session.CollectCheese(value);
            Assert.That(session.CheeseCollected, Is.Zero);
            Assert.That(session.Experience, Is.Zero);
        }

        /// <summary>유효하지 않은 판 설정은 즉시 거부한다.</summary>
        [Test]
        public void InvalidRulesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(duration: 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(startHearts: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(invulnerability: -1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameSession(baseExperience: 0));
        }
    }
}
