using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MidnightMouse.Tests
{
    /// <summary>저장된 밤 데이터와 작동별 부탁 판정을 검증한다.</summary>
    public sealed class NightDefinitionTests
    {
        /// <summary>세 밤의 실제 에셋은 다섯 단계와 풀 상한을 지킨다.</summary>
        [Test]
        public void CampaignAssetsHaveValidDistinctNightsAndCatKinds()
        {
            var nights = Resources.LoadAll<NightDefinition>("Campaign/Nights");
            System.Array.Sort(nights, (a, b) => a.Order.CompareTo(b.Order));
            Assert.That(nights.Length, Is.EqualTo(4));
            Assert.That(nights[0].Id, Is.Not.EqualTo(nights[1].Id));
            Assert.That(nights[0].Duration, Is.EqualTo(225));
            Assert.That(nights[1].Duration, Is.EqualTo(300));
            foreach (var night in nights) Assert.That(night.IsValid(), Is.True, night.name);
            Assert.That(nights[0].Count(nights[0].Cats[0].kind, 45), Is.EqualTo(2));
            Assert.That(nights[1].Count(nights[1].Cats[0].kind, 240), Is.EqualTo(4));
            Assert.That(nights[0].StageLine(999), Is.EqualTo(nights[0].StageLine(180)));
            Assert.That(nights[2].LullabyRequest, Is.True);
            Assert.That(nights[3].Ball, Is.True);
            Assert.That(nights[3].BallWakeRequest, Is.True);
            Assert.That(nights[3].BallWakeWindow, Is.EqualTo(10f));
            Assert.That(nights[3].Count(nights[3].Cats[0].kind, 240), Is.EqualTo(4));
            Assert.That(nights[3].Count(nights[3].Cats[1].kind, 240), Is.EqualTo(2));
            for (int i = 0; i < 3; i++) Assert.That(nights[i].Ball, Is.False);
            Assert.That(nights[2].Cats[1].kind.Kind, Is.EqualTo(CatKind.Lazy));
            Assert.That(nights[2].Count(nights[2].Cats[1].kind, 0), Is.EqualTo(2));
            Assert.That(nights[2].Count(nights[2].Cats[1].kind, 240), Is.EqualTo(2));
            Assert.That(nights[2].Cats[1].kind.Health, Is.EqualTo(5));
        }

        /// <summary>사소한 에셋 오입력도 실행 전에 거부한다.</summary>
        [TestCase("_chaseMultiplier", float.NaN)]
        [TestCase("_baitImmunity", -1f)]
        [TestCase("_stageSeconds", float.PositiveInfinity)]
        public void InvalidRuleValuesAreRejected(string field, float value)
        {
            var source = Resources.Load<NightDefinition>("Campaign/Nights/Night1");
            var copy = Object.Instantiate(source);
            try
            {
                typeof(NightDefinition).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(copy, value);
                Assert.That(copy.IsValid(), Is.False);
            }
            finally { Object.DestroyImmediate(copy); }
        }

        /// <summary>수면 누적 시간을 0이나 비정상 수로 지정한 에셋은 실행 전에 거부한다.</summary>
        [TestCase("_walkWakeSeconds", 0f)]
        [TestCase("_walkWakeSeconds", float.NaN)]
        [TestCase("_runWakeSeconds", -1f)]
        [TestCase("_runWakeSeconds", float.PositiveInfinity)]
        [TestCase("_sleepRecoverySeconds", 0f)]
        [TestCase("_sleepRecoverySeconds", float.NaN)]
        [TestCase("_sleepRecoverySeconds", float.PositiveInfinity)]
        [TestCase("_sleepRecoverySeconds", 3f)]
        [TestCase("_sleepRecoverySeconds", 1.5f)]
        public void InvalidWakeDurationsAreRejected(string field, float value)
        {
            var copy = Object.Instantiate(Resources.Load<CatDefinition>("Campaign/LazyCat"));
            try
            {
                typeof(CatDefinition).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(copy, value);
                Assert.That(copy.IsValid(), Is.False);
            }
            finally { Object.DestroyImmediate(copy); }
        }

        /// <summary>다른 작동의 한 마리씩은 합치지 않고 올바른 함정 한 작동만 인정한다.</summary>
        [Test]
        public void RequestNeedsTwoUniqueHitsWithinOneCorrectActivationAndResets()
        {
            var night = Resources.Load<NightDefinition>("Campaign/Nights/Night1");
            var events = new RunEvents();
            events.TrapHit(TrapKind.Vacuum, 4, night);
            Assert.That(events.RequestBestHits, Is.Zero);
            events.TrapHit(TrapKind.Cup, 1, night);
            events.TrapHit(TrapKind.Cup, 1, night);
            Assert.That(events.RequestCompleted, Is.False);
            events.TrapHit(TrapKind.Cup, 2, night);
            Assert.That(events.RequestCompleted, Is.True);
            events.TrapHit(TrapKind.Cup, 1, night);
            Assert.That(events.RequestBestHits, Is.EqualTo(2));
            events.Reset();
            Assert.That(events.RequestCompleted, Is.False);
            Assert.That(events.RequestBestHits, Is.Zero);
        }
    }
}
