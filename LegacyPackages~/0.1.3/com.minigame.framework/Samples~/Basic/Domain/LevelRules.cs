using System;

namespace MiniGame.Examples.Basic
{
    public enum LevelStatus { Ready, Playing, Won, Lost }

    public readonly struct LevelSnapshot
    {
        public readonly int Moves;
        public readonly int Matches;
        public readonly int Goal;
        public readonly int Target;
        public readonly LevelStatus Status;
        public readonly long Revision;
        public LevelSnapshot(int moves, int matches, int goal, int target, LevelStatus status, long revision)
        {
            Moves = moves; Matches = matches; Goal = goal; Target = target; Status = status; Revision = revision;
        }
    }

    public sealed class LevelRules
    {
        private readonly int cellCount;
        private readonly int initialMoves;
        private readonly int goal;
        private readonly int seed;
        private Random random;
        public LevelSnapshot Snapshot { get; private set; }

        public LevelRules(int cellCount, int moves, int goal, int seed)
        {
            if (cellCount < 2) throw new ArgumentOutOfRangeException(nameof(cellCount));
            if (moves < 1) throw new ArgumentOutOfRangeException(nameof(moves));
            if (goal < 1 || goal > moves) throw new ArgumentOutOfRangeException(nameof(goal));
            this.cellCount = cellCount;
            initialMoves = moves;
            this.goal = goal;
            this.seed = seed;
            random = new Random(seed);
            Snapshot = new LevelSnapshot(moves, 0, goal, -1, LevelStatus.Ready, 0);
        }

        public void Start()
        {
            random = new Random(seed);
            Snapshot = new LevelSnapshot(initialMoves, 0, goal, random.Next(cellCount), LevelStatus.Playing, Snapshot.Revision + 1);
        }

        public bool Select(int cell)
        {
            if (cell < 0 || cell >= cellCount) throw new ArgumentOutOfRangeException(nameof(cell));
            LevelSnapshot old = Snapshot;
            if (old.Status != LevelStatus.Playing) return false;
            int matches = old.Matches + (cell == old.Target ? 1 : 0);
            int moves = old.Moves - 1;
            LevelStatus status = matches >= goal ? LevelStatus.Won : moves == 0 ? LevelStatus.Lost : LevelStatus.Playing;
            int target = old.Target;
            if (cell == target && status == LevelStatus.Playing)
                target = (target + 1 + random.Next(cellCount - 1)) % cellCount;
            Snapshot = new LevelSnapshot(moves, matches, goal, target, status, old.Revision + 1);
            return true;
        }
    }
}
