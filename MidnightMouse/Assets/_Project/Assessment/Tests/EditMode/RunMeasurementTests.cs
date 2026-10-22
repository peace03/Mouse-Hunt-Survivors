#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Globalization;
using NUnit.Framework;

namespace MidnightMouse.Tests
{
    /// <summary>판 CSV가 문화권·인용 부호·중복 종료 때문에 손상되지 않는지 검사한다.</summary>
    public sealed class RunMeasurementTests
    {
        /// <summary>지역화된 소수점과 줄바꿈을 안전하게 저장하고 한 판은 한 번만 끝낸다.</summary>
        [Test]
        public void CsvUsesInvariantNumbersEscapesFieldsAndFinishesOnce()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var session = new GameSession(); session.Start(); session.Tick(1.25f);
                var record = new RunMeasurement("test-run", "M0", "loop,\"A\"");
                record.Record(1.25f, "hit", "cat\n1");
                record.AddChaseTime(0.5f); record.AddChaseTime(float.NaN);
                string row = record.Finish(session, "title", "");
                Assert.That(row, Does.Contain("1.25"));
                Assert.That(row, Does.Contain("\"loop,\"\"A\"\"\""));
                Assert.That(row, Does.Not.Contain("\n"));
                Assert.That(row, Does.Contain("0.5"));
                Assert.That(record.Finish(session, "quit", ""), Is.Null);
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }
    }
}
#endif
