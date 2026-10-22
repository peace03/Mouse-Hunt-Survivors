using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MidnightMouse.Tests
{
    /// <summary>실제 측정 씬에서 M0의 취소·목표 전환·안전 구간을 검사한다.</summary>
    public sealed class M0GameplayTests
    {
        private GameController _game;

        /// <summary>새 씬에서 중앙 Tick만 직접 제어하고 실제 에셋과 개체를 사용한다.</summary>
        [UnitySetUp]
        public IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/SampleScene.unity");
            yield return null;
            _game = Object.FindFirstObjectByType<GameController>();
            _game.StartRun(); _game.ConfirmStart();
            _game.enabled = false;
            foreach (var cat in _game.Cats) cat.SetAvailable(false);
        }

        /// <summary>실행 중 생성한 표시와 기록을 씬 수명에 맞춰 정리한다.</summary>
        [UnityTearDown]
        public IEnumerator Unload()
        {
            var scene = _game.gameObject.scene;
            SceneManager.SetActiveScene(SceneManager.CreateScene("M0Cleanup"));
            yield return SceneManager.UnloadSceneAsync(scene);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>누르기는 즉시 작동하지 않으며 이동 취소에는 쿨타임이 붙지 않는다.</summary>
        [Test]
        public void SwitchPressCancelsOnMovementAndKeepsCooldownReady()
        {
            var cup = Trap(TrapKind.Cup);
            MovePlayer(cup.transform.position);
            Assert.That(Vector3.Distance(cup.transform.position, new Vector3(cup.EffectPosition.x, cup.transform.position.y, cup.EffectPosition.z)), Is.EqualTo(6f).Within(0.01f));
            Assert.That(_game.TryUseNearestTrap(), Is.True);
            Assert.That(cup.IsActive, Is.False);
            Assert.That(cup.IsPressing, Is.True);
            cup.Tick(0.25f);
            _game.TogglePause();
            cup.Tick(10f);
            Assert.That(cup.PressProgress, Is.EqualTo(0.5f).Within(0.001f));
            _game.TogglePause();
            MovePlayer(cup.transform.position + Vector3.forward * 0.1f);
            cup.Tick(0.3f);
            Assert.That(cup.IsPressing, Is.False);
            Assert.That(cup.IsReady, Is.True);
            Assert.That(cup.CooldownRemaining, Is.Zero);
            MovePlayer(cup.transform.position);
            Assert.That(cup.TryActivate(), Is.True);
            cup.Tick(0.49f);
            Assert.That(cup.IsActive, Is.False);
            cup.Tick(0.02f);
            Assert.That(cup.IsActive, Is.True);
            Assert.That(cup.CooldownRemaining, Is.EqualTo(20f).Within(0.02f));
        }

        /// <summary>기존 8초보다 긴 경로를 끝까지 돌고 역주행 없이 충전독에서 끝난다.</summary>
        [Test]
        public void VacuumFinishesOneLoopAndNeverHitsInDockSafetyZone()
        {
            var vacuum = Trap(TrapKind.Vacuum);
            Vector3 dock = vacuum.EffectPosition;
            MovePlayer(vacuum.transform.position);
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Warp(cat, dock);
            Assert.That(vacuum.TryActivate(), Is.True);
            vacuum.Tick(0.5f);
            for (int i = 0; i < 10; i++) vacuum.Tick(0.05f);
            Assert.That(cat.Health, Is.EqualTo(3), "충전독 안에서는 타격하지 않음");
            for (int i = 0; i < 150; i++) vacuum.Tick(0.05f);
            Assert.That(vacuum.IsActive, Is.True, "8초가 지나도 경로가 남으면 작동 유지");
            vacuum.Tick(10f);
            Assert.That(vacuum.IsActive, Is.False);
            Assert.That(cat.Health, Is.EqualTo(3), "귀환 때에도 충전독 안에서는 타격하지 않음");
            Assert.That(Vector3.Distance(dock, vacuum.EffectPosition), Is.LessThan(0.01f));
            Assert.That(vacuum.CooldownRemaining, Is.GreaterThan(0f));
        }

        /// <summary>큰 프레임에서도 경로 사이의 고양이를 한 번만 맞히고 스위치를 조사한다.</summary>
        [Test]
        public void VacuumSweepsSegmentsOnceAndStunEndsAtSwitchTarget()
        {
            var vacuum = Trap(TrapKind.Vacuum);
            MovePlayer(vacuum.transform.position);
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Warp(cat, new Vector3(-3f, 0f, -4.5f));
            Assert.That(vacuum.TryActivate(), Is.True);
            vacuum.Tick(0.5f);
            vacuum.Tick(2.2f);
            Assert.That(cat.Health, Is.EqualTo(1), "한 프레임에 지나친 경로도 타격");
            Assert.That(cat.State, Is.EqualTo(CatState.Stunned));
            cat.Tick(2.1f);
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
            Assert.That((Vector3)Field(cat, "_target"), Is.EqualTo(vacuum.transform.position));
            Warp(cat, vacuum.EffectPosition);
            vacuum.Tick(0.02f);
            Assert.That(cat.Health, Is.EqualTo(1), "같은 작동에서는 중복 타격 금지");
        }

        /// <summary>미끼를 먹거나 향하는 고양이도 잡고, 내성 중에는 새 미끼를 무시한다.</summary>
        [Test]
        public void BaitEatingCanCatchAndImmunitySurvivesStun()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Vector3 baitPoint = new Vector3(1f, 0f, -3f);
            MovePlayer(baitPoint + Vector3.back * 3f);
            Assert.That(_game.TryThrowBait(baitPoint), Is.True);
            var baits = (CheeseBait[])Field(_game, "_baits");
            var bait = baits[0];
            Warp(cat, baitPoint + Vector3.left * 1f);
            bait.Tick(0.5f);
            MovePlayer(cat.transform.position + Vector3.forward * 0.5f);
            cat.Tick(0.01f);
            Assert.That(_game.Hearts, Is.EqualTo(2), "미끼로 이동 중 잡힘");
            MovePlayer(baitPoint + Vector3.back * 3f);
            Warp(cat, baitPoint);
            cat.Tick(1.1f);
            cat.Tick(0.01f);
            Assert.That(cat.State, Is.EqualTo(CatState.Eating));
            Assert.That(cat.BaitImmunityRemaining, Is.GreaterThan(29f));
            var session = (GameSession)Field(_game, "_session");
            session.Tick(1.6f);
            MovePlayer(cat.transform.position + Vector3.forward * 0.5f);
            cat.Tick(0.01f);
            Assert.That(_game.Hearts, Is.EqualTo(1), "먹는 중 잡힘");
            MovePlayer(baitPoint + Vector3.back * 4f);
            cat.ApplyTrapHit(1, 0.1f, Vector3.zero, 0f, Trap(TrapKind.Cup).transform.position);
            cat.Tick(0.2f);
            var next = baits[1];
            next.Launch(baitPoint, baitPoint);
            next.Tick(0.5f);
            Assert.That(Field(cat, "_bait"), Is.Null, "기절 후에도 미끼 내성 유지");
            cat.Tick(30f);
            _game.EmitNoise(baitPoint, 6f, NoiseKind.Bait, false, next);
            Assert.That(Field(cat, "_bait"), Is.SameAs(next));
        }

        /// <summary>잔상은 추적보다 우선하고 미끼보다 낮으며 공 소리는 호기심냥이 무시한다.</summary>
        [Test]
        public void NoisePriorityAndPlayerOriginControlChaseLoss()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Warp(cat, new Vector3(1f, 0f, -3f));
            MovePlayer(cat.transform.position + Vector3.left * 1.5f);
            cat.Tick(0.01f);
            Assert.That(cat.State, Is.EqualTo(CatState.Chase));
            MovePlayer(new Vector3(-10f, 0f, -3f));
            cat.Tick(2f);
            _game.EmitNoise(cat.transform.position, 30f, NoiseKind.Normal, false);
            cat.Tick(2.1f);
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate), "다른 곳의 일반 소리는 놓침을 초기화하지 않음");
            MovePlayer(cat.transform.position + Vector3.left * 1.5f);
            cat.Tick(0.01f);
            MovePlayer(new Vector3(-10f, 0f, -3f));
            cat.Tick(2f);
            _game.EmitNoise(_game.Player.transform.position, 30f, NoiseKind.Loud, true);
            cat.Tick(2.1f);
            Assert.That(cat.State, Is.EqualTo(CatState.Chase), "쥐 소리를 들으면 놓침 시간 초기화");
            Vector3 echo = cat.transform.position + Vector3.forward * 3f;
            _game.EmitNoise(echo, 9f, NoiseKind.Afterimage);
            MovePlayer(cat.transform.position + Vector3.left * 1.5f);
            cat.Tick(0.01f);
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
            Assert.That((Vector3)Field(cat, "_target"), Is.EqualTo(echo));
            _game.EmitNoise(Vector3.zero, 100f, NoiseKind.Ball);
            Assert.That((Vector3)Field(cat, "_target"), Is.EqualTo(echo));
        }

        /// <summary>새 동순위 소리는 목표를 바꾸지만 청각 밖 소리는 반응표까지 전달하지 않는다.</summary>
        [Test]
        public void LatestAudibleNoiseReplacesEqualPriorityTarget()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Warp(cat, new Vector3(1f, 0f, -3f));
            MovePlayer(new Vector3(-10f, 0f, -7f));
            Vector3 first = cat.transform.position + Vector3.forward;
            Vector3 second = first + Vector3.right;
            cat.ReceiveNoise(first, 4f, NoiseKind.Loud, false);
            cat.ReceiveNoise(second, 4f, NoiseKind.Normal, false);
            Assert.That(Field(cat, "_target"), Is.EqualTo(second));
            cat.ReceiveNoise(new Vector3(-10f, 0f, 7f), .1f, NoiseKind.Afterimage, false);
            Assert.That(Field(cat, "_target"), Is.EqualTo(second));
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
        }

        /// <summary>미끼는 잔상을 덮고 새 미끼로 교체되며 낮은 소리와 근접으로 취소되지 않는다.</summary>
        [Test]
        public void NewBaitReplacesOldBaitWithoutLosingPriority()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Warp(cat, new Vector3(1f, 0f, -3f));
            MovePlayer(new Vector3(-10f, 0f, -7f));
            var baits = (CheeseBait[])Field(_game, "_baits");
            Vector3 first = cat.transform.position + Vector3.forward * 2f;
            Vector3 second = first + Vector3.right;
            cat.ReceiveNoise(first, 9f, NoiseKind.Afterimage, false);
            baits[0].Launch(first, first); baits[0].Tick(.5f);
            Assert.That(Field(cat, "_bait"), Is.SameAs(baits[0]));
            baits[1].Launch(second, second); baits[1].Tick(.5f);
            cat.ReceiveNoise(Vector3.zero, 30f, NoiseKind.Loud, false);
            MovePlayer(cat.transform.position + Vector3.left * 1.5f);
            cat.Tick(.01f);
            Assert.That(Field(cat, "_bait"), Is.SameAs(baits[1]));
            Assert.That(Field(cat, "_target"), Is.EqualTo(baits[1].transform.position));
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
        }

        /// <summary>몸 소리만 발소리 성장 영향을 받고 잔상 동안 이동 소리가 나지 않는다.</summary>
        [Test]
        public void DashAfterimageKeepsRadiusAndSilencesMovement()
        {
            MovePlayer(new Vector3(0f, 0f, -5.8f));
            var player = _game.Player;
            player.UpgradeNoise(); player.UpgradeNoise();
            Assert.That(player.NoiseMultiplier, Is.EqualTo(0.9f).Within(0.001f));
            player.Tick(0.01f, Vector2.up, false, true, true);
            Assert.That(player.NoiseRadius, Is.Zero);
            var echo = (RadiusRing)Field(player, "_afterimageRing");
            Assert.That(echo.transform.localScale.x, Is.EqualTo(9f * _game.Rules.GlobalNoiseMultiplier).Within(0.001f));
            Vector3 echoPosition = echo.transform.position;
            player.Tick(0.2f, Vector2.up, false, true, false);
            Assert.That(player.NoiseRadius, Is.Zero);
            Assert.That(echo.transform.position, Is.EqualTo(echoPosition));
            player.Tick(0.31f, Vector2.up, false, true, false);
            Assert.That(player.NoiseRadius, Is.EqualTo(9f * 0.9f * _game.Rules.GlobalNoiseMultiplier).Within(0.001f));
        }

        /// <summary>고양이 퇴장이 게임 시계만 당기고 같은 보상을 중복 지급하지 않는다.</summary>
        [Test]
        public void BanishAdvancesClockWithoutReducingRealCooldowns()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            MovePlayer(new Vector3(0f, 0f, -5.8f));
            Assert.That(_game.TryThrowBait(new Vector3(1f, 0f, -3f)), Is.True);
            float cooldown = _game.BaitCooldown01;
            Assert.That(cat.ApplyTrapHit(3, 2f, Vector3.zero), Is.True);
            Assert.That(_game.Elapsed, Is.EqualTo(6f));
            Assert.That(_game.RealElapsed, Is.Zero);
            Assert.That(_game.BaitCooldown01, Is.EqualTo(cooldown));
            Assert.That(_game.CatsBanished, Is.EqualTo(1));
            Assert.That(cat.ApplyTrapHit(3, 2f, Vector3.zero), Is.False);
            Assert.That(_game.CatsBanished, Is.EqualTo(1));
        }

        /// <summary>종류가 같은 실제 씬의 함정을 찾는다.</summary>
        private TrapController Trap(TrapKind kind) { foreach (var trap in _game.Traps) if (trap.Kind == kind) return trap; throw new AssertionException("함정 없음"); }

        /// <summary>디버그도 게임 시계의 단계·부스러기와 정상 퇴장 보상을 사용한다.</summary>
        [Test]
        public void DebugKeysUseGameClockAndNormalBanishRewards()
        {
            _game.DebugAdvance(0);
            Assert.That(_game.Elapsed, Is.EqualTo(60f));
            Assert.That(_game.Stage, Is.EqualTo(2));
            Assert.That(_game.RealElapsed, Is.Zero);
            var pickups = (CheesePickup[])Field(_game, "_pickups");
            int active = 0;
            foreach (var pickup in pickups) if (pickup.gameObject.activeSelf) active++;
            Assert.That(active, Is.EqualTo(_game.Rules.MaxCrumbs));
            _game.DebugAdvance(1);
            Assert.That(_game.Elapsed, Is.EqualTo(90f));
            _game.Cats[0].SetAvailable(true);
            _game.Cats[1].SetAvailable(true);
            _game.DebugAdvance(2);
            Assert.That(_game.CatsBanished, Is.EqualTo(2));
            Assert.That(_game.Elapsed, Is.EqualTo(102f));
            Assert.That(_game.DebugNotice, Is.Not.Empty);
            _game.TogglePause();
            _game.DebugAdvance(1);
            Assert.That(_game.Elapsed, Is.EqualTo(102f));
        }

        /// <summary>다른 고양이가 먼저 먹으면 남은 고양이는 지난 미끼 목표를 내려놓는다.</summary>
        [Test]
        public void ClaimedBaitReleasesOtherCatsTarget()
        {
            var a = _game.Cats[0]; var b = _game.Cats[1];
            a.SetAvailable(true); b.SetAvailable(true);
            Vector3 point = new Vector3(1f, 0f, -3f);
            MovePlayer(new Vector3(-10f, 0f, -3f));
            Warp(a, point); Warp(b, point + Vector3.right);
            var bait = ((CheeseBait[])Field(_game, "_baits"))[0];
            bait.Launch(point, point); bait.Tick(0.5f);
            a.Tick(0.01f); b.Tick(0.01f);
            Assert.That(a.State, Is.EqualTo(CatState.Eating));
            Assert.That(Field(b, "_bait"), Is.Null);
            Assert.That(Field(b, "_targetKind"), Is.EqualTo(CatTarget.Normal), "미끼가 사라지면 일반 자리 조사로 낮아짐");
            bait.Launch(new Vector3(-10f, 0f, 8f), new Vector3(-10f, 0f, 8f));
            bait.Tick(0.5f);
            b.Tick(0.01f);
            Assert.That(Field(b, "_bait"), Is.Null, "듣지 못한 재사용 미끼를 추적하지 않음");
        }

        /// <summary>먹기를 끝낸 고양이가 순찰 상태뿐 아니라 실제 이동도 재개한다.</summary>
        [UnityTest]
        public IEnumerator FinishedBaitResumesActualMovement()
        {
            var cat = _game.Cats[0];
            cat.SetAvailable(true);
            Vector3 point = new Vector3(1f, 0f, -3f);
            MovePlayer(new Vector3(-10f, 0f, -3f));
            Warp(cat, point);
            var bait = ((CheeseBait[])Field(_game, "_baits"))[0];
            bait.Launch(point, point); bait.Tick(0.5f); cat.Tick(0.01f);
            Assert.That(cat.State, Is.EqualTo(CatState.Eating));
            float elapsed = 0f;
            while (elapsed < 4f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                bait.Tick(Time.deltaTime); cat.Tick(Time.deltaTime);
            }
            Assert.That(cat.State, Is.Not.EqualTo(CatState.Eating));
            Assert.That(Vector3.Distance(cat.transform.position, point), Is.GreaterThan(1f),
                "먹기 뒤 이동 재개: " + cat.State + " / " + cat.GetComponent<NavMeshAgent>().remainingDistance);
        }

        /// <summary>여러 고양이가 모인 미끼가 사라져도 지난 지점에 영구 고정되지 않는다.</summary>
        [UnityTest]
        public IEnumerator CatsLeaveConsumedBaitWithActualNavigation()
        {
            Vector3 point = new Vector3(1f, 0f, -3f);
            MovePlayer(new Vector3(-10f, 0f, -3f));
            for (int i = 0; i < 4; i++)
            {
                var cat = _game.Cats[i]; cat.SetAvailable(true);
                Warp(cat, point + new Vector3(i % 2 == 0 ? -1.5f : 1.5f, 0f, i < 2 ? -1.5f : 1.5f));
            }
            var bait = ((CheeseBait[])Field(_game, "_baits"))[0];
            bait.Launch(point, point); bait.Tick(0.5f);
            float elapsed = 0f;
            while (elapsed < 15f)
            {
                yield return null;
                elapsed += Time.deltaTime;
                bait.Tick(Time.deltaTime);
                for (int i = 0; i < 4; i++) _game.Cats[i].Tick(Time.deltaTime);
            }
            for (int i = 0; i < 4; i++)
            {
                var cat = _game.Cats[i];
                Assert.That(cat.State, Is.Not.EqualTo(CatState.Investigate),
                    "미끼 해제 뒤 조사 종료: " + i + " / 거리 " + cat.GetComponent<NavMeshAgent>().remainingDistance);
                Assert.That(Vector3.Distance(cat.transform.position, point), Is.GreaterThan(1f));
            }
        }

        /// <summary>함정 피격 뒤 같은 스위치로 향한 두 고양이가 서로 막혀도 조사를 마친다.</summary>
        [UnityTest]
        public IEnumerator TwoCatsLeaveSharedTrapSwitch()
        {
            foreach (var kind in new[] { TrapKind.Cup, TrapKind.Vacuum })
            {
                MovePlayer(new Vector3(10f, 0f, -7f));
                Vector3 point = Trap(kind).transform.position;
                for (int i = 0; i < 2; i++)
                {
                    var cat = _game.Cats[i]; cat.SetAvailable(true);
                    Warp(cat, point + Vector3.forward * (i == 0 ? -1.5f : 1.5f));
                    Assert.That(cat.ApplyTrapHit(1, 0.1f, Vector3.zero, 0f, point), Is.True);
                }
                yield return TickCatsFor(12f, 2);
                for (int i = 0; i < 2; i++)
                {
                    var cat = _game.Cats[i];
                    Assert.That(cat.State, Is.Not.EqualTo(CatState.Investigate),
                        kind + " 스위치 조사 종료: " + i + " / 거리 " + cat.GetComponent<NavMeshAgent>().remainingDistance);
                    Assert.That(Vector3.Distance(cat.transform.position, point), Is.GreaterThan(1f));
                    cat.SetAvailable(false);
                }
            }
        }

        /// <summary>슬롯 전체가 같은 소리 지점에 모여도 조사 상태에 영구 고정되지 않는다.</summary>
        [UnityTest]
        public IEnumerator FullCatPoolLeavesSharedNoiseTarget()
        {
            Vector3 point = new Vector3(1f, 0f, -3f);
            MovePlayer(new Vector3(-10f, 0f, -7f));
            for (int i = 0; i < 7; i++)
            {
                var cat = _game.Cats[i]; cat.SetAvailable(true);
                float angle = i * Mathf.PI * 2f / 7f;
                Warp(cat, point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f);
            }
            _game.EmitNoise(point, 10f, NoiseKind.Normal);
            yield return TickCatsFor(15f, 7);
            for (int i = 0; i < 7; i++)
                Assert.That(_game.Cats[i].State, Is.Not.EqualTo(CatState.Investigate),
                    "공동 목표 조사 종료: " + i + " / 거리 " + _game.Cats[i].GetComponent<NavMeshAgent>().remainingDistance);
        }

        /// <summary>목표까지 이동 중인 조사는 두리번 시간만 지났다고 종료하지 않고 일시정지를 지킨다.</summary>
        [UnityTest]
        public IEnumerator MovingInvestigationDoesNotTimeOutAndPauseFreezesIt()
        {
            var cat = _game.Cats[0]; cat.SetAvailable(true);
            MovePlayer(new Vector3(10f, 0f, -7f));
            Vector3 start = new Vector3(-10f, 0f, -7f);
            Vector3 target = new Vector3(10f, 0f, 5f);
            Warp(cat, start);
            _game.EmitNoise(target, 40f, NoiseKind.Normal);
            yield return TickCatsFor(3f, 1);
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
            Assert.That(Vector3.Distance(cat.transform.position, start), Is.GreaterThan(1f));
            Assert.That(Vector3.Distance(cat.transform.position, target), Is.GreaterThan(2f));
            _game.TogglePause();
            Vector3 pausedPosition = cat.transform.position;
            cat.Tick(20f);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(cat.State, Is.EqualTo(CatState.Investigate));
            Assert.That(Vector3.Distance(cat.transform.position, pausedPosition), Is.LessThan(0.05f));
            _game.TogglePause();
            yield return TickCatsFor(0.5f, 1);
            Assert.That(Vector3.Distance(cat.transform.position, pausedPosition), Is.GreaterThan(0.1f));
        }

        /// <summary>고양이 판단은 직접 진행하되 경로 이동은 실제 Unity 프레임으로 검사한다.</summary>
        private IEnumerator TickCatsFor(float seconds, int count)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                for (int i = 0; i < count; i++) _game.Cats[i].Tick(Time.deltaTime);
            }
        }

        /// <summary>승리 직전에도 F3는 대상 전체의 퇴장과 보상을 한 번씩 처리한다.</summary>
        [Test]
        public void DebugBanishAllCompletesBeforeDawnWin()
        {
            _game.Cats[0].SetAvailable(true); _game.Cats[1].SetAvailable(true);
            ((GameSession)Field(_game, "_session")).AdvanceClock(294f);
            _game.DebugAdvance(2);
            Assert.That(_game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(_game.CatsBanished, Is.EqualTo(2));
            Assert.That(_game.Cats[1].Health, Is.Zero);
        }

        /// <summary>시험에 필요한 상태만 읽고 런타임에 시험용 API를 추가하지 않는다.</summary>
        private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        /// <summary>플레이어를 수용 조건의 시작점에 배치한다.</summary>
        private void MovePlayer(Vector3 point) { var controller = _game.Player.GetComponent<CharacterController>(); controller.enabled = false; _game.Player.transform.position = point; controller.enabled = true; }

        /// <summary>실제 NavMesh 위에 고양이를 배치한다.</summary>
        private static void Warp(CatController cat, Vector3 point) { Assert.That(NavMesh.SamplePosition(point, out var hit, 0.3f, NavMesh.AllAreas), Is.True); Assert.That(cat.GetComponent<NavMeshAgent>().Warp(hit.position), Is.True); }
    }
}
