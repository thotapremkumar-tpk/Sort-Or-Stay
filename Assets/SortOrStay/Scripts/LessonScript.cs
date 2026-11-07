using System.Collections.Generic;

namespace SortOrStay
{
    /// One frame of a lesson: the bars, what each bar is doing, and Ada's line for it.
    public class LessonStep
    {
        public const int Idle = 0, Compare = 1, Move = 2, Sorted = 3, Pivot = 4, Min = 5, Dim = 6;

        public int[] values;
        public int[] marks;          // per bar: Idle, Compare, Move, Sorted, Pivot, Min, Dim
        public bool[] gapAfter;      // merge sort: a gap after this bar (run boundary)
        public int divider = -1;     // quick sort: the gate stands just left of this bar
        public string caption;

        public LessonStep(int[] v, int[] m, string caption)
        {
            values = (int[])v.Clone();
            marks = (int[])m.Clone();
            this.caption = caption;
        }
    }

    /// A row of the best / average / worst table at the end of a lesson.
    public struct CaseRow
    {
        public string name, input, count, bigO;
        public CaseRow(string name, string input, string count, string bigO) { this.name = name; this.input = input; this.count = count; this.bigO = bigO; }
    }

    /// The on-screen lessons Professor Ada gives: each algorithm acted out on a few bars, step by step,
    /// then a table of its best, average and worst case (what input causes it, how many comparisons, Big-O).
    public static class LessonScript
    {
        public static string Title(int floor)
        {
            switch (floor)
            {
                case 1: return "HOW BUBBLE SORT WORKS";
                case 2: return "HOW SELECTION SORT WORKS";
                case 3: return "HOW INSERTION SORT WORKS";
                case 4: return "HOW MERGE SORT WORKS";
                default: return "HOW QUICK SORT WORKS";
            }
        }

        public static List<LessonStep> Steps(int floor)
        {
            switch (floor)
            {
                case 1: return Bubble(new[] { 4, 2, 5, 1, 3 });
                case 2: return Selection(new[] { 4, 2, 5, 1, 3 });
                case 3: return Insertion(new[] { 3, 5, 2, 4, 1 });
                case 4: return Merge(new[] { 6, 2, 7, 4, 1, 8, 3, 5 });
                default: return Quick(new[] { 5, 2, 6, 1, 4, 3 });
            }
        }

        public static CaseRow[] Cases(int floor, out string memory, out string takeaway)
        {
            switch (floor)
            {
                case 1:
                    memory = "Extra memory:  O(1), it swaps in place.";
                    takeaway = "Simple and good on nearly sorted rows (it stops after a pass with no swaps), slow on big random ones.";
                    return new[]
                    {
                        new CaseRow("Best", "already sorted", "n − 1", "O(n)"),
                        new CaseRow("Average", "random order", "about n²/2", "O(n²)"),
                        new CaseRow("Worst", "reverse order", "n(n−1)/2", "O(n²)"),
                    };
                case 2:
                    memory = "Extra memory:  O(1).  Swaps:  at most n − 1.";
                    takeaway = "Same comparisons in every case:  it never notices a sorted row.  Few swaps, so good when moving is costly.";
                    return new[]
                    {
                        new CaseRow("Best", "already sorted (0 swaps)", "n(n−1)/2", "O(n²)"),
                        new CaseRow("Average", "random order", "n(n−1)/2", "O(n²)"),
                        new CaseRow("Worst", "biggest first (n − 1 swaps)", "n(n−1)/2", "O(n²)"),
                    };
                case 3:
                    memory = "Extra memory:  O(1).  Shifts = out-of-order pairs.";
                    takeaway = "Adaptive:  the closer to sorted, the faster.  The go-to for small or nearly sorted lists.";
                    return new[]
                    {
                        new CaseRow("Best", "already sorted", "n − 1", "O(n)"),
                        new CaseRow("Average", "random order", "about n²/4", "O(n²)"),
                        new CaseRow("Worst", "reverse order", "n(n−1)/2", "O(n²)"),
                    };
                case 4:
                    memory = "Extra memory:  O(n), the merge row.";
                    takeaway = "Always log₂n levels, so it's O(n log n) in every case:  steady and predictable, at the price of extra space.";
                    return new[]
                    {
                        new CaseRow("Best", "already sorted", "(n/2) · log₂n", "O(n log n)"),
                        new CaseRow("Average", "random order", "about n · log₂n", "O(n log n)"),
                        new CaseRow("Worst", "merges alternate to the end", "n·log₂n − n + 1", "O(n log n)"),
                    };
                default:
                    memory = "Extra memory:  O(log n) for the \"to do\" stack (n in the worst case).";
                    takeaway = "Usually the fastest in practice, but a bad pivot every time makes it O(n²).  Random pivots avoid that.";
                    return new[]
                    {
                        new CaseRow("Best", "every pivot lands in the middle", "about n · log₂n", "O(n log n)"),
                        new CaseRow("Average", "random order", "about 1.39 n · log₂n", "O(n log n)"),
                        new CaseRow("Worst", "already sorted (pivot = biggest)", "n(n−1)/2", "O(n²)"),
                    };
            }
        }

