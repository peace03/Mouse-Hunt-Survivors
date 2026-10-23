using System;
using System.IO;
using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>임시 디렉터리에서 캠페인 저장의 왕복·손상 보존·교체 실패를 검증한다.</summary>
    public sealed class CampaignStoreTests
    {
        private string _directory;
        private string _path;

        /// <summary>실제 사용자 저장과 분리된 테스트 전용 경로를 만든다.</summary>
        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "MidnightMouse-CampaignTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "campaign.json");
        }

        /// <summary>이번 테스트가 만든 파일만 전용 디렉터리에서 제거한다.</summary>
        [TearDown]
        public void TearDown() => Directory.Delete(_directory, true);

        /// <summary>첫 실행은 오류 없이 시작하고 플래그·알 수 없는 밤 ID를 재로드한다.</summary>
        [Test]
        public void MissingFileAndRoundTripPreserveFlagsAndUnknownNightIds()
        {
            var store = new CampaignStore(_path);
            var progress = store.Load();
            Assert.That(store.Error, Is.Null);
            Assert.That(progress.nights, Is.Empty);
            progress.introSeen = true;
            progress.tutorialSeen = 5;
            progress.RecordResult("retired-night", true, true, 7, 180.5f);
            Assert.That(store.Save(progress), Is.True, store.Error);
            var loaded = new CampaignStore(_path).Load();
            Assert.That(loaded.introSeen, Is.True);
            Assert.That(loaded.tutorialSeen, Is.EqualTo(5));
            Assert.That(loaded.Find("retired-night").mostBanished, Is.EqualTo(7));
            Assert.That(loaded.Find("retired-night").bestSeconds, Is.EqualTo(180.5f));
            Assert.That(loaded.Stars("retired-night", 7), Is.EqualTo(3));
            StringAssert.DoesNotContain("stars", File.ReadAllText(_path));
        }

        /// <summary>종류 기록이 없는 v1 저장도 읽고 실제 만난 종류만 추가해 다시 읽는다.</summary>
        [Test]
        public void OldSaveKeepsRecordsAndPersistsOnlyEncounteredCats()
        {
            File.WriteAllText(_path, "{\"version\":1,\"introSeen\":true,\"tutorialSeen\":5,\"nights\":[{\"nightId\":\"night-1\",\"survived\":true,\"bestSeconds\":200,\"mostBanished\":3}]}");
            var store = new CampaignStore(_path);
            var progress = store.Load();
            Assert.That(store.Error, Is.Null);
            Assert.That(progress.HasEncountered(CatKind.Curious), Is.False);
            Assert.That(progress.RecordEncounter(CatKind.Lazy), Is.True);
            Assert.That(progress.RecordEncounter(CatKind.Lazy), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.RecordEncounter((CatKind)32));
            Assert.That(store.Save(progress), Is.True, store.Error);
            var loaded = new CampaignStore(_path).Load();
            Assert.That(loaded.HasEncountered(CatKind.Lazy), Is.True);
            Assert.That(loaded.HasEncountered(CatKind.Curious), Is.False);
            Assert.That(loaded.HasEncountered(CatKind.Hunter), Is.False);
            Assert.That(loaded.introSeen && loaded.tutorialSeen == 5, Is.True);
            Assert.That(loaded.Find("night-1").mostBanished, Is.EqualTo(3));
        }

        /// <summary>반복 저장은 원본을 교체하고 바로 이전 정상 파일 한 개만 보관한다.</summary>
        [Test]
        public void AtomicReplacementRetainsOnePreviousBackupAndNoTemporaryFiles()
        {
            var store = new CampaignStore(_path);
            var progress = new CampaignProgress();
            Assert.That(store.Save(progress), Is.True, store.Error);
            progress.RecordResult("night-1", true, false, 1, 240f);
            Assert.That(store.Save(progress), Is.True, store.Error);
            var previous = File.ReadAllText(_path);
            progress.RecordResult("night-1", true, true, 3, 200f);
            Assert.That(store.Save(progress), Is.True, store.Error);
            Assert.That(File.ReadAllText(_path + ".bak"), Is.EqualTo(previous));
            Assert.That(new CampaignStore(_path).Load().Find("night-1").bestSeconds, Is.EqualTo(200f));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(2));
        }

        /// <summary>손상·상위 버전·필드 누락·불가능한 기록은 새 진행으로 덮어쓰지 않는다.</summary>
        [TestCase("broken json")]
        [TestCase("")]
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("[]")]
        [TestCase("{\"version\":2,\"nights\":[]}")]
        [TestCase("{\"version\":1}")]
        [TestCase("{\"version\":1,\"tutorialSeen\":-1,\"nights\":[]}")]
        [TestCase("{\"version\":1,\"encounteredCats\":-1,\"nights\":[]}")]
        [TestCase("{\"version\":1,\"encounteredCats\":8,\"nights\":[]}")]
        [TestCase("{\"version\":1,\"nights\":[null]}")]
        [TestCase("{\"version\":1,\"nights\":[{\"nightId\":\"a\",\"survived\":true,\"bestSeconds\":-1}]}")]
        [TestCase("{\"version\":1,\"nights\":[{\"nightId\":\"a\",\"survived\":true,\"bestSeconds\":1,\"mostBanished\":-1}]}")]
        [TestCase("{\"version\":1,\"nights\":[{\"nightId\":\"a\",\"survived\":true,\"bestSeconds\":1},{\"nightId\":\"a\",\"survived\":true,\"bestSeconds\":2}]}")]
        [TestCase("{\"version\":1,\"nights\":[{\"nightId\":\"a\",\"requestCompleted\":true,\"bestSeconds\":-1}]}")]
        public void InvalidExistingFileBlocksLoadAndSaveWithoutOverwrite(string original)
        {
            File.WriteAllText(_path, original);
            var store = new CampaignStore(_path);
            Assert.That(store.Load().nights, Is.Empty);
            Assert.That(store.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(store.Save(new CampaignProgress()), Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            Assert.That(new CampaignStore(_path).Save(new CampaignProgress()), Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        }

        /// <summary>편집된 메모리의 비정상 수치는 기존 정상 저장을 훼손하지 않는다.</summary>
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-2f)]
        public void InvalidInMemoryValuesDoNotReplaceValidFile(float seconds)
        {
            var store = new CampaignStore(_path);
            Assert.That(store.Save(new CampaignProgress()), Is.True);
            var original = File.ReadAllText(_path);
            var progress = new CampaignProgress();
            progress.RecordResult("night-1", true, true, 3, 200f);
            progress.Find("night-1").bestSeconds = seconds;
            Assert.That(store.Save(progress), Is.False);
            Assert.That(store.Error, Is.Not.Null.And.Not.Empty);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
        }

        /// <summary>잠긴 기존 파일에서 저장이 실패해도 원본 바이트와 임시 파일 정리를 보장한다.</summary>
        [Test]
        public void LockedDestinationFailsWithoutLosingOriginal()
        {
            var store = new CampaignStore(_path);
            Assert.That(store.Save(new CampaignProgress()), Is.True);
            var original = File.ReadAllText(_path);
            using (new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var progress = new CampaignProgress { introSeen = true };
                Assert.That(store.Save(progress), Is.False);
                Assert.That(store.Error, Is.Not.Null.And.Not.Empty);
            }
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1));
        }

        /// <summary>로드 후 파일이 손상된 경우에도 저장 직전 재검증으로 보존한다.</summary>
        [Test]
        public void CorruptionAfterLoadIsDetectedBeforeReplacement()
        {
            var store = new CampaignStore(_path);
            Assert.That(store.Save(new CampaignProgress()), Is.True);
            var progress = store.Load();
            File.WriteAllText(_path, "external corruption");
            Assert.That(store.Save(progress), Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo("external corruption"));
        }
    }
}
