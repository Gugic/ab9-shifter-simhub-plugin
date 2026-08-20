using System;
using System.Globalization;
using System.IO;
using AB9ActiveShifter.Core;
using Xunit;

namespace AB9ActiveShifter.Tests
{
    /// <summary>
    /// The recorder keeps the LAST two minutes, not the first.
    ///
    /// These exist because the old behaviour cost a real measurement: the recorder stopped itself
    /// at capacity, so a session that ran fifty minutes before the base froze had the first two
    /// minutes of the race in the buffer and nothing else - and because stopping itself cleared
    /// IsRecording, the press meant to save it started a fresh recording and discarded it instead.
    /// Both halves of that are pinned here.
    /// </summary>
    public class TraceRecorderTests
    {
        private static void Feed(TraceRecorder r, int from, int count)
        {
            for (int i = from; i < from + count; i++)
            {
                // x carries the tick number, so a saved file says exactly which ticks survived.
                r.Add(i, i, 0, 0, 0, 1.0, GateState.Neutral, Column.None, ShiftDir.None, 0, 0, 0);
            }
        }

        private static string SaveToTemp(TraceRecorder r, out string directory)
        {
            directory = Path.Combine(Path.GetTempPath(),
                "ab9-trace-tests-" + Guid.NewGuid().ToString("N"));
            return r.Save(directory, new EngineConfig(), null);
        }

        private static int[] XColumnOf(string path)
        {
            string[] lines = File.ReadAllLines(path);
            var xs = new System.Collections.Generic.List<int>();

            foreach (string line in lines)
            {
                if (line.Length == 0 || line[0] == '#' || line.StartsWith("ms,", StringComparison.Ordinal)) continue;
                string[] parts = line.Split(',');
                xs.Add(int.Parse(parts[1], CultureInfo.InvariantCulture));
            }

            return xs.ToArray();
        }

        [Fact]
        public void AShortRecordingKeepsEveryTickItWasGiven()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, 500);
            r.Stop();

            Assert.Equal(500, r.Count);
            Assert.Equal(0, r.Dropped);
            Assert.False(r.IsFull);
        }

        [Fact]
        public void RecordingDoesNotStopItselfWhenTheBufferFills()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, TraceRecorder.Capacity + 10);

            // The whole point: a recording left running through a session is still running when
            // the thing worth capturing finally happens.
            Assert.True(r.IsRecording);
        }

        [Fact]
        public void PastCapacityItHoldsTheNewestTicksAndSaysHowManyItDropped()
        {
            var r = new TraceRecorder();
            r.Start();

            const int extra = 5000;
            Feed(r, 0, TraceRecorder.Capacity + extra);
            r.Stop();

            Assert.Equal(TraceRecorder.Capacity, r.Count);
            Assert.Equal(extra, r.Dropped);
            Assert.True(r.IsFull);

            string directory;
            string path = SaveToTemp(r, out directory);

            try
            {
                int[] xs = XColumnOf(path);

                Assert.Equal(TraceRecorder.Capacity, xs.Length);

                // Oldest surviving tick first, newest last, contiguous in between - the wrap must
                // not show up as a seam in the middle of the file.
                Assert.Equal(extra, xs[0]);
                Assert.Equal(TraceRecorder.Capacity + extra - 1, xs[xs.Length - 1]);

                for (int i = 1; i < xs.Length; i++) Assert.Equal(xs[i - 1] + 1, xs[i]);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void TheHeaderSaysTheFileIsATailWheneverItIs()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, TraceRecorder.Capacity + 1234);
            r.Stop();

            string directory;
            string path = SaveToTemp(r, out directory);

            try
            {
                string text = File.ReadAllText(path);

                // A ms column starting at 121 234 is not a broken clock, and the file has to say so
                // on its own - it will be read long after the session that produced it.
                Assert.Contains("the last " + TraceRecorder.Capacity + " ticks of "
                                + (TraceRecorder.Capacity + 1234), text);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void AShortTraceSaysNothingAboutDroppingAnything()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, 100);
            r.Stop();

            string directory;
            string path = SaveToTemp(r, out directory);

            try
            {
                Assert.DoesNotContain("the last ", File.ReadAllText(path));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void StartingAgainForgetsTheOldRecordingCompletely()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, TraceRecorder.Capacity + 900);

            r.Start();
            Feed(r, 7000, 3);
            r.Stop();

            Assert.Equal(3, r.Count);
            Assert.Equal(0, r.Dropped);
            Assert.False(r.IsFull);
        }

        [Fact]
        public void ClearingLeavesNothingToSave()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, 250);
            r.Stop();
            r.Clear();

            // What makes the record button a toggle again: a saved trace stops counting as
            // savable, so the next press starts a recording instead of writing the same file twice.
            Assert.Equal(0, r.Count);
            Assert.Equal(0, r.Dropped);
        }

        [Fact]
        public void AStoppedRecorderIgnoresWhateverTheLoopStillHasInFlight()
        {
            var r = new TraceRecorder();
            r.Start();
            Feed(r, 0, 10);
            r.Stop();
            Feed(r, 900, 10);

            Assert.Equal(10, r.Count);
        }
    }
}
