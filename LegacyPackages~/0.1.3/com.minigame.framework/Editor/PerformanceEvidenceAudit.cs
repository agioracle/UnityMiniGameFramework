using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class PerformanceEvidenceAudit
    {
        [Serializable]
        public sealed class Budget
        {
            public int schemaVersion = 1;
            public string environmentId;
            public string loadVersion;
            public string approvedBy;
            public string approvedUtc;
            public bool frozen;
            public double maxFrameP95Milliseconds = -1;
            public double maxFrameP99Milliseconds = -1;
            public double maxColdStartP95Seconds = -1;
            public double maxPeakMemoryMiB = -1;
            public double maxRetainedMemoryDriftMiB = -1;
            public double maxSteadyAllocationBytesPerFrame = -1;
        }

        [Serializable]
        public sealed class Evidence
        {
            public int schemaVersion = 1;
            public string environmentId;
            public string loadVersion;
            public string runId;
            public bool integratedLoad;
            public bool allOperationsSucceeded;
            public bool countersReturnedToDeclaredBaseline;
            public int coldStartRounds;
            public int steadyStateRounds;
            public double shortestSteadyStateSeconds;
            public int warmedCycles;
            public double frameP95Milliseconds = -1;
            public double frameP99Milliseconds = -1;
            public double coldStartP95Seconds = -1;
            public double peakMemoryMiB = -1;
            public double retainedMemoryDriftMiB = -1;
            public double steadyAllocationBytesPerFrame = -1;
            public string frameMetricScope;
            public string memoryMetricScope;
            public string allocationMetricScope;
            public string[] rawSampleReferences;
        }

        [Serializable]
        public sealed class Report
        {
            public string status;
            public List<string> blockers = new List<string>();
            public List<string> exceeded = new List<string>();
            public string scope = "Checks declared budget/evidence metadata and metric thresholds only. Does not authenticate approval, raw samples, load completeness, device measurements, AOT or product release. WithinDeclaredBudget is not a release verdict.";
        }

        public static Report Inspect(Budget budget, Evidence evidence)
        {
            var result = new Report();
            if (budget == null) result.blockers.Add("Explicit approved budget is missing.");
            else
            {
                if (budget.schemaVersion != 1) result.blockers.Add("Unsupported budget schema.");
                if (!budget.frozen || string.IsNullOrWhiteSpace(budget.approvedBy) || !UtcTimestamp(budget.approvedUtc))
                    result.blockers.Add("Budget must be frozen with an approval identity and explicit UTC timestamp; approval authenticity requires separate review.");
                if (string.IsNullOrWhiteSpace(budget.environmentId) || string.IsNullOrWhiteSpace(budget.loadVersion))
                    result.blockers.Add("Budget requires environment and versioned load identities.");
                if (!Nonnegative(budget.maxFrameP95Milliseconds) || !Nonnegative(budget.maxFrameP99Milliseconds) ||
                    budget.maxFrameP95Milliseconds <= 0 || budget.maxFrameP99Milliseconds < budget.maxFrameP95Milliseconds ||
                    !Nonnegative(budget.maxColdStartP95Seconds) || budget.maxColdStartP95Seconds <= 0 ||
                    !Nonnegative(budget.maxPeakMemoryMiB) || budget.maxPeakMemoryMiB <= 0 ||
                    !Nonnegative(budget.maxRetainedMemoryDriftMiB) || !Nonnegative(budget.maxSteadyAllocationBytesPerFrame))
                    result.blockers.Add("All required metric limits must be finite and explicitly supplied; zero allocation/drift budgets are valid, unknown is not zero.");
            }
            if (evidence == null) result.blockers.Add("Integrated measurement evidence is missing.");
            else
            {
                if (evidence.schemaVersion != 1) result.blockers.Add("Unsupported evidence schema.");
                if (string.IsNullOrWhiteSpace(evidence.environmentId) || string.IsNullOrWhiteSpace(evidence.loadVersion) || string.IsNullOrWhiteSpace(evidence.runId))
                    result.blockers.Add("Evidence requires environment, load version and run identities.");
                if (budget != null && (!string.Equals(budget.environmentId, evidence.environmentId, StringComparison.Ordinal) ||
                    !string.Equals(budget.loadVersion, evidence.loadVersion, StringComparison.Ordinal)))
                    result.blockers.Add("Budget and evidence environment/load identities differ.");
                if (!evidence.integratedLoad) result.blockers.Add("Core microbenchmarks cannot substitute for the declared integrated load.");
                if (!evidence.allOperationsSucceeded) result.blockers.Add("Failed operations or failed rounds must not be discarded.");
                if (!evidence.countersReturnedToDeclaredBaseline) result.blockers.Add("Resource/handle/subscription/pool counters did not return to their declared baseline.");
                if (evidence.coldStartRounds < 10 || evidence.steadyStateRounds < 3 || !Nonnegative(evidence.shortestSteadyStateSeconds) ||
                    evidence.shortestSteadyStateSeconds < 300 || evidence.warmedCycles < 50)
                    result.blockers.Add("Evidence requires at least 10 cold starts, 3 steady-state rounds each at least 300 seconds, and 50 warmed cycles.");
                if (!Nonnegative(evidence.frameP95Milliseconds) || !Nonnegative(evidence.frameP99Milliseconds) || evidence.frameP99Milliseconds < evidence.frameP95Milliseconds ||
                    !Nonnegative(evidence.coldStartP95Seconds) || !Nonnegative(evidence.peakMemoryMiB) ||
                    !Nonnegative(evidence.retainedMemoryDriftMiB) || !Nonnegative(evidence.steadyAllocationBytesPerFrame))
                    result.blockers.Add("All required observations must be finite and non-negative; missing measurements use -1, never zero.");
                if (string.IsNullOrWhiteSpace(evidence.frameMetricScope) || string.IsNullOrWhiteSpace(evidence.memoryMetricScope) ||
                    string.IsNullOrWhiteSpace(evidence.allocationMetricScope))
                    result.blockers.Add("Frame, memory and allocation measurement scopes must be explicit; overlapping memory scopes must not be added together.");
                if (evidence.rawSampleReferences == null || evidence.rawSampleReferences.Length == 0 || evidence.rawSampleReferences.Length > 10000)
                    result.blockers.Add("Explicit raw sample references are required for independent review.");
                else
                {
                    var references = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string reference in evidence.rawSampleReferences)
                        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 4096 || !references.Add(reference))
                        {
                            result.blockers.Add("Raw sample references must be nonempty, bounded and unique.");
                            break;
                        }
                }
            }
            if (result.blockers.Count == 0)
            {
                Check(result, "frameP95Milliseconds", evidence.frameP95Milliseconds, budget.maxFrameP95Milliseconds);
                Check(result, "frameP99Milliseconds", evidence.frameP99Milliseconds, budget.maxFrameP99Milliseconds);
                Check(result, "coldStartP95Seconds", evidence.coldStartP95Seconds, budget.maxColdStartP95Seconds);
                Check(result, "peakMemoryMiB", evidence.peakMemoryMiB, budget.maxPeakMemoryMiB);
                Check(result, "retainedMemoryDriftMiB", evidence.retainedMemoryDriftMiB, budget.maxRetainedMemoryDriftMiB);
                Check(result, "steadyAllocationBytesPerFrame", evidence.steadyAllocationBytesPerFrame, budget.maxSteadyAllocationBytesPerFrame);
            }
            result.status = result.blockers.Count != 0 ? "Blocked" : result.exceeded.Count != 0 ? "Exceeded" : "WithinDeclaredBudget";
            return result;
        }

        [MenuItem("MiniGame/Framework/Check Performance Evidence Against Budget")]
        public static void InspectFromMenu()
        {
            string budgetPath = EditorUtility.OpenFilePanel("Select explicit frozen budget from Builds", Path.GetFullPath("Builds/Reports"), "json");
            if (string.IsNullOrEmpty(budgetPath)) return;
            string evidencePath = EditorUtility.OpenFilePanel("Select integrated measurement evidence from Builds", Path.GetFullPath("Builds/Reports"), "json");
            if (string.IsNullOrEmpty(evidencePath)) return;
            Report report = InspectJson(ReadJson(budgetPath), ReadJson(evidencePath));
            string output = "Builds/Reports/performance-evidence-check-" + Guid.NewGuid().ToString("N") + ".json";
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(output));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report, true));
            Debug.Log("[MiniGame] Performance evidence check: " + report.status + "; report: " + output);
        }

        public static Report InspectJson(string budgetJson, string evidenceJson)
        {
            ValidateJson(budgetJson); ValidateJson(evidenceJson);
            var budget = new Budget { schemaVersion = -1 };
            var evidence = new Evidence { schemaVersion = -1 };
            JsonUtility.FromJsonOverwrite(budgetJson, budget);
            JsonUtility.FromJsonOverwrite(evidenceJson, evidence);
            return Inspect(budget, evidence);
        }
        private static void ValidateJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > 1048576)
                throw new ArgumentException("Explicit JSON metadata is required and must not exceed 1 MiB.");
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
                throw new FormatException("Metadata must be a JSON object.");
        }
        private static string ReadJson(string path)
        {
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), path);
            if (new FileInfo(path).Length > 1048576) throw new ArgumentException("Evidence metadata exceeds 1 MiB.");
            return File.ReadAllText(path);
        }
        private static bool Nonnegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool UtcTimestamp(string value) => !string.IsNullOrEmpty(value) && value.EndsWith("Z", StringComparison.Ordinal) &&
            DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTimeOffset timestamp) && timestamp.Offset == TimeSpan.Zero;
        private static void Check(Report report, string metric, double observed, double limit)
        {
            if (observed > limit) report.exceeded.Add(metric);
        }
    }
}
