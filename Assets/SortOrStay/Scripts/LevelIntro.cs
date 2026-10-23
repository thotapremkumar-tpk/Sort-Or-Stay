using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Arrival on a new floor: fade in on a wide establishing shot of the room, a few lines of text,
    /// then the camera flies down behind the avatar and control is handed over. Space skips it.
    public class LevelIntro : MonoBehaviour
    {
        public PlayerController player;
        public SortPuzzleBase puzzle;
        public Transform overview;             // establishing-shot camera pose
        public float holdTime = 2.2f;
        public float flyTime = 3.0f;

        [TextArea] public string objective = "";
        public string[] lines = { };

        bool skip, finished;

        void Update()
        {
            var kb = Keyboard.current;
            if (!finished && kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) skip = true;
        }

        IEnumerator Start()
        {
            var ui = GameUI.Instance;
            if (player == null || player.cam == null || overview == null)
            {
                Finish(ui);
                yield break;
            }

            player.canMove = false;
            player.canLook = false;
            player.cinematic = true;
            if (puzzle != null) puzzle.inputEnabled = false;
            if (ui != null) { ui.SetFade(1f); ui.showHud = false; }

            Transform cam = player.cam.transform;
            cam.position = overview.position;
            cam.rotation = overview.rotation;

            yield return null;
            if (ui != null && lines.Length > 0) ui.Subtitle(lines[0], holdTime + 1.6f);
            yield return FadeTo(ui, 0f, 1.3f);
            yield return Wait(holdTime);

            if (ui != null && lines.Length > 1 && !skip) ui.Subtitle(lines[1], flyTime + 1.2f);
            Vector3 p0 = cam.position; Quaternion r0 = cam.rotation;
            float t = 0f;
            while (t < flyTime && !skip)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / flyTime);
                player.ComputeCameraPose(out Vector3 p1, out Quaternion r1);
                Vector3 mid = Vector3.Lerp(p0, p1, 0.5f) + Vector3.up * 0.6f;   // gentle arc
                cam.position = Vector3.Lerp(Vector3.Lerp(p0, mid, k), Vector3.Lerp(mid, p1, k), k);
                cam.rotation = Quaternion.Slerp(r0, r1, k);
                yield return null;
            }

            Finish(ui);
            if (!skip && lines.Length > 2)
            {
                yield return new WaitForSeconds(1.0f);
                if (ui != null) ui.Subtitle(lines[2], 4.5f);
            }
        }

        void Finish(GameUI ui)
        {
            if (finished) return;
            finished = true;
            if (player != null)
            {
                player.cinematic = false;
                player.canMove = true;
                player.canLook = true;
                player.SnapCamera();
            }
            if (puzzle != null) puzzle.inputEnabled = true;
            if (ui != null)
            {
                ui.SetFade(0f);
                ui.showHud = true;
                ui.SetObjective(objective);
                ui.ShowControls(12f);
            }
        }

        IEnumerator Wait(float seconds)
        {
            float e = 0f;
            while (e < seconds && !skip) { e += Time.deltaTime; yield return null; }
        }

        IEnumerator FadeTo(GameUI ui, float target, float seconds)
        {
            if (ui == null) yield break;
            float start = ui.Fade, e = 0f;
            while (e < seconds && !skip)
            {
                e += Time.deltaTime;
                ui.SetFade(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, e / seconds)));
                yield return null;
            }
            ui.SetFade(target);
        }
    }
}
