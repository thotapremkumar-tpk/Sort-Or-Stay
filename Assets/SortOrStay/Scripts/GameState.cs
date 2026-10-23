using System;
using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay
{
    [Serializable]
    public class LevelResult
    {
        public int level;
        public string algorithm;
        public int comparisons;
        public int swaps;
        public int minSwaps;
        public int hints;
        public int mistakes;
        public float seconds;
        public string detail;      // one line for the scoreboard, e.g. "6 swaps · 27 weighings"
    }

    /// Survives scene loads and keeps every floor's score for the end-of-game scoreboard.
    public class GameState : MonoBehaviour
    {
        /// Scene name for each floor (index 0 = floor 1).
        public static readonly string[] FloorScenes =
            { "Level1_Bubble", "Level2_Selection", "Level3_Insertion", "Level4_Merge", "Level5_Quick" };

        public static readonly string[] FloorNames =
            { "The Locked Room", "The Storage Room", "The Library", "The Control Room", "The Final Vault" };

        static GameState instance;

        public static GameState Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("GameState");
                    instance = go.AddComponent<GameState>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        public List<LevelResult> results = new List<LevelResult>();

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        public void Record(LevelResult r)
        {
            results.RemoveAll(x => x.level == r.level);
            results.Add(r);
            results.Sort((a, b) => a.level.CompareTo(b.level));
        }

        public LevelResult Get(int level) => results.Find(x => x.level == level);

        public static string Clock(float seconds)
        {
            return $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";
        }
    }
}
