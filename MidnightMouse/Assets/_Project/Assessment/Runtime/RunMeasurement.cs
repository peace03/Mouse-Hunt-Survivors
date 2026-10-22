#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.Text;

namespace MidnightMouse
{
    /// <summary>개발 중 한 판의 사건을 모아 종료 때 CSV 한 행으로 만든다.</summary>
    public sealed class RunMeasurement
    {
        public const string Header = "run_id,profile,scenario,outcome,real_seconds,game_seconds,banished,levelups,chase_seconds,death_cause,debug_used,events";
        private readonly string _runId;
        private readonly string _profile;
        private readonly string _scenario;
        private readonly StringBuilder _events = new StringBuilder();
        private float _chaseSeconds;
        private bool _finished;
        private bool _debugUsed;

        /// <summary>실행 식별자와 측정 시나리오를 판 시작 때 고정한다.</summary>
        public RunMeasurement(string runId, string profile, string scenario)
        {
            _runId = runId; _profile = profile; _scenario = scenario;
        }

        /// <summary>상태 전환이나 타격 때만 실제 시각과 사건 내용을 기록한다.</summary>
        public void Record(float realTime, string kind, string detail)
        {
            if (_finished) return;
            if (_events.Length > 0) _events.Append(';');
            _events.Append(Number(realTime)).Append('|').Append(kind).Append('|').Append(detail);
            if (kind == "debug") _debugUsed = true;
        }

        /// <summary>하나 이상의 고양이가 추적한 실제 시간을 중복 없이 누적한다.</summary>
        public void AddChaseTime(float delta)
        {
            if (!_finished && delta > 0f && !float.IsNaN(delta) && !float.IsInfinity(delta)) _chaseSeconds += delta;
        }

        /// <summary>판당 한 행만 만들고 재시작·종료의 중복 저장을 막는다.</summary>
        public string Finish(GameSession session, string outcome, string deathCause)
        {
            if (_finished) return null;
            _finished = true;
            return string.Join(",", Quote(_runId), Quote(_profile), Quote(_scenario), Quote(outcome),
                Number(session.RealElapsed), Number(session.Elapsed), session.CatsBanished.ToString(CultureInfo.InvariantCulture),
                (session.Level - 1).ToString(CultureInfo.InvariantCulture), Number(_chaseSeconds), Quote(deathCause),
                _debugUsed ? "1" : "0", Quote(_events.ToString()));
        }

        /// <summary>CSV 수치의 소수점을 실행 PC의 지역 설정과 분리한다.</summary>
        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>인용 부호와 줄바꿈을 이스케이프해 한 판이 한 물리 행에 남게 한다.</summary>
        private static string Quote(string value) => "\"" + (value ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\"\"") + "\"";
    }
}
#endif
