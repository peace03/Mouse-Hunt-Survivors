using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace MidnightMouse.Tests
{
    /// <summary>실제 씬의 밤 선택·함정 부탁·재시작·결과 저장 연결을 검증한다.</summary>
    public sealed class CampaignGameplayTests
    {
        private GameController _game;
        private string _path;

        /// <summary>사용자 진행 파일 대신 시험 전용 임시 파일을 연결한다.</summary>
        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            _game.enabled = false;
            _path = Path.Combine(Application.temporaryCachePath, "CampaignTests", Guid.NewGuid().ToString("N") + ".json");
            Set("_campaignStore", new CampaignStore(_path));
            Set("<Progress>k__BackingField", new CampaignProgress());
        }

        /// <summary>시험이 만든 진행 파일과 씬만 정리한다.</summary>
        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = _game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("CampaignCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            foreach (string suffix in new[] { "", ".bak", ".tmp" }) if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>첫날은 한 마리와 컵으로 시작하며 미해금 청소기는 작동하지 않는다.</summary>
        [Test]
        public void FirstNightUsesDataAndCannotActivateLockedVacuum()
        {
            Assert.That(_game.BeginNight(1), Is.False);
            Assert.That(_game.BeginNight(0), Is.True); _game.ConfirmStart();
            Assert.That(_game.Duration, Is.EqualTo(225));
            Assert.That(_game.BaitImmunity, Is.Zero);
            int count = 0;
            foreach (var cat in _game.Cats) if (cat.IsAvailable) count++;
            Assert.That(count, Is.EqualTo(1));
            Assert.That(_game.Cats[0].Definition, Is.SameAs(_game.CurrentNight.Cats[0].kind));
            var vacuum = Trap(TrapKind.Vacuum);
            MovePlayer(vacuum.transform.position);
            Assert.That(vacuum.IsNightEnabled, Is.False);
            Assert.That(vacuum.TryActivate(), Is.False);
            Assert.That(Trap(TrapKind.Cup).IsNightEnabled, Is.True);
        }

        /// <summary>실제 컵의 고유 타격이 부탁에 반영되고 생존해야 해금·별로 저장된다.</summary>
        [Test]
        public void CupRequestSurvivalUnlockAndRestartUseRealActors()
        {
            Assert.That(_game.BeginNight(0), Is.True); _game.ConfirmStart();
            var cup = Trap(TrapKind.Cup);
            MovePlayer(cup.transform.position);
            for (int i = 0; i < 2; i++)
            {
                var cat = _game.Cats[i];
                cat.SetAvailable(true);
                cat.ApplyTrapHit(2, .1f, Vector3.zero);
                Assert.That(NavMesh.SamplePosition(cup.EffectPosition + Vector3.right * i * .3f, out var hit, 1f, NavMesh.AllAreas), Is.True);
                Assert.That(cat.GetComponent<NavMeshAgent>().Warp(hit.position), Is.True);
            }
            var session = (GameSession)typeof(GameController).GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_game);
            session.AdvanceClock(223);
            Assert.That(cup.TryActivate(), Is.True);
            cup.Tick(_game.Rules.SwitchPressDuration);
            cup.Tick(_game.Rules.CupDelay);
            Assert.That(_game.Events.RequestCompleted, Is.True);
            Assert.That(_game.CatsBanished, Is.EqualTo(2), "아침 직전 동시 타격도 두 마리 모두 정산");
            typeof(GameController).GetMethod("SyncPhase", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_game, null);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(_game.Progress.Stars(_game.CurrentNight.Id, 3), Is.EqualTo(2));
            Assert.That(_game.IsNightUnlocked(1), Is.True);
            Assert.That(new CampaignStore(_path).Load().IsUnlocked(new[] { _game.Nights[0].Id, _game.Nights[1].Id }, 1), Is.True);
            _game.ReturnToTitle();
            Assert.That(_game.BeginNight(1), Is.True); _game.ConfirmStart();
            Assert.That(_game.Duration, Is.EqualTo(300));
            Assert.That(_game.BaitImmunity, Is.EqualTo(30));
            Assert.That(Trap(TrapKind.Vacuum).IsNightEnabled, Is.True);
            Assert.That(_game.Events.RequestCompleted, Is.False);
            _game.Player.UpgradeNoise();
            Assert.That(_game.Player.NoiseMultiplier, Is.EqualTo(.95f).Within(.001f));
            _game.RestartRun(); _game.ConfirmStart();
            Assert.That(_game.Player.NoiseMultiplier, Is.EqualTo(1));
            Assert.That(_game.Events.RequestBestHits, Is.Zero);
            Assert.That(_game.Elapsed, Is.Zero);
            Assert.That(_game.RealElapsed, Is.Zero);
            Assert.That(_game.CurrentNight, Is.SameAs(_game.Nights[1]));
        }

        /// <summary>규칙 배율은 기본값에 한 번만 곱하며 재시작해도 누적하지 않는다.</summary>
        [Test]
        public void NightMultiplierDoesNotMutateSharedCatDataOrStackOnRestart()
        {
            var copy = Object.Instantiate(_game.Nights[0]);
            try
            {
                typeof(NightDefinition).GetField("_chaseMultiplier", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(copy, 1.1f);
                _game.Nights[0] = copy;
                _game.BeginNight(0); _game.ConfirmStart();
                var kind = copy.Cats[0].kind;
                float baseline = kind.Chase;
                Assert.That(_game.ChaseSpeed(kind), Is.EqualTo(baseline * 1.1f).Within(.001f));
                _game.RestartRun(); _game.ConfirmStart();
                Assert.That(_game.ChaseSpeed(kind), Is.EqualTo(baseline * 1.1f).Within(.001f));
                Assert.That(kind.Chase, Is.EqualTo(baseline));
            }
            finally { Object.Destroy(copy); }
        }

        /// <summary>실제 씬에 연결된 함정만 고른다.</summary>
        private TrapController Trap(TrapKind kind) { foreach (var trap in _game.Traps) if (trap.Kind == kind) return trap; throw new InvalidOperationException(); }
        /// <summary>공개 시험 API를 늘리지 않고 저장 위치만 분리한다.</summary>
        private void Set(string name, object value) => typeof(GameController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, value);
        /// <summary>시작 위치를 지정하고 실제 상호작용 거리를 사용한다.</summary>
        private void MovePlayer(Vector3 point) { var c = _game.Player.GetComponent<CharacterController>(); c.enabled = false; _game.Player.transform.position = point; c.enabled = true; }
    }
}
