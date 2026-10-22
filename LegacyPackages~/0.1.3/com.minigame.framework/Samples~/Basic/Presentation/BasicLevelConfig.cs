using System;
using UnityEngine;

namespace MiniGame.Examples.Basic
{
    [CreateAssetMenu(menuName = "MiniGame/Examples/Basic Level Configuration")]
    public sealed class BasicLevelConfig : ScriptableObject
    {
        [SerializeField, Min(1)] private int moves = 8;
        [SerializeField, Min(1)] private int goal = 5;
        [SerializeField] private int seed = 31;

        public readonly struct Snapshot
        {
            public readonly int Moves;
            public readonly int Goal;
            public readonly int Seed;
            public Snapshot(int moves, int goal, int seed) { Moves = moves; Goal = goal; Seed = seed; }
        }

        public Snapshot ReadSnapshot()
        {
            if (moves < 1 || goal < 1 || goal > moves) throw new ArgumentException("Basic level requires positive moves and a goal within the move budget.");
            return new Snapshot(moves, goal, seed);
        }
    }
}
