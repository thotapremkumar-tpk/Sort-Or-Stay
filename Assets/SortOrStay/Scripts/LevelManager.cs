using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SortOrStay
{
    /// Loads the next floor when the player walks through the unlocked door.
    /// If the next floor's scene does not exist yet, shows the scoreboard of every floor played so far.
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        public int levelNumber = 1;
        public string nextScene = "Level2_Selection";
        public string nextFloorLabel = "Floor 2  ·  the Storage Room  (Selection Sort)";
        public Door door;
        public PlayerController player;

        bool leaving;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void ExitReached()
        {
            if (leaving || door == null || !door.IsUnlocked) return;
            leaving = true;
            StartCoroutine(Leave());
        }

        IEnumerator Leave()
        {
            var ui = GameUI.Instance;
            bool building = BuildingManager.Instance != null;
            if (player != null) player.canMove = false;
            if (ui != null)
            {
                ui.ClosePopup();
                if (building) ui.fadeColor = new Color(1f, 0.98f, 0.94f);     // out of the building into daylight
                yield return ui.FadeTo(1f, building ? 1.8f : 1.3f);
            }

            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
            {
                SceneManager.LoadScene(nextScene);
                yield break;
            }

            if (ui != null)
            {
                if (levelNumber >= GameState.FloorScenes.Length)
                {
                    bool all = GameState.Instance.results.Count >= GameState.FloorScenes.Length;
                    ui.ShowEndCard(all ? "YOU ESCAPED  ·  ALL 5 FLOORS" : "YOU ESCAPED",
                                   Scoreboard() + (all ? "\n\nFive floors, five sorting algorithms, one way out."
                                                       : "\n\nPlay from Floor 1 to see all five algorithms on one scoreboard."),
                                   building ? "R  play the building again from the top      ·      1 - 5  start on a floor"
                                            : "R  play the Vault again      ·      1 - 5  jump to a floor");
                }
                else
                    ui.ShowEndCard($"FLOORS 1–{levelNumber} ESCAPED", Scoreboard() + $"\n\nNext:  {nextFloorLabel}  (coming soon)",
                                   "R  play this floor again      ·      1 - 5  jump to a floor");
            }
            PlayerController.FreeCursor();
        }

        public static string Scoreboard()
        {
            var gs = GameState.Instance;
            if (gs.results.Count == 0) return "You found your way out.";
            var sb = new StringBuilder();
            float total = 0f;
            foreach (var r in gs.results)
            {
                total += r.seconds;
                string detail = string.IsNullOrEmpty(r.detail) ? $"{r.swaps} swaps  ·  {r.comparisons} comparisons" : r.detail;
                sb.Append($"<b>Floor {r.level}  ·  {r.algorithm}</b>\n");
                sb.Append($"      {detail}  ·  {r.hints} hints  ·  {r.mistakes} slips  ·  {GameState.Clock(r.seconds)}\n");
            }
            sb.Append($"\nTotal time  <b>{GameState.Clock(total)}</b>");
            return sb.ToString();
        }

        /// Restart this floor (in the building: the floor the player is on; from the end card, the whole building).
        public static void Reload()
        {
            if (BuildingManager.Instance != null) { BuildingManager.Instance.Restart(); return; }
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public static bool FloorExists(int floor)
        {
            var b = BuildingManager.Instance;
            if (b != null) return floor >= 1 && floor <= b.FloorCount;
            int i = floor - 1;
            return i >= 0 && i < GameState.FloorScenes.Length && Application.CanStreamedLevelBeLoaded(GameState.FloorScenes[i]);
        }

        /// Go to a floor (in the building: start again on that floor of the same building).
        public static void LoadFloor(int floor)
        {
            if (BuildingManager.Instance != null) { BuildingManager.Instance.JumpTo(floor - 1); return; }
            if (FloorExists(floor)) SceneManager.LoadScene(GameState.FloorScenes[floor - 1]);
        }

        void Update()
        {
            var ui = GameUI.Instance;
            if (ui == null || !ui.EndCardOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) Reload();
            for (int f = 1; f <= GameState.FloorScenes.Length; f++)
                if (kb[Key.Digit1 + (f - 1)].wasPressedThisFrame || kb[Key.Numpad1 + (f - 1)].wasPressedThisFrame) LoadFloor(f);
        }
    }
}
