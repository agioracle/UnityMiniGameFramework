using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.ReleaseValidation.Tests
{
    public sealed class ReleaseUpgradeTests
    {
        [Test]
        public void UnchangedConsumerReadsAndPreservesItsOwnSaveSchema()
        {
            string directory = Path.GetFullPath("Builds/ConsumerSave");
            bool create = Environment.GetEnvironmentVariable("MINIGAME_UPGRADE_READ_ONLY") != "1";
            ReleaseConsumer.CheckFoundation(directory, create);
        }

        [UnityTest]
        public IEnumerator ImportedSampleWinsAndProgressSurvivesPackageUpgrade()
        {
            bool create = Environment.GetEnvironmentVariable("MINIGAME_UPGRADE_READ_ONLY") != "1";
            var task = ReleaseConsumer.CheckGame(Path.GetFullPath("Builds/SampleSave"), create).AsTask();
            double deadline = Time.realtimeSinceStartupAsDouble + 20;
            while (!task.IsCompleted && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Consumer operation did not drain.");
            task.GetAwaiter().GetResult();
        }
    }
}
