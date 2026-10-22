using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>아침 당김이 실제 시간과 정지 규칙을 침범하지 않는지 검사한다.</summary>
    public sealed class M0SessionTests
    {
        /// <summary>게임 시계 이동은 실제 기록과 무적을 보존하고 잘못된 입력은 거부한다.</summary>
        [Test]
        public void DawnAdvancePreservesRealTimeAndInvulnerability()
        {
            var session = new GameSession();
            session.Start(); session.Tick(3f); session.TryDamage();
            session.AdvanceClock(6f);
            Assert.That(session.Elapsed, Is.EqualTo(9f));
            Assert.That(session.RealElapsed, Is.EqualTo(3f));
            Assert.That(session.InvulnerabilityRemaining, Is.EqualTo(1.5f));
            session.AdvanceClock(float.NaN); session.AdvanceClock(float.PositiveInfinity); session.AdvanceClock(-1f);
            Assert.That(session.Elapsed, Is.EqualTo(9f));
            session.TogglePause(); session.AdvanceClock(30f); session.Tick(30f);
            Assert.That(session.Elapsed, Is.EqualTo(9f));
            Assert.That(session.RealElapsed, Is.EqualTo(3f));
            session.TogglePause(); session.CollectCheese(4);
            session.AdvanceClock(30f); session.Tick(30f);
            Assert.That(session.Elapsed, Is.EqualTo(9f));
            session.CompleteUpgrade(); session.AdvanceClock(999f);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(session.Elapsed, Is.EqualTo(300f));
            Assert.That(session.RealElapsed, Is.EqualTo(3f));
            session.Start();
            Assert.That(session.RealElapsed, Is.Zero);
            Assert.That(session.Elapsed, Is.Zero);
        }

        /// <summary>패배한 판은 뒤늦은 당김으로 승리하지 않는다.</summary>
        [Test]
        public void DawnCannotChangeADeadRun()
        {
            var session = new GameSession(startHearts: 1);
            session.Start(); session.Tick(10f); session.TryDamage(); session.AdvanceClock(300f);
            Assert.That(session.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(session.Elapsed, Is.EqualTo(10f));
            Assert.That(session.RealElapsed, Is.EqualTo(10f));
        }
    }
}
