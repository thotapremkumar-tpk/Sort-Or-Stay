using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay
{
    /// One multiple-choice question. options[correct] is the right answer; the room shuffles the order.
    public class QuizQuestion
    {
        public string text;
        public string[] options;
        public int correct;
        public string hint;       // shown after a wrong pick: a nudge, not the answer
        public string explain;    // shown once the right answer is picked

        public QuizQuestion(string text, string hint, string explain, params string[] options)
        {
            this.text = text; this.hint = hint; this.explain = explain; this.options = options; correct = 0;
        }

        /// Same question with the options in a random order.
        public QuizQuestion Shuffled()
        {
            int n = options.Length;
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            for (int i = n - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (idx[i], idx[j]) = (idx[j], idx[i]); }
            var q = new QuizQuestion(text, hint, explain, new string[n]);
            for (int i = 0; i < n; i++) { q.options[i] = options[idx[i]]; if (idx[i] == correct) q.correct = i; }
            return q;
        }
    }

    /// The questions of every room: three per algorithm and case (5 algorithms x best / average / worst = 45).
    /// Each room asks about its own case, so the questions climb from "why was this quick" (best case) through
    /// "what to expect on random input" (average) to "how bad can it get, and why" (worst case).
    /// Numbers are worked out from the room itself (how many items, how many out-of-order pairs it started with).
    public static class QuizBank
    {
        const string ON = "O(n)", ONLOGN = "O(n log n)", ON2 = "O(n²)", OLOGN = "O(log n)", O1 = "O(1)";

        public static List<QuizQuestion> For(SortPuzzleBase p)
        {
            int n = Mathf.Max(2, p.items != null ? p.items.Count : 6);
            var c = p.sortCase == SortCase.Classic ? SortCase.Average : p.sortCase;
            List<QuizQuestion> list;
            if (p is SelectionPuzzle) list = Selection(c, n, p);
            else if (p is InsertionPuzzle) list = Insertion(c, n, p);
            else if (p is MergePuzzle) list = Merge(c, n, p);
            else if (p is QuickPuzzle) list = Quick(c, n, p);
            else list = Bubble(c, n, p);
            var res = new List<QuizQuestion>();
            foreach (var q in list) res.Add(q.Shuffled());
            return res;
        }

        // ------------------------------------------------------------ helpers

        static int Pairs(int n) => n * (n - 1) / 2;
        static int Log2(int n) => Mathf.Max(1, Mathf.RoundToInt(Mathf.Log(n, 2f)));
        static string S(int k, string one, string many) => k == 1 ? one : many;

        /// Number options: the right one first, then distractors made unique (and never negative).
        static string[] Nums(int right, params int[] wrong)
        {
            var used = new HashSet<int> { right };
            var res = new List<string> { right.ToString() };
            foreach (int w0 in wrong)
            {
                int w = Mathf.Max(0, w0);
                while (used.Contains(w)) w++;
                used.Add(w);
                res.Add(w.ToString());
            }
            return res.ToArray();
        }

        static QuizQuestion Q(string text, string hint, string explain, params string[] options) => new QuizQuestion(text, hint, explain, options);

        // ------------------------------------------------------------ bubble sort

        static List<QuizQuestion> Bubble(SortCase c, int n, SortPuzzleBase p)
        {
            int max = Pairs(n), inv = p.StartInversions;
            var l = new List<QuizQuestion>();
            if (c == SortCase.Best)
            {
                l.Add(Q($"This row was <b>nearly sorted</b>.  If all {n} keys had been <b>completely</b> in order, how many comparisons would bubble sort need?  (It stops after a pass with no swaps.)",
                        "It can't know the row is sorted without looking.  How many neighbour pairs does one pass look at?",
                        $"One pass compares each neighbour pair once:  n − 1 = {n - 1}.  No swap happens, so it stops right there.  That's bubble sort's best case.",
                        Nums(n - 1, max, n, 0)));
                l.Add(Q("So bubble sort's <b>best-case</b> running time is…",
                        "In the best case the whole job is one pass.  How does one pass grow with the length of the row?",
                        "One pass of n − 1 comparisons:  the work grows in a straight line with n.  That's <b>O(n)</b>.",
                        ON, ON2, ONLOGN, OLOGN));
                l.Add(Q($"This row started with <b>{inv}</b> out-of-order {S(inv, "pair", "pairs")}.  How many swaps does bubble sort make on it?",
                        "A neighbour swap puts one pair of keys in the right order.  Does it ever fix two at once?",
                        $"Each neighbour swap fixes exactly one out-of-order pair:  {inv} {S(inv, "pair", "pairs")}, {inv} {S(inv, "swap", "swaps")}.  Never fewer, never more.",
                        Nums(inv, n - 1, max, inv * 2 + 1)));
            }
            else if (c == SortCase.Average)
            {
                string avg = (n * (n - 1) / 4f).ToString("0.#");
                l.Add(Q($"When {n} keys are shuffled at random, how many swaps does bubble sort make on average?",
                        "Any two keys are in the wrong order half of the time.  How many pairs of keys are there?",
                        $"There are {max} pairs, and each is out of order half the time. That gives an average of {avg} swaps. This row started with {inv} out-of-order {S(inv, "pair", "pairs")}.",
                        $"About {avg}  (half of all {max} pairs)", $"About {n / 2}", $"Exactly {max}", $"Exactly {n - 1}"));
                l.Add(Q("Bubble sort's <b>average-case</b> running time is…",
                        "About n²/4 swaps.  What happens to n²/4 when n doubles?",
                        "The average number of swaps is n(n − 1)/4. Doubling the number of keys roughly quadruples the work, so the running time is <b>O(n²)</b>.",
                        ON2, ON, ONLOGN, O1));
                int m1 = n + 1;
                l.Add(Q($"With <b>{m1}</b> keys instead of {n}, the most comparisons bubble sort can need is…",
                        "Use n(n−1)/2 with the new n.",
                        $"{m1} · {n} / 2 = {Pairs(m1)}.  One more key adds {n} more comparisons:  the cost grows faster than the row.",
                        Nums(Pairs(m1), max, m1 * m1, 2 * m1)));
            }
            else
            {
                int d = 2 * n;
                l.Add(Q($"The keys started in <b>reverse order</b>.  How many swaps does bubble sort need for {n} reversed keys?",
                        "In a reversed row, which pairs of keys are in the wrong order?",
                        $"Every one of the n(n−1)/2 = {max} pairs is out of order, and each swap fixes one:  {max} swaps.  You made {p.swaps}.",
                        Nums(max, n, n - 1, n * n)));
                l.Add(Q("Why is a reversed row bubble sort's <b>worst case</b>?",
                        "Think about what happened at every glowing pair in this room.",
                        "Every comparison found a pair out of order, so every comparison was also a swap, and no pass could stop early.",
                        "Every comparison finds a pair out of order, so every comparison is also a swap",
                        "The smallest key is on the right, so only one pass is needed",
                        "Bubble sort skips pairs that are reversed",
                        "A reversed row has fewer pairs to compare"));
                l.Add(Q($"If the reversed row doubled to <b>{d}</b> keys, how many swaps would bubble sort make?",
                        $"n(n−1)/2 again, with n = {d}.",
                        $"{d} × {d - 1} / 2 = {Pairs(d)} swaps. The swap count grows roughly with the square of the input size, so the running time is O(n²).",
                        Nums(Pairs(d), 2 * max, d, d * d)));
            }
            return l;
        }

        // ------------------------------------------------------------ selection sort

        static List<QuizQuestion> Selection(SortCase c, int n, SortPuzzleBase p)
        {
            int max = Pairs(n);
            var l = new List<QuizQuestion>();
            if (c == SortCase.Best)
            {
                l.Add(Q($"These crates were <b>already in order</b>, yet you still weighed every crate left on every pass.  How many comparisons does selection sort make on {n} crates?",
                        "Pass 1 compares across all the crates, pass 2 across one fewer, and so on.",
                        $"(n−1) + (n−2) + … + 1 = n(n−1)/2 = {max}.  Selection sort can't see the row is sorted, so it always does every one of them.",
                        Nums(max, n - 1, n, 0)));
                l.Add(Q("How many swaps did the crane make on this sorted row?",
                        "Was the lightest crate ever <i>not</i> already in the next bay?",
                        $"Every pass the lightest crate was already in place, so the crane never moved.  You saw {p.swaps}.  A sorted row saves swaps, not comparisons.",
                        Nums(0, n - 1, n, max)));
                l.Add(Q("Selection sort's <b>best-case</b> running time is…",
                        "Did the sorted order save you any weighing?",
                        $"The comparisons stay at n(n−1)/2 = {max} even on a sorted row, so even the best case is <b>O(n²)</b>.",
                        ON2, ON, ONLOGN, O1));
            }
            else if (c == SortCase.Average)
            {
                l.Add(Q("Selection sort's <b>average-case</b> running time is…",
                        "Did the order of the crates change how many you had to weigh?",
                        $"Always n(n−1)/2 = {max} comparisons, whatever the order:  <b>O(n²)</b>.",
                        ON2, ON, ONLOGN, OLOGN));
                l.Add(Q($"What is the <b>most</b> swaps selection sort can ever make on {n} crates?",
                        "How many swaps can one pass make at most?  How many passes are there?",
                        $"At most one swap per pass, and there are n − 1 passes:  {n - 1}.  That's selection sort's strength:  few moves of heavy things.",
                        Nums(n - 1, max, n, n * n)));
                l.Add(Q("Why did the game hide the remaining crate weights after each pass?",
                        "What does the algorithm carry over from one pass to the next?",
                        "The game hides the weights to demonstrate how selection sort scans the unsorted crates again to find the next lightest one.",
                        "Each pass scans the remaining unsorted crates again",
                        "The crates change weight after each pass",
                        "The scale only remembers the heaviest crate",
                        "So that you weigh fewer crates"));
            }
            else
            {
                l.Add(Q($"The heaviest crate started in bay 1, so the crane had to move on <b>every</b> pass.  How many swaps is that for {n} crates?",
                        "One swap per pass, on every pass.  How many passes?",
                        $"{n - 1} passes, one swap each:  {n - 1} swaps, the most selection sort ever makes.  You made {p.swaps}.",
                        Nums(n - 1, n / 2, max, n)));
                l.Add(Q("Did this bad order change the number of comparisons?",
                        "Think back to the weighings in Room 1 (the sorted crates).",
                        $"Selection sort always compares n(n−1)/2 = {max} times.  Only the swaps change from case to case.",
                        $"No:  still {max}, the same as on the sorted row", $"Yes:  it doubled to {2 * max}", $"Yes:  it rose to {n * n}", $"No:  it was only {n - 1}"));
                l.Add(Q("Which sums up selection sort's three cases?",
                        "Think about the comparisons in each of this floor's three rooms.",
                        "Same comparisons every time, so it's O(n²) in every case.  Use it when moving things is expensive, not when comparing is.",
                        "Best, average and worst are all O(n²)",
                        "Best O(n), average and worst O(n²)",
                        "All three are O(n log n)",
                        "Best O(1), worst O(n)"));
            }
            return l;
        }

        // ------------------------------------------------------------ insertion sort

        static List<QuizQuestion> Insertion(SortCase c, int n, SortPuzzleBase p)
        {
            int max = Pairs(n), inv = p.StartInversions;
            var l = new List<QuizQuestion>();
            if (c == SortCase.Best)
            {
                l.Add(Q($"If all {n} books were <b>already sorted</b>, how many comparisons would insertion sort make?",
                        "The first book on its own is already a sorted section.  How many books are left to insert?",
                        $"Books 2 to {n}:  one comparison each and no shifts, so n − 1 = {n - 1}.",
                        Nums(n - 1, max, n, 0)));
                l.Add(Q("Insertion sort's <b>best-case</b> running time is…",
                        "The first book starts the sorted section. Each remaining book needs one comparison.",
                        "n − 1 comparisons and no shifts:  <b>O(n)</b>.  Insertion sort is <i>adaptive</i>:  the closer to sorted, the faster.",
                        ON, ON2, ONLOGN, OLOGN));
                l.Add(Q($"This shelf started with <b>{inv}</b> out-of-order {S(inv, "pair", "pairs")}.  How many shifts does insertion sort need?",
                        "One shift moves one bigger book past the new one.  How many wrong pairs does that fix?",
                        $"Each shift fixes exactly one out-of-order pair:  {inv} {S(inv, "pair", "pairs")}, {inv} {S(inv, "shift", "shifts")}.",
                        Nums(inv, n - 1, n, inv + n)));
            }
            else if (c == SortCase.Average)
            {
                string avg = (n * (n - 1) / 4f).ToString("0.#");
                l.Add(Q("Insertion sort's <b>average-case</b> running time is…",
                        "On average a new book travels past part of the sorted section.  Does that part grow with n?",
                        "Each new book moves past about half of the sorted section:  about n²/4 shifts, <b>O(n²)</b>.",
                        ON2, ON, ONLOGN, O1));
                l.Add(Q("On average, a new book moves past about how much of the sorted section?",
                        "In random order, a new book is bigger than about half of the books before it.",
                        $"About half, so about n(n−1)/4 ≈ {avg} shifts in total.  This shelf needed {inv}.",
                        "About half of it", "None of it", "All of it", "Exactly one book"));
                l.Add(Q("Why is insertion sort a good choice for <b>nearly sorted</b> data?",
                        "What decides how far each book moves?",
                        "Each out-of-order pair requires one shift. With few such pairs, insertion sort makes few shifts and still checks each book after the first.",
                        "Each book moves past only the larger books that came before it",
                        "It splits the shelf into halves",
                        "It uses a second shelf as extra memory",
                        "It picks a pivot first"));
            }
            else
            {
                l.Add(Q($"The shelf started in <b>reverse order</b>.  How many shifts does insertion sort need for {n} books?",
                        "Book 2 shifts past 1 book, book 3 past 2 books…",
                        $"To insert book k, shift all k − 1 earlier books one place to the right. That gives 1 + 2 + … + {n - 1} = {max} shifts.",
                        Nums(max, n, n - 1, n * n)));
                l.Add(Q("In the worst case, how many of the earlier books does book k move past?",
                        "In a reversed shelf, how does each new book compare with the sorted section?",
                        "Each new book is the smallest so far, so it travels all the way to the left:  past all k − 1 books.",
                        "Every book already in the sorted section (k − 1 books)", "Exactly one book", "No books", "Half of the sorted section"));
                l.Add(Q("Insertion sort's <b>worst-case</b> running time is…",
                        $"{max} shifts for {n} books.  What happens when n doubles?",
                        $"n(n−1)/2 = {max} comparisons and shifts:  <b>O(n²)</b>.",
                        ON2, ON, ONLOGN, OLOGN));
            }
            return l;
        }

        // ------------------------------------------------------------ merge sort

        static List<QuizQuestion> Merge(SortCase c, int n, SortPuzzleBase p)
        {
            int lg = Log2(n), max = Pairs(n);
            int bestC = n / 2 * lg, worstC = n * lg - n + 1, moves = n * lg;
            var l = new List<QuizQuestion>();
            if (c == SortCase.Best)
            {
                l.Add(Q($"How many <b>levels</b> of splitting does merge sort need for {n} blocks?",
                        "Keep halving until every run is a single block.",
                        $"{n} → {n / 2} → … → 1:  that's log₂{n} = {lg} levels.",
                        Nums(lg, n, n / 2, n - 1)));
                l.Add(Q($"On a sorted row each merge stops comparing as soon as its left run is empty.  How many comparisons for {n} sorted blocks?",
                        $"On each level, half of the blocks get compared.  How many levels are there?",
                        $"Each level compares n/2 = {n / 2} times, on {lg} levels:  {bestC}.  You made {p.comparisons}.",
                        Nums(bestC, worstC, max, n - 1)));
                l.Add(Q("Merge sort's <b>best-case</b> running time is…",
                        "Did the sorted order let you skip any splitting or merging?",
                        "Even sorted input is split and merged on every level:  n/2 · log₂n comparisons, still <b>O(n log n)</b>.",
                        ONLOGN, ON, ON2, OLOGN));
            }
            else if (c == SortCase.Average)
            {
                l.Add(Q("Merge sort's <b>average-case</b> running time is…",
                        "How many levels are there, and how much work does each level do?",
                        "log₂n levels, each moving all n blocks:  <b>O(n log n)</b>, whatever the order.",
                        ONLOGN, ON2, ON, OLOGN));
                l.Add(Q($"Every level of merging moves every block once.  With {lg} levels and {n} blocks, how many moves is that?",
                        "Blocks per level times levels.",
                        $"{n} blocks × {lg} levels = {moves} moves, for any order.",
                        Nums(moves, n + lg, n * n, n)));
                l.Add(Q("What does merge sort need that the earlier floors did not?",
                        "Where did the merged blocks go while a merge was running?",
                        "Merging needs somewhere to build the output:  O(n) extra memory.  That's its price for O(n log n) time.",
                        "Extra space:  the merge row holds the run being built", "A pivot", "Hidden weights", "Nothing extra"));
            }
            else
            {
                l.Add(Q($"This row was dealt so that every merge alternates between its two runs to the very end.  How many comparisons is that for {n} blocks?",
                        "If neither run empties until only one block remains, merging runs of a and b blocks takes a + b − 1 comparisons. Add those counts across all merges.",
                        $"n·log₂n − n + 1 = {worstC}, the most merge sort can need for {n} blocks.  You made {p.comparisons}.",
                        Nums(worstC, bestC, max, moves)));
                l.Add(Q("Merge sort's <b>worst-case</b> running time is…",
                        "Did the bad order add any levels?",
                        "There are always only log₂n levels, so even the worst case is <b>O(n log n)</b>.",
                        ONLOGN, ON2, ON, O1));
                l.Add(Q($"How many comparisons does <b>bubble sort</b> need on {n} reversed items, next to merge sort's worst case of {worstC}?",
                        "Bubble sort's worst case is n(n−1)/2.",
                        $"{max} against {worstC}.  For 1,000 items it's about 500,000 against 10,000:  O(n²) against O(n log n).",
                        Nums(max, worstC, bestC, n)));
            }
            return l;
        }

        // ------------------------------------------------------------ quick sort

        static List<QuizQuestion> Quick(SortCase c, int n, SortPuzzleBase p)
        {
            int lg = Log2(n), max = Pairs(n), bestQ = QuickPuzzle.MinComparisons(n);
            var l = new List<QuizQuestion>();
            if (c == SortCase.Best)
            {
                l.Add(Q("Each pivot here landed near the middle of its range.  Why is that quick sort's <b>best case</b>?",
                        "What happens to the size of the problem after a split down the middle?",
                        $"Halving gives about log₂{n} = {lg} levels with about n comparisons each:  O(n log n).  You made {p.comparisons} comparisons.",
                        "Each split halves the problem, so there are only about log₂n levels",
                        "Pivots in the middle are never compared",
                        "The bars were already sorted",
                        "No swaps are needed at all"));
                l.Add(Q($"In one partition of a range of {n} bars, how many comparisons with the pivot are made?",
                        "Which bars of the range get compared with the pivot, and how often?",
                        $"Every other bar of the range is compared with the pivot once:  {n - 1}.",
                        Nums(n - 1, n, lg, max)));
                l.Add(Q("Quick sort's <b>best-case</b> running time is…",
                        "About log₂n levels, about n comparisons per level.",
                        "Balanced partitions give about log₂ n levels, with at most n comparisons per level. This gives <b>O(n log n)</b> time.",
                        ONLOGN, ON, ON2, OLOGN));
            }
            else if (c == SortCase.Average)
            {
                l.Add(Q("Quick sort's <b>average-case</b> running time is…",
                        "Think about how random input affects where the last bar ranks within its range.",
                        "On randomly ordered input, the last bar can have any rank in its range. Averaging over the possible splits gives <b>O(n log n)</b> time.",
                        ONLOGN, ON2, ON, O1));
                l.Add(Q("After a partition, where is the pivot?",
                        "Where are the smaller bars and the bigger bars when the pivot drops in?",
                        "Everything smaller is left of it and everything bigger is right of it:  it's in its final place and never moves again.",
                        "In its final sorted position", "At the end of the row", "Back where it started", "In the middle of the row"));
                l.Add(Q("What do the \"to do\" ranges on the board stand for?",
                        "Each partition leaves two parts behind.  When do they get sorted?",
                        "Each partition can leave ranges to the left and right of the pivot. Quick sort still needs to process any of those ranges that contain two or more bars.",
                        "Ranges still waiting to be sorted", "Bars that are already sorted", "The swaps you made", "Your wrong moves"));
            }
            else
            {
                l.Add(Q($"These bars were <b>already sorted</b>, so every pivot (the last bar) was the biggest.  How many comparisons is that for {n} bars?",
                        "Each partition only peels off its pivot.  The next range is just one bar shorter.",
                        $"(n−1) + (n−2) + … + 1 = {max}.  The best case for {n} bars needs only {bestQ}.",
                        Nums(max, bestQ, n - 1, n)));
                l.Add(Q("Quick sort's <b>worst-case</b> running time is…",
                        "How many levels did the splitting take here instead of log₂n?",
                        "Each partition removes only the pivot from the remaining range. The n − 1 partitions perform n(n − 1)/2 comparisons in total, giving <b>O(n²)</b> time.",
                        ON2, ONLOGN, ON, OLOGN));
                l.Add(Q("Which pivot strategy makes this worst case less likely on already sorted input?",
                        "The problem was always choosing the biggest bar as the pivot.",
                        "A random pivot can land anywhere in the range. On sorted input, taking the median of the first, middle, and last bars picks a pivot near the middle.",
                        "Choose a random pivot or the median of the first, middle, and last bars", "Always use the last bar as the pivot", "Sort the input first", "Compare each bar twice"));
            }
            return l;
        }
    }
}
