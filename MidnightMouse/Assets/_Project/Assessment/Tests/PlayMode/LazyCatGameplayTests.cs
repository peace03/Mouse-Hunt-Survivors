using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MidnightMouse.Tests
{
    /// <summary>3일째 실제 슬롯·미끼·NavMesh에서 게으름냥의 수면 주기를 검사한다.</summary>
    public sealed class LazyCatGameplayTests
    {
        private GameController _game;
        private CatController _cat;
        private CatController _other;
        /// <summary>시험에 필요한 내부 목표를 읽고 제품 공개 API는 늘리지 않는다.</summary>
        private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        /// <summary>사용자 진행과 분리된 상태에서 셋째 밤을 열고 중앙 시간만 직접 제어한다.</summary>
        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            _game.enabled = false;
            typeof(GameController).GetField("<Progress>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game, new CampaignProgress());
            for (int i = 0; i < 2; i++) _game.Progress.RecordResult(_game.Nights[i].Id, true, false, 0, 200f);
            Assert.That(_game.BeginNight(2), Is.True);
            foreach (var cat in _game.Cats)
            {
                if (!cat.IsAvailable) continue;
                if (cat.Definition.Kind != CatKind.Lazy) { cat.SetAvailable(false); continue; }
                if (_cat == null) _cat = cat; else _other = cat;
            }
            Assert.That(_other, Is.Not.Null);
            MovePlayer(new Vector3(10f, 0f, 8f));
        }

        /// <summary>시각·물리 개체를 씬과 함께 정리하고 예상 밖 로그를 허용하지 않는다.</summary>
        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = _game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("LazyCatCleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            _cat = _other = null;
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>두 수면 자리는 다르며 접촉하지 않은 조용한 접근과 단발 발소리에는 깨지 않는다.</summary>
        [Test]
        public void TwoSleepersWaitAndIgnoreQuietApproach()
        {
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            Assert.That(_other.State, Is.EqualTo(CatState.Sleeping));
            Assert.That(Vector3.Distance(_cat.transform.position, _other.transform.position), Is.GreaterThan(5f));
            Assert.That(_cat.Health, Is.EqualTo(5));
            Assert.That(_cat.ApplyTrapHit(2, 1f, Vector3.zero), Is.False);
            _game.ConfirmStart();
            MovePlayer(_cat.transform.position + Vector3.forward * 1.2f);
            _game.EmitNoise(_cat.transform.position, 30f, NoiseKind.Normal, true);
            _cat.Tick(2f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            Assert.That(_game.Hearts, Is.EqualTo(3));
            Assert.That(_cat.GetComponent<NavMeshAgent>().isStopped, Is.True);
        }

        /// <summary>기상 질주는 첫 소리 지점을 고정하고 잔상과 근접만 이를 바꾼다.</summary>
        [Test]
        public void WakeRushHasFixedSpeedTargetAndBoundedDuration()
        {
            _game.ConfirmStart();
            Vector3 first = new Vector3(1f, 0f, -3f);
            _game.EmitNoise(first, 40f, NoiseKind.Loud);
            Assert.That(_cat.State, Is.EqualTo(CatState.WakeRush));
            Assert.That(_cat.GetComponent<NavMeshAgent>().speed, Is.EqualTo(5.5f));
            _game.EmitNoise(first + Vector3.right * 3f, 40f, NoiseKind.Loud, true);
            var bait = LandBait(_cat.transform.position);
            Assert.That(Field(_cat, "_target"), Is.EqualTo(first));
            Assert.That(Field(_cat, "_bait"), Is.Null);
            _cat.Tick(6.01f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Investigate));
            _game.EmitNoise(first + Vector3.left, 40f, NoiseKind.Afterimage, true);
            Assert.That(Field(_cat, "_targetKind"), Is.EqualTo(CatTarget.Afterimage));
            MovePlayer(_cat.transform.position + Vector3.forward * 1.5f);
            _cat.Tick(.01f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Investigate), "잔상이 근접보다 우선");
            Assert.That(bait.IsAvailable, Is.True);
        }

        /// <summary>수면 피해는 두 배이고 살아남으면 기절·밀림 없이 스위치로 질주한다.</summary>
        [Test]
        public void SleepingTrapHitDoublesDamageAndRespawnReturnsThroughDoor()
        {
            _game.ConfirmStart();
            Vector3 before = _cat.transform.position;
            Vector3 target = new Vector3(1f, 0f, -3f);
            Assert.That(_cat.ApplyTrapHit(2, 3f, Vector3.right, 4f, target), Is.True);
            Assert.That(_cat.Health, Is.EqualTo(1));
            Assert.That(_cat.State, Is.EqualTo(CatState.WakeRush));
            Assert.That(_cat.transform.position, Is.EqualTo(before));
            Assert.That(Field(_cat, "_target"), Is.EqualTo(target));
            Assert.That(_game.Events.RequestBestHits, Is.Zero, "최초 수면은 자장가로 세지 않음");
            _cat.ApplyTrapHit(1, 1f, Vector3.zero, 0f, target);
            Assert.That(_cat.State, Is.EqualTo(CatState.Retreat));
            Assert.That(_game.CatsBanished, Is.EqualTo(1));
            _cat.Tick(_game.Rules.RetreatTimeout + .1f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Respawning));
            _cat.Tick(_game.Rules.RespawnDelay + .1f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Returning));
            Assert.That(_cat.Health, Is.EqualTo(5));
            Assert.That(Vector3.Distance(_cat.transform.position, (Vector3)Field(_cat, "_spawnPosition")), Is.LessThan(.5f));
            Assert.That(Field(_cat, "_target"), Is.EqualTo(Field(_cat, "_sleepPosition")));
        }

        /// <summary>미끼를 끝까지 먹은 자리에서 자며 첫 함정 피격만 자장가에 누적한다.</summary>
        [Test]
        public void BaitSleepCountsTwoSeparateTrapHitsWithoutDoubleCounting()
        {
            _game.ConfirmStart();
            foreach (var cat in new[] { _cat, _other })
            {
                Vector3 point = new Vector3(1f, 0f, -3f);
                PrepareAwake(cat, point);
                LandBait(point);
                cat.Tick(.01f);
                Assert.That(cat.State, Is.EqualTo(CatState.Eating));
                cat.Tick(1.49f);
                Assert.That(cat.State, Is.EqualTo(CatState.Eating));
                cat.Tick(.02f);
                Assert.That(cat.State, Is.EqualTo(CatState.Sleeping));
                Assert.That(cat.BaitImmunityRemaining, Is.GreaterThan(28f));
                Assert.That(Vector3.Distance(point, cat.transform.position), Is.LessThan(.5f));
                cat.ApplyTrapHit(1, 1f, Vector3.zero, 0f, point + Vector3.right * 3f);
                int count = _game.Events.RequestBestHits;
                cat.ApplyTrapHit(1, 1f, Vector3.zero, 0f, point);
                Assert.That(_game.Events.RequestBestHits, Is.EqualTo(count));
                cat.SetAvailable(false);
            }
            Assert.That(_game.Events.RequestBestHits, Is.EqualTo(2));
            Assert.That(_game.Events.RequestCompleted, Is.True);
            _game.RecordTrapHit(TrapKind.Cup, 5);
            Assert.That(_game.Events.RequestBestHits, Is.EqualTo(2));
        }

        /// <summary>근접 추적은 미끼를 먹기 전 취소하며 먹는 중에도 플레이어를 잡는다.</summary>
        [Test]
        public void ProximityOverridesBaitButEatingStillCatches()
        {
            _game.ConfirmStart();
            Vector3 point = new Vector3(1f, 0f, -3f);
            PrepareAwake(_cat, point);
            var bait = LandBait(point);
            MovePlayer(point + Vector3.forward * 1.5f);
            _cat.Tick(.01f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Chase));
            Assert.That(bait.IsAvailable, Is.True);
            MovePlayer(new Vector3(10f, 0f, 8f));
            _cat.Tick(_game.Rules.LoseTime + .1f);
            _game.EmitNoise(point, 30f, NoiseKind.Bait, false, bait);
            _cat.Tick(.01f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Eating));
            MovePlayer(_cat.transform.position);
            _cat.Tick(.01f);
            Assert.That(_game.Hearts, Is.EqualTo(2));
        }

        /// <summary>5단계에서 잠·귀가·미끼 이동을 끝내며 먹던 개체도 다시 잠들지 않는다.</summary>
        [Test]
        public void FinalStageWakesSleepersAndPreventsFurtherBaitSleep()
        {
            _game.ConfirmStart();
            PrepareAwake(_other, new Vector3(1f, 0f, -3f));
            LandBait(_other.transform.position);
            _other.Tick(.01f);
            Assert.That(_other.State, Is.EqualTo(CatState.Eating));
            ((GameSession)Field(_game, "_session")).AdvanceClock(_game.StageSeconds * 4f);
            _cat.ApplyStageRules();
            Assert.That(_cat.State, Is.EqualTo(CatState.Patrol));
            _other.Tick(2f);
            Assert.That(_other.State, Is.EqualTo(CatState.Patrol));
            LandBait(_cat.transform.position);
            Assert.That(Field(_cat, "_bait"), Is.Null);
            _cat.SetAvailable(false);
            _cat.SetAvailable(true);
            Assert.That(_cat.State, Is.EqualTo(CatState.Patrol));
        }

        /// <summary>질주 뒤 조사한 고양이는 실제 NavMesh로 귀가하고 일시정지는 이동을 멈춘다.</summary>
        [UnityTest]
        public IEnumerator ReturnsHomeAndSleepsAfterObservation()
        {
            _game.ConfirmStart();
            Vector3 home = _cat.transform.position;
            Vector3 point = home + Vector3.right * 1.5f;
            _game.EmitNoise(point, 40f, NoiseKind.Loud);
            yield return null;
            _game.TogglePause();
            Vector3 paused = _cat.transform.position;
            _cat.Tick(10f);
            yield return new WaitForSeconds(.2f);
            Assert.That(_cat.State, Is.EqualTo(CatState.WakeRush));
            Assert.That(Vector3.Distance(paused, _cat.transform.position), Is.LessThan(.01f));
            _game.TogglePause();
            bool returned = false;
            float deadline = Time.realtimeSinceStartup + 18f;
            while (Time.realtimeSinceStartup < deadline && _cat.State != CatState.Sleeping)
            {
                _cat.Tick(Time.deltaTime);
                returned |= _cat.State == CatState.Returning;
                yield return null;
            }
            Assert.That(returned, Is.True);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            Assert.That(Vector3.Distance(home, _cat.transform.position), Is.LessThan(.4f));
        }

        /// <summary>실제 발소리 원 안에서만 시간이 쌓이며 남은 수면량을 원호로 표시한다.</summary>
        [TestCase(false, 3f)]
        [TestCase(true, 1.5f)]
        public void FootstepsDrainGaugeThenWakeIntoChase(bool run, float seconds)
        {
            PrepareSleepingContactTest();
            var gauge = _cat.transform.Find("수면 게이지").GetComponent<LineRenderer>();
            float step = seconds / 20f;
            for (int i = 0; i < 10; i++) StepFootsteps(step, i, run);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.EqualTo(.5f).Within(.01f));
            Assert.That(gauge.enabled, Is.True);
            Assert.That(gauge.loop, Is.False);
            Assert.That(Vector3.Distance(gauge.GetPosition(0), gauge.GetPosition(gauge.positionCount - 1)), Is.EqualTo(2f).Within(.01f));
            Assert.That(gauge.transform.localScale.x, Is.EqualTo(.75f), "게이지는 반경 대신 원호가 줄어듦");
            for (int i = 10; i < 19; i++) StepFootsteps(step, i, run);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            StepFootsteps(step + .01f, 19, run);
            Assert.That(_cat.State, Is.EqualTo(CatState.Chase));
            Assert.That(Field(_cat, "_targetKind"), Is.EqualTo(CatTarget.Player));
            Assert.That(gauge.enabled, Is.False);
            MovePlayer(new Vector3(3f, 0f, -5f));
            _cat.Tick(.1f);
            Assert.That(Field(_cat, "_target"), Is.EqualTo(_game.Player.transform.position));
        }

        /// <summary>소음에서 벗어나거나 조용해지면 연속 노출이 끊기고 게이지가 복구된다.</summary>
        [TestCase("outside")]
        [TestCase("sneak")]
        [TestCase("stop")]
        public void InterruptedFootstepsRestoreSleepGauge(string interruption)
        {
            PrepareSleepingContactTest();
            StepFootsteps(.1f, 0, false);
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.GreaterThan(0f));
            if (interruption == "outside") MovePlayer(new Vector3(1f, 0f, -9f));
            _game.Player.Tick(.1f, interruption == "stop" ? Vector2.zero : Vector2.left, interruption == "sneak", false, false);
            _cat.Tick(.1f);
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.Zero);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            var gauge = _cat.transform.Find("수면 게이지").GetComponent<LineRenderer>();
            Assert.That(Vector3.Distance(gauge.GetPosition(0), gauge.GetPosition(gauge.positionCount - 1)), Is.LessThan(.01f));
        }

        /// <summary>카드로 줄어든 원 바깥은 누적하지 않고 청각 배율로 범위를 부풀리지 않는다.</summary>
        [Test]
        public void FootstepRangeMatchesUpgradedVisibleCircle()
        {
            PrepareSleepingContactTest();
            float originalRadius = 5f * _game.Rules.GlobalNoiseMultiplier;
            _game.Player.UpgradeNoise();
            float reducedRadius = originalRadius * _game.Player.NoiseMultiplier;
            MovePlayer(new Vector3(1f, 0f, -3f - (originalRadius + reducedRadius) * .5f));
            for (int i = 0; i < 32; i++) StepFootsteps(.1f, i, false);
            Assert.That(_game.Player.NoiseRadius, Is.EqualTo(reducedRadius).Within(.01f));
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.Zero);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            MovePlayer(new Vector3(1f, 0f, -3f - reducedRadius + .2f));
            StepFootsteps(.1f, 0, false);
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.GreaterThan(0f));
        }

        /// <summary>살금살금은 게이지를 깎지 않지만 실제 몸 반경까지 접근하면 즉시 추적한다.</summary>
        [Test]
        public void SilentBodyContactWakesAndPauseDoesNotAdvanceSleep()
        {
            PrepareSleepingContactTest();
            MovePlayer(new Vector3(1f, 0f, -3.9f));
            _game.TogglePause();
            _game.Player.Tick(5f, Vector2.up, true, false, false);
            _cat.Tick(5f);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.Zero);
            _game.TogglePause();
            for (int i = 0; i < 2; i++)
            {
                _game.Player.Tick(.05f, Vector2.up, true, false, false);
                _cat.Tick(.05f);
                Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
            }
            _game.Player.Tick(.05f, Vector2.up, true, false, false);
            _cat.Tick(.05f);
            Assert.That(_game.Player.SleepDisturbance, Is.Null);
            Assert.That(_cat.State, Is.EqualTo(CatState.Chase));
            Assert.That(_game.Hearts, Is.EqualTo(3), "깨어난 프레임에는 바로 피해를 주지 않음");
        }

        /// <summary>대시는 범위 안 잠든 고양이를 깨워 추적시키고 깨어 있던 고양이는 잔상으로 유인한다.</summary>
        [Test]
        public void DashWakesOnlyWithinItsCircleAndPreservesAwakeDecoy()
        {
            PrepareSleepingContactTest();
            Assert.That(_other.GetComponent<NavMeshAgent>().Warp(new Vector3(-10f, 0f, 7f)), Is.True);
            _game.Player.Tick(.02f, Vector2.right, false, false, true);
            Assert.That(_cat.State, Is.EqualTo(CatState.Chase));
            Assert.That(_other.State, Is.EqualTo(CatState.Sleeping));
            _game.Player.ResetRun();
            MovePlayer(new Vector3(1f, 0f, -5f));
            Vector3 afterimage = _game.Player.transform.position;
            _game.Player.Tick(.02f, Vector2.right, false, false, true);
            Assert.That(_cat.State, Is.EqualTo(CatState.Investigate));
            Assert.That(Field(_cat, "_targetKind"), Is.EqualTo(CatTarget.Afterimage));
            Assert.That(Field(_cat, "_target"), Is.EqualTo(afterimage));
        }

        /// <summary>일시정지 중 누적량을 보존하고 재시작은 수면·발소리·게이지를 초기화한다.</summary>
        [Test]
        public void PausedProgressIsFrozenAndRetryStartsWithFullGauge()
        {
            PrepareSleepingContactTest();
            StepFootsteps(.1f, 0, false);
            float progress = (float)Field(_cat, "_wakeProgress");
            _game.TogglePause();
            _game.Player.Tick(10f, Vector2.right, false, true, true);
            _cat.Tick(10f);
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.EqualTo(progress));
            _game.RestartRun();
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.Player.SleepDisturbance, Is.Null);
            Assert.That((float)Field(_cat, "_wakeProgress"), Is.Zero);
            Assert.That(_cat.State, Is.EqualTo(CatState.Sleeping));
        }

        /// <summary>가구 없는 바닥에서 실제 이동 방향과 두 몸의 거리를 고정한다.</summary>
        private void PrepareSleepingContactTest()
        {
            _game.ConfirmStart();
            Assert.That(_cat.GetComponent<NavMeshAgent>().Warp(new Vector3(1f, 0f, -3f)), Is.True);
            MovePlayer(new Vector3(1f, 0f, -5f));
            typeof(MouseController).GetField("_cameraForward", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game.Player, Vector3.forward);
            typeof(MouseController).GetField("_cameraRight", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_game.Player, Vector3.right);
        }

        /// <summary>소음 상태를 꾸미지 않고 좌우로 실제 움직여 연속 노출을 만든다.</summary>
        private void StepFootsteps(float delta, int index, bool run)
        {
            _game.Player.Tick(delta, index % 2 == 0 ? Vector2.right : Vector2.left, false, run, false);
            _cat.Tick(delta);
        }

        /// <summary>수면 중에 이동시킨 뒤 자연스러운 기상 제한 시간을 지나 조사 상태로 만든다.</summary>
        private void PrepareAwake(CatController cat, Vector3 point)
        {
            Assert.That(cat.GetComponent<NavMeshAgent>().Warp(point), Is.True);
            cat.ReceiveNoise(point + Vector3.right * 2f, 40f, NoiseKind.Loud, false);
            cat.Tick(cat.Definition.WakeDuration + .1f);
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
        }

        /// <summary>기존 미끼 풀에서 실제 착지 소리와 소유권을 사용한다.</summary>
        private CheeseBait LandBait(Vector3 point)
        {
            foreach (var bait in (CheeseBait[])Field(_game, "_baits"))
                if (!bait.gameObject.activeSelf)
                {
                    bait.Launch(point, point);
                    bait.Tick(_game.Rules.BaitFlightTime + .01f);
                    return bait;
                }
            Assert.Fail("시험용 미끼 슬롯 부족");
            return null;
        }

        /// <summary>실제 플레이어와의 거리만 바꾸고 이동 시뮬레이션을 유지한다.</summary>
        private void MovePlayer(Vector3 point)
        {
            var controller = _game.Player.GetComponent<CharacterController>();
            controller.enabled = false;
            _game.Player.transform.position = point;
            controller.enabled = true;
        }
    }
}
