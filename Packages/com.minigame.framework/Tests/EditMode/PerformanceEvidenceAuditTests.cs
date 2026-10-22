using System;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class PerformanceEvidenceAuditTests
    {
        private static PerformanceEvidenceAudit.Budget FixtureBudget() => new PerformanceEvidenceAudit.Budget
        {
            environmentId = "test-fixture-not-a-real-approval", loadVersion = "fixture-v1", frozen = true,
            approvedBy = "test fixture", approvedUtc = "2026-10-21T00:00:00Z",
            maxFrameP95Milliseconds = 2, maxFrameP99Milliseconds = 3, maxColdStartP95Seconds = 4,
            maxPeakMemoryMiB = 100, maxRetainedMemoryDriftMiB = 0, maxSteadyAllocationBytesPerFrame = 0
        };

        private static PerformanceEvidenceAudit.Evidence FixtureEvidence() => new PerformanceEvidenceAudit.Evidence
        {
            environmentId = "test-fixture-not-a-real-approval", loadVersion = "fixture-v1", runId = "fixture-run",
            integratedLoad = true, allOperationsSucceeded = true, countersReturnedToDeclaredBaseline = true,
            coldStartRounds = 10, steadyStateRounds = 3, shortestSteadyStateSeconds = 300, warmedCycles = 50,
            frameP95Milliseconds = 2, frameP99Milliseconds = 3, coldStartP95Seconds = 4,
            peakMemoryMiB = 100, retainedMemoryDriftMiB = 0, steadyAllocationBytesPerFrame = 0,
            frameMetricScope = "Fixture total frame time", memoryMetricScope = "Fixture single memory metric",
            allocationMetricScope = "Fixture per-frame allocation", rawSampleReferences = new[] { "fixture-samples-only.csv" }
        };

        [Test]
        public void MissingAndUnfrozenBudgetOrEvidenceBlocksEvaluation()
        {
            var result = PerformanceEvidenceAudit.Inspect(null, null);
            Assert.AreEqual("Blocked", result.status);
            Assert.AreEqual(2, result.blockers.Count);
            var budget = FixtureBudget();
            budget.frozen = false;
            result = PerformanceEvidenceAudit.Inspect(budget, FixtureEvidence());
            Assert.AreEqual("Blocked", result.status);
            Assert.IsTrue(result.blockers.Any(reason => reason.Contains("frozen")));
            budget.frozen = true;
            budget.approvedUtc = "2026-10-21T00:00:00";
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, FixtureEvidence()).status);
        }

        [Test]
        public void ExactThresholdAndZeroAllocationAreValidButOnlyForDeclaredMetadata()
        {
            var budget = FixtureBudget();
            var evidence = FixtureEvidence();
            string originalBudget = JsonUtility.ToJson(budget), originalEvidence = JsonUtility.ToJson(evidence);
            var result = PerformanceEvidenceAudit.Inspect(budget, evidence);
            Assert.AreEqual("WithinDeclaredBudget", result.status);
            Assert.IsEmpty(result.blockers);
            Assert.IsEmpty(result.exceeded);
            StringAssert.Contains("not a release verdict", result.scope);
            Assert.AreEqual(originalBudget, JsonUtility.ToJson(budget));
            Assert.AreEqual(originalEvidence, JsonUtility.ToJson(evidence));
        }

        [Test]
        public void ExceededMetricsAreListedIndividuallyWithoutHidingAny()
        {
            var evidence = FixtureEvidence();
            evidence.frameP95Milliseconds = 2.1;
            evidence.frameP99Milliseconds = 3.1;
            evidence.coldStartP95Seconds = 4.1;
            evidence.peakMemoryMiB = 100.1;
            evidence.retainedMemoryDriftMiB = 0.1;
            evidence.steadyAllocationBytesPerFrame = 1;
            var result = PerformanceEvidenceAudit.Inspect(FixtureBudget(), evidence);
            Assert.AreEqual("Exceeded", result.status);
            Assert.AreEqual(6, result.exceeded.Count);
            CollectionAssert.AreEquivalent(new[]
            {
                "frameP95Milliseconds", "frameP99Milliseconds", "coldStartP95Seconds", "peakMemoryMiB",
                "retainedMemoryDriftMiB", "steadyAllocationBytesPerFrame"
            }, result.exceeded);
        }

        [Test]
        public void MicrobenchmarkShortRoundsAndFailedOperationsCannotPass()
        {
            var evidence = FixtureEvidence();
            evidence.integratedLoad = false;
            evidence.coldStartRounds = 9;
            evidence.shortestSteadyStateSeconds = 299.9;
            evidence.allOperationsSucceeded = false;
            evidence.countersReturnedToDeclaredBaseline = false;
            var result = PerformanceEvidenceAudit.Inspect(FixtureBudget(), evidence);
            Assert.AreEqual("Blocked", result.status);
            Assert.IsTrue(result.blockers.Any(reason => reason.Contains("microbenchmarks")));
            Assert.IsTrue(result.blockers.Any(reason => reason.Contains("300 seconds")));
            Assert.IsTrue(result.blockers.Any(reason => reason.Contains("Failed operations")));
            Assert.IsTrue(result.blockers.Any(reason => reason.Contains("counters")));
        }

        [Test]
        public void UnknownNonfiniteAndMismatchedMetricsAreNeverZeroOrSuccess()
        {
            var budget = FixtureBudget();
            var evidence = FixtureEvidence();
            evidence.peakMemoryMiB = -1;
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
            evidence = FixtureEvidence();
            evidence.frameP99Milliseconds = double.NaN;
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
            evidence = FixtureEvidence();
            budget.maxPeakMemoryMiB = double.PositiveInfinity;
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
            budget = FixtureBudget();
            evidence.loadVersion = "different-load";
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
        }

        [Test]
        public void MissingScopesRawSamplesAndUnknownSchemasAreRejected()
        {
            var budget = FixtureBudget();
            var evidence = FixtureEvidence();
            evidence.rawSampleReferences = new[] { "same", "same" };
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
            evidence = FixtureEvidence();
            evidence.memoryMetricScope = null;
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
            evidence = FixtureEvidence();
            evidence.schemaVersion = 2;
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, evidence).status);
            budget.schemaVersion = 2;
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(budget, FixtureEvidence()).status);
            Assert.AreEqual("Blocked", PerformanceEvidenceAudit.Inspect(JsonUtility.FromJson<PerformanceEvidenceAudit.Budget>("{}"),
                JsonUtility.FromJson<PerformanceEvidenceAudit.Evidence>("{}")).status);
        }
    }
}
