using UnityEngine;

namespace SortOrStay
{
    /// Puzzles that can take their next correct step on their own. Used by the editor-only play-test tools
    /// (Ctrl+N in the editor, and Sort or Stay > Dev: Auto-play); never by the player in a build.
    public interface IAutoPlayable
    {
        /// Perform the next correct step. False if there is nothing to do right now.
        bool AutoStep();

        /// The object a player would click (or reach with a number key) for the next step: the right one,
        /// or, with wrong = true, a plausible wrong one (null if there is no wrong choice right now).
        Component AutoTarget(bool wrong);

        /// One line describing the puzzle's state, for the play-test log.
        string DebugState();
    }
}
