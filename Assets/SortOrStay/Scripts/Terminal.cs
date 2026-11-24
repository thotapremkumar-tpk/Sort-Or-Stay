using TMPro;
using UnityEngine;

namespace SortOrStay
{
    /// Wall terminal: shows the instruction and live status, and gives a hint when clicked.
    public class Terminal : MonoBehaviour, IInteractable
    {
        public TMP_Text screen;
        public SortPuzzleBase puzzle;

        public void SetText(string text)
        {
            if (screen == null) return;
            screen.color = Color.white;
            int lineEnd = text.IndexOf('\n');
            if (lineEnd < 0)
            {
                screen.text = text;
                return;
            }

            string roomLine = puzzle != null && puzzle.roomNumber > 0
                ? $"\n<size=85%>ROOM {puzzle.roomNumber}  ·  {puzzle.CaseName.ToUpperInvariant()}</size>"
                : "";
            screen.text = "<color=#7FF2D6>" + text.Substring(0, lineEnd) + roomLine + "</color>" + text.Substring(lineEnd);
        }

        public string prompt = "Terminal  ·  ask for a hint";
        public string Prompt => prompt;
        public bool CanInteract => puzzle != null && puzzle.AcceptsInput;

        public void Interact()
        {
            if (puzzle == null) return;
            ProceduralSfx.Play(ProceduralSfx.Hint, transform.position + Vector3.up * 1.5f, 0.7f);
            puzzle.RequestHint();
        }
    }
}
