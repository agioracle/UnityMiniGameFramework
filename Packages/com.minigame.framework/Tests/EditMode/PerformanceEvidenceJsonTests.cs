using System;
using System.Text.RegularExpressions;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class PerformanceEvidenceJsonTests
    {
        private static string BudgetJson() => JsonUtility.ToJson(new PerformanceEvidenceAudit.Budget
        {
            environmentId = "json-rule-fixture", loadVersion = "fixture-v1", frozen = true,
            approvedBy = "test fixture only", approvedUtc = "2026-10-21T00:00:00Z",
            maxFrameP95Milliseconds = 2, maxFrameP99Milliseconds = 3, maxColdStartP95Seconds = 4,
            maxPeakMemoryMiB = 100, maxRetainedMemoryDriftMiB = 0, maxSteadyAllocationBytesPerFrame = 0
        });
        private static string EvidenceJson() => JsonUtility.ToJson(new PerformanceEvidenceAudit.Evidence
        {
            environmentId = "json-rule-fixture", loadVersion = "fixture-v1", runId = "fixture-run",
            integratedLoad = true, allOperationsSucceeded = true, countersReturnedToDeclaredBaseline = true,
            coldStartRounds = 10, steadyStateRounds = 3, shortestSteadyStateSeconds = 300, warmedCycles = 50,
            frameP95Milliseconds = 2, frameP99Milliseconds = 3, coldStartP95Seconds = 4,
            peakMemoryMiB = 100, retainedMemoryDriftMiB = 0, steadyAllocationBytesPerFrame = 0,
            frameMetricScope = "fixture total frame time", memoryMetricScope = "fixture single memory metric",
            allocationMetricScope = "fixture allocation per frame", rawSampleReferences = new[] { "fixture.csv" }
        });
        private static string RemoveNumber(string json, string name)
        {
            var pattern = new Regex("(?<before>,?)\"" + Regex.Escape(name) + "\"\\s*:\\s*[-+0-9.eE]+(?<after>,?)");
            Assert.AreEqual(1, pattern.Matches(json).Count, "Fixture must contain exactly one declared numeric field.");
            return pattern.Replace(json, match => match.Groups["before"].Value == "," && match.Groups["after"].Value == "," ? "," : "");
        }

        [Test]
        public void ValidExplicitJsonAndTrueZeroMetricsRemainWithinDeclaredBudget()
        {
            var report = PerformanceEvidenceAudit.InspectJson(BudgetJson(), EvidenceJson());
            Assert.AreEqual("WithinDeclaredBudget", report.status);
            StringAssert.Contains("not a release verdict", report.scope);
            Assert.IsEmpty(report.blockers);
        }

        [TestCase("maxFrameP95Milliseconds")]
        [TestCase("maxFrameP99Milliseconds")]
        [TestCase("maxColdStartP95Seconds")]
        [TestCase("maxPeakMemoryMiB")]
        [TestCase("maxRetainedMemoryDriftMiB")]
        [TestCase("maxSteadyAllocationBytesPerFrame")]
        public void SingleMissingBudgetMetricBlocksDespiteOtherwiseValidMetadata(string field)
        {
            var report = PerformanceEvidenceAudit.InspectJson(RemoveNumber(BudgetJson(), field), EvidenceJson());
            Assert.AreEqual("Blocked", report.status, "Missing " + field + " must not become a zero budget.");
        }

        [TestCase("frameP95Milliseconds")]
        [TestCase("frameP99Milliseconds")]
        [TestCase("coldStartP95Seconds")]
        [TestCase("peakMemoryMiB")]
        [TestCase("retainedMemoryDriftMiB")]
        [TestCase("steadyAllocationBytesPerFrame")]
        public void SingleMissingObservationBlocksDespiteOtherwiseValidMetadata(string field)
        {
            var report = PerformanceEvidenceAudit.InspectJson(BudgetJson(), RemoveNumber(EvidenceJson(), field));
            Assert.AreEqual("Blocked", report.status, "Missing " + field + " must not become a zero measurement.");
        }

        [Test]
        public void MissingSchemaDoesNotDefaultToCurrentSchema()
        {
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.InspectJson(RemoveNumber(BudgetJson(), "schemaVersion"), EvidenceJson()).status);
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.InspectJson(BudgetJson(), RemoveNumber(EvidenceJson(), "schemaVersion")).status);
        }

        [Test]
        public void EmptyNullArrayAndOversizedJsonAreRejected()
        {
            Assert.Throws<ArgumentException>(() => PerformanceEvidenceAudit.InspectJson(null, EvidenceJson()));
            Assert.Throws<ArgumentException>(() => PerformanceEvidenceAudit.InspectJson(" ", EvidenceJson()));
            Assert.Throws<FormatException>(() => PerformanceEvidenceAudit.InspectJson("[]", EvidenceJson()));
            Assert.Throws<ArgumentException>(() => PerformanceEvidenceAudit.InspectJson("{" + new string(' ', 1048576) + "}", EvidenceJson()));
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.InspectJson("{}", "{}").status);
        }
    }
}