        // ------------------------------------------------------------ the algorithms, acted out

        static int[] Marks(int n, int fill = LessonStep.Idle)
        {
            var m = new int[n];
            for (int i = 0; i < n; i++) m[i] = fill;
            return m;
        }

        static List<LessonStep> Bubble(int[] a)
        {
            var s = new List<LessonStep>();
            int n = a.Length;
            var m = Marks(n);
            s.Add(new LessonStep(a, m, "Bubble sort walks along the row comparing <b>neighbours</b>.  If the left one is bigger, they swap."));
            for (int pass = 0; pass < n - 1; pass++)
            {
                bool swapped = false;
                for (int j = 0; j < n - 1 - pass; j++)
                {
                    var c = (int[])m.Clone(); c[j] = c[j + 1] = LessonStep.Compare;
                    s.Add(new LessonStep(a, c, $"Pass {pass + 1}:  compare <b>{a[j]}</b> and <b>{a[j + 1]}</b>."));
                    if (a[j] > a[j + 1])
                    {
                        (a[j], a[j + 1]) = (a[j + 1], a[j]);
                        swapped = true;
                        var w = (int[])m.Clone(); w[j] = w[j + 1] = LessonStep.Move;
                        s.Add(new LessonStep(a, w, $"{a[j + 1]} > {a[j]}:  <b>swap</b> them."));
                    }
                    else s.Add(new LessonStep(a, c, $"{a[j]} < {a[j + 1]}:  already in order, they <b>stay</b>."));
                }
                m[n - 1 - pass] = LessonStep.Sorted;
                if (!swapped)
                {
                    for (int i = 0; i < n; i++) m[i] = LessonStep.Sorted;
                    s.Add(new LessonStep(a, m, "No swaps in this whole pass, so the row is sorted:  <b>stop early</b>.  That's why a sorted row costs only n − 1 comparisons."));
                    return s;
                }
                s.Add(new LessonStep(a, m, $"End of pass {pass + 1}:  the biggest unsorted value, <b>{a[n - 1 - pass]}</b>, has bubbled to the right.  It's final."));
            }
            for (int i = 0; i < n; i++) m[i] = LessonStep.Sorted;
            s.Add(new LessonStep(a, m, "Sorted.  Each pass fixes one more value at the end:  up to n − 1 passes, n(n−1)/2 comparisons."));
            return s;
        }

