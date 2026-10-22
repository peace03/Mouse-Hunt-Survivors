using System;
using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>캠페인 원기록의 조건별 누적과 현재 기준 해금을 검증한다.</summary>
    public sealed class CampaignProgressTests
    {
        /// <summary>처음에는 첫 밤만 열리고 기록과 별은 비어 있다.</summary>
        [Test]
        public void NewCampaignOnlyUnlocksFirstNight()
        {
            var progress = new CampaignProgress();
            Assert.That(progress.version, Is.EqualTo(1));
            Assert.That(progress.Find("night-1"), Is.Null);
            Assert.That(progress.Stars("night-1", 3), Is.Zero);
            Assert.That(progress.IsUnlocked(new[] { "night-1", "night-2" }, 0), Is.True);
            Assert.That(progress.IsUnlocked(new[] { "night-1", "night-2" }, 1), Is.False);
        }

        /// <summary>부탁과 퇴장 목표는 다른 생존 판에서 달성해도 별 세 개가 된다.</summary>
        [Test]
        public void IndependentSurvivingRunsAccumulateStarsAndBestTime()
        {
            var progress = new CampaignProgress();
            progress.RecordResult("night-1", true, true, 1, 240f);
            Assert.That(progress.Stars("night-1", 3), Is.EqualTo(2));
            progress.RecordResult("night-1", true, false, 4, 210f);
            progress.RecordResult("night-1", true, false, 2, 260f);
            Assert.That(progress.Stars("night-1", 3), Is.EqualTo(3));
            Assert.That(progress.Stars("night-1", 5), Is.EqualTo(2));
            Assert.That(progress.Find("night-1").bestSeconds, Is.EqualTo(210f));
            Assert.That(progress.Find("night-1").mostBanished, Is.EqualTo(4));
            Assert.That(progress.nights, Has.Count.EqualTo(1));
        }

        /// <summary>패배한 판의 부탁·퇴장·시간은 새 기록이나 기존 보상에 반영되지 않는다.</summary>
        [Test]
        public void DefeatCannotCreateOrImproveSurvivalRecords()
        {
            var progress = new CampaignProgress();
            progress.RecordResult("night-2", false, true, 100, 1f);
            Assert.That(progress.Find("night-2"), Is.Null);
            progress.RecordResult("night-1", true, false, 1, 240f);
            progress.RecordResult("night-1", false, true, 100, 1f);
            var record = progress.Find("night-1");
            Assert.That(record.requestCompleted, Is.False);
            Assert.That(record.mostBanished, Is.EqualTo(1));
            Assert.That(record.bestSeconds, Is.EqualTo(240f));
            Assert.That(progress.Stars("night-1", 3), Is.EqualTo(1));
        }

        /// <summary>밤 순서를 바꿔도 원기록은 같은 ID에 남고 새 직전 밤으로 해금을 계산한다.</summary>
        [Test]
        public void ReorderingNightIdsPreservesRecordsAndRecomputesUnlocks()
        {
            var progress = new CampaignProgress();
            progress.RecordResult("night-1", true, false, 3, 200f);
            Assert.That(progress.IsUnlocked(new[] { "night-1", "night-2", "night-3" }, 1), Is.True);
            Assert.That(progress.IsUnlocked(new[] { "night-2", "night-3", "night-1" }, 1), Is.False);
            Assert.That(progress.Find("night-1").bestSeconds, Is.EqualTo(200f));
            Assert.That(progress.Find("night-2"), Is.Null);
        }

        /// <summary>음수 또는 유한하지 않은 시간은 원기록을 변경하기 전에 거부한다.</summary>
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidTimeCannotMutateProgress(float time)
        {
            var progress = new CampaignProgress();
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.RecordResult("night-1", true, true, 3, time));
            Assert.That(progress.nights, Is.Empty);
        }

        /// <summary>잘못된 ID·수량·목표·인덱스를 경계에서 거부한다.</summary>
        [Test]
        public void InvalidIdentifiersAndCountsAreRejected()
        {
            var progress = new CampaignProgress();
            Assert.Throws<ArgumentException>(() => progress.RecordResult(" ", true, true, 3, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.RecordResult("night-1", true, true, -1, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Stars("night-1", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.IsUnlocked(new[] { "night-1" }, 1));
            Assert.That(progress.nights, Is.Empty);
        }
    }
}
