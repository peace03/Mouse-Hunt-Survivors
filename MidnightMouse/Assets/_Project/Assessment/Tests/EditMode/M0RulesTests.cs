using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MidnightMouse.Tests
{
    /// <summary>M0 측정 판에 실제로 연결되는 규칙과 기본값을 검사한다.</summary>
    public sealed class M0RulesTests
    {
        /// <summary>새 규칙과 저장된 규칙 모두 2일째 근사 구성과 새 미끼 시간을 사용한다.</summary>
        [Test]
        public void MeasurementRulesUseSecondNightPopulationAndShortBait()
        {
            var fresh = ScriptableObject.CreateInstance<AssessmentRules>();
            try
            {
                var saved = AssetDatabase.LoadAssetAtPath<AssessmentRules>("Assets/_Project/Assessment/Environment/AssessmentRules.asset");
                Assert.That(saved, Is.Not.Null);
                foreach (var rules in new[] { fresh, saved })
                {
                    Assert.That(new[] { rules.GetCatCount(0), rules.GetCatCount(60), rules.GetCatCount(120), rules.GetCatCount(180), rules.GetCatCount(240) }, Is.EqualTo(new[] { 2, 2, 3, 3, 4 }));
                    Assert.That(rules.BaitEatTime, Is.EqualTo(1.5f));
                    Assert.That(rules.NoiseUpgrade, Is.EqualTo(0.05f));
                    Assert.That(rules.MinimumNoise, Is.EqualTo(0.6f));
                }
            }
            finally { Object.DestroyImmediate(fresh); }
        }
    }
}