        static List<LessonStep> Selection(int[] a)
        {
            var s = new List<LessonStep>();
            int n = a.Length;
            var m = Marks(n);
            s.Add(new LessonStep(a, m, "Selection sort <b>scans</b> the unsorted part for the smallest value, then swaps it to the front.  Repeat."));
            for (int i = 0; i < n - 1; i++)
            {
                int min = i;
                var c = (int[])m.Clone(); c[i] = LessonStep.Min;
                s.Add(new LessonStep(a, c, $"Pass {i + 1}:  start with <b>{a[i]}</b> as the smallest so far."));
                for (int j = i + 1; j < n; j++)
                {
                    c = (int[])m.Clone(); c[min] = LessonStep.Min; c[j] = LessonStep.Compare;
                    bool smaller = a[j] < a[min];
                    s.Add(new LessonStep(a, c, smaller ? $"<b>{a[j]}</b> < {a[min]}:  new smallest." : $"{a[j]} is not smaller than <b>{a[min]}</b>."));
                    if (smaller) min = j;
                }
                if (min != i)
                {
                    (a[i], a[min]) = (a[min], a[i]);
                    c = (int[])m.Clone(); c[i] = c[min] = LessonStep.Move;
                    s.Add(new LessonStep(a, c, $"Swap the smallest, <b>{a[i]}</b>, into place {i + 1}."));
                }
                else s.Add(new LessonStep(a, m, $"<b>{a[i]}</b> is already in place {i + 1}:  no swap."));
                m[i] = LessonStep.Sorted;
            }
            m[n - 1] = LessonStep.Sorted;
            s.Add(new LessonStep(a, m, "Sorted.  Every pass scanned <b>all</b> of the rest, even when nothing moved:  n(n−1)/2 comparisons, every time."));
            return s;
        }

        static List<LessonStep> Insertion(int[] a)
        {
            var s = new List<LessonStep>();
            int n = a.Length;
            var m = Marks(n); m[0] = LessonStep.Sorted;
            s.Add(new LessonStep(a, m, "Insertion sort grows a <b>sorted part</b> on the left.  The first value on its own is already sorted."));
            for (int i = 1; i < n; i++)
            {
                int key = a[i];
                var c = (int[])m.Clone(); c[i] = LessonStep.Min;
                s.Add(new LessonStep(a, c, $"Take the next value, <b>{key}</b>, and slide it left into the sorted part."));
                int j = i;
                while (j > 0)
                {
                    c = (int[])m.Clone();
                    for (int k = 0; k < i + 1; k++) c[k] = LessonStep.Sorted;
                    c[j] = LessonStep.Min; c[j - 1] = LessonStep.Compare;
                    if (a[j - 1] > key)
                    {
                        s.Add(new LessonStep(a, c, $"{a[j - 1]} > {key}:  <b>shift</b> {a[j - 1]} one place right."));
                        (a[j - 1], a[j]) = (a[j], a[j - 1]);
                        j--;
                    }
                    else
                    {
                        s.Add(new LessonStep(a, c, $"{a[j - 1]} < {key}:  stop.  <b>{key}</b> belongs right here."));
                        break;
                    }
                }
                for (int k = 0; k <= i; k++) m[k] = LessonStep.Sorted;
                s.Add(new LessonStep(a, m, $"The first {i + 1} values are sorted."));
            }
            s.Add(new LessonStep(a, m, "Sorted.  Every shift fixed one out-of-order pair:  few pairs (nearly sorted) means very little work."));
            return s;
        }

        static List<LessonStep> Merge(int[] a)
        {
            var s = new List<LessonStep>();
            int n = a.Length;
            var idle = Marks(n);
            var runs = new List<int> { n };     // run lengths, left to right
            s.Add(Gapped(a, idle, runs, "Merge sort first <b>splits</b> the row in halves, again and again, until every value stands alone."));
            int level = 0;
            while (true)
            {
                var next = new List<int>();
                bool split = false;
                foreach (int r in runs)
                {
                    if (r > 1) { next.Add(r / 2); next.Add(r - r / 2); split = true; }
                    else next.Add(r);
                }
                if (!split) break;
                runs = next;
                level++;
                s.Add(Gapped(a, idle, runs, $"Split, level {level}:  runs of {runs[0]}."));
            }
            s.Add(Gapped(a, idle, runs, $"{level} levels of splitting for {n} values:  log₂{n} = {level}.  A single value is sorted."));
            // merge pairs of runs back together
            while (runs.Count > 1)
            {
                var next = new List<int>();
                int start = 0;
                for (int k = 0; k < runs.Count; k += 2)
                {
                    if (k + 1 >= runs.Count) { next.Add(runs[k]); start += runs[k]; continue; }
                    int la = runs[k], lb = runs[k + 1];
                    var merged = new List<int>();
                    int i = start, j = start + la, cmp = 0;
                    while (i < start + la && j < start + la + lb) { cmp++; merged.Add(a[i] <= a[j] ? a[i++] : a[j++]); }
                    while (i < start + la) merged.Add(a[i++]);
                    while (j < start + la + lb) merged.Add(a[j++]);
                    for (int t = 0; t < merged.Count; t++) a[start + t] = merged[t];
                    var after = Marks(n);
                    for (int t = start; t < start + la + lb; t++) after[t] = LessonStep.Move;
                    next.Add(la + lb);
                    var runsAfter = new List<int>(next);
                    for (int t = k + 2; t < runs.Count; t++) runsAfter.Add(runs[t]);
                    s.Add(Gapped(a, after, runsAfter, $"<b>Merge</b> two runs:  take the smaller front value each time ({cmp} comparison{(cmp == 1 ? "" : "s")})."));
                    start += la + lb;
                }
                runs = next;
            }
            s.Add(Gapped(a, Marks(n, LessonStep.Sorted), runs, $"Sorted.  {level} levels, and every level moves all {n} values:  n · log₂n work, whatever the order."));
            return s;
        }

