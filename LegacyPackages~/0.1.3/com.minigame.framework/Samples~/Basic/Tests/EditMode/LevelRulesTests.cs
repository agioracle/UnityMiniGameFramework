using System;
using NUnit.Framework;

namespace MiniGame.Examples.Basic.Tests
{
    public sealed class LevelRulesTests
    {
        [Test]
        public void ReadyLevelDoesNotAcceptInput()
        {
            var rules = new LevelRules(9, 8, 5, 31);
            Assert.IsFalse(rules.Select(0));
            Assert.AreEqual(8, rules.Snapshot.Moves);
        }

        [Test]
        public void FiveMatchesWinAndStopAcceptingMoves()
        {
            var rules = new LevelRules(9, 8, 5, 31);
            rules.Start();
            for (int i = 0; i < 5; i++) rules.Select(rules.Snapshot.Target);
            Assert.AreEqual(LevelStatus.Won, rules.Snapshot.Status);
            Assert.AreEqual(3, rules.Snapshot.Moves);
            Assert.IsFalse(rules.Select(0));
        }

        [Test]
        public void WrongChoicesConsumeMovesAndLose()
        {
            var rules = new LevelRules(9, 8, 5, 31);
            rules.Start();
            for (int i = 0; i < 8; i++) rules.Select((rules.Snapshot.Target + 1) % 9);
            Assert.AreEqual(LevelStatus.Lost, rules.Snapshot.Status);
            Assert.AreEqual(0, rules.Snapshot.Matches);
        }

        [Test]
        public void RestartResetsStateButAdvancesRevision()
        {
            var rules = new LevelRules(9, 8, 5, 31);
            rules.Start();
            int first = rules.Snapshot.Target;
            rules.Select(first);
            long revision = rules.Snapshot.Revision;
            rules.Start();
            Assert.AreEqual(first, rules.Snapshot.Target);
            Assert.AreEqual(8, rules.Snapshot.Moves);
            Assert.Greater(rules.Snapshot.Revision, revision);
        }

        [Test]
        public void TargetChangesAfterSuccessfulSelection()
        {
            var rules = new LevelRules(9, 8, 5, 31);
            rules.Start();
            int target = rules.Snapshot.Target;
            rules.Select(target);
            Assert.AreNotEqual(target, rules.Snapshot.Target);
        }

        [Test]
        public void InvalidRulesAndInputAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelRules(1, 8, 5, 31));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelRules(9, 4, 5, 31));
            var rules = new LevelRules(9, 8, 5, 31);
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.Select(9));
        }
    }
}
