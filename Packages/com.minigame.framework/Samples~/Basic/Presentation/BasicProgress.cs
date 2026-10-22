using System;
using MiniGame.Framework;

namespace MiniGame.Examples.Basic
{
    [Serializable]
    public sealed class BasicProgress
    {
        public int completedRounds;
        public int wonRounds;
    }

    public sealed class BasicProgressStore
    {
        private readonly LocalStore store;
        public BasicProgress Value { get; private set; }
        public StoreStatus Status { get; private set; }

        public BasicProgressStore(string directory)
        {
            store = new LocalStore(directory, 4096);
            StoreResult<BasicProgress> result = store.Load<BasicProgress>("basic_progress", 1);
            Status = result.Status;
            Value = result.Success ? result.Value : new BasicProgress();
        }

        public StoreStatus Record(bool won)
        {
            if (Status != StoreStatus.Success && Status != StoreStatus.Missing) return Status;
            var next = new BasicProgress
            {
                completedRounds = checked(Value.completedRounds + 1),
                wonRounds = checked(Value.wonRounds + (won ? 1 : 0))
            };
            StoreResult<bool> result = store.Save("basic_progress", 1, next);
            Status = result.Status;
            if (result.Success) Value = next;
            return Status;
        }
    }
}