        static LessonStep Gapped(int[] a, int[] marks, List<int> runs, string caption)
        {
            var st = new LessonStep(a, marks, caption) { gapAfter = new bool[a.Length] };
            int p = 0;
            for (int r = 0; r < runs.Count - 1; r++) { p += runs[r]; if (p - 1 < a.Length) st.gapAfter[p - 1] = true; }
            return st;
        }

        static List<LessonStep> Quick(int[] a)
        {
            var s = new List<LessonStep>();
            int n = a.Length;
            var done = new bool[n];
            s.Add(new LessonStep(a, Marks(n), "Quick sort picks a <b>pivot</b> (here the last value) and splits the rest:  smaller to the left of a gate, bigger to the right."));
            var todo = new Stack<(int lo, int hi)>();
            todo.Push((0, n - 1));
            while (todo.Count > 0)
            {
                var (lo, hi) = todo.Pop();
                if (lo > hi) continue;
                if (lo == hi)
                {
                    done[lo] = true;
                    s.Add(Q(a, done, lo, hi, -1, -1, -1, $"<b>{a[lo]}</b> is on its own:  one value is always in place."));
                    continue;
                }
                int p = a[hi], w = lo;
                s.Add(Q(a, done, lo, hi, hi, -1, w, $"Range {lo + 1}-{hi + 1}:  the pivot is the last value, <b>{p}</b>."));
                for (int i = lo; i < hi; i++)
                {
                    if (a[i] < p)
                    {
                        s.Add(Q(a, done, lo, hi, hi, i, w, $"{a[i]} < {p}:  smaller, so it goes <b>behind the gate</b>."));
                        (a[i], a[w]) = (a[w], a[i]);
                        w++;
                    }
                    else s.Add(Q(a, done, lo, hi, hi, i, w, $"{a[i]} > {p}:  bigger, it <b>stays</b> right of the gate."));
                }
                (a[w], a[hi]) = (a[hi], a[w]);
                done[w] = true;
                s.Add(Q(a, done, lo, hi, -1, -1, -1, $"Drop the pivot <b>{p}</b> at the gate:  it's in its <b>final</b> place.  Now sort each side the same way."));
                todo.Push((w + 1, hi));
                todo.Push((lo, w - 1));
            }
            s.Add(new LessonStep(a, Marks(n, LessonStep.Sorted), "Sorted.  Good pivots halve the work each time (n log n);  a pivot that is always the biggest makes it n²."));
            return s;
        }

        static LessonStep Q(int[] a, bool[] done, int lo, int hi, int pivot, int cur, int gate, string caption)
        {
            int n = a.Length;
            var m = new int[n];
            for (int i = 0; i < n; i++)
                m[i] = done[i] ? LessonStep.Sorted : (i < lo || i > hi) ? LessonStep.Dim : LessonStep.Idle;
            if (pivot >= 0) m[pivot] = LessonStep.Pivot;
            if (cur >= 0) m[cur] = LessonStep.Compare;
            return new LessonStep(a, m, caption) { divider = gate };
        }
    }
}
