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
            if (player != null) player.canMove = false;
            if (ui != null)
            {
                ui.ClosePopup();
                yield return ui.FadeTo(1f, 1.3f);
            }

            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
            {
                SceneManager.LoadScene(nextScene);
                yield break;
            }

            if (ui != null) ui.ShowEndCard($"FLOORS 1–{levelNumber} ESCAPED", Scoreboard() + $"\n\nNext:  {nextFloorLabel}  (coming soon)",
                                           "R  play this floor again      ·      1 / 2 / 3  jump to a floor");
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

        public static void Reload()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public static bool FloorExists(int floor)
        {
            int i = floor - 1;
            return i >= 0 && i < GameState.FloorScenes.Length && Application.CanStreamedLevelBeLoaded(GameState.FloorScenes[i]);
        }

        public static void LoadFloor(int floor)
        {
            if (FloorExists(floor)) SceneManager.LoadScene(GameState.FloorScenes[floor - 1]);
        }

        void Update()
        {
            var ui = GameUI.Instance;
            if (ui == null || !ui.EndCardOpen) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) Reload();
            if (kb.digit1Key.wasPressedThisFrame) LoadFloor(1);
            if (kb.digit2Key.wasPressedThisFrame) LoadFloor(2);
            if (kb.digit3Key.wasPressedThisFrame) LoadFloor(3);
        }
    }
}
