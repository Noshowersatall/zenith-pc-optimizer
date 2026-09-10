using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Zenith.Core
{
    /// <summary>Quick, repeatable benchmarks. Scores are only meant to be compared on the same PC (before vs after).</summary>
    public static class Benchmark
    {
        public static BenchResult Run(Action<string, double> report)
        {
            var r = new BenchResult { Date = DateTime.Now };
            var old = Process.GetCurrentProcess().PriorityClass;
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
                report("CPU · single core", 0.02); r.CpuSingle = CpuScore(1, 3000);
                report("CPU · all cores", 0.20); r.CpuMulti = CpuScore(Environment.ProcessorCount, 3000);
                report("Memory speed", 0.40); r.RamGBs = RamSpeed();
                report("Disk · sequential write", 0.52);
                DiskSpeed(r, report);
                report("Boot time", 0.97); r.BootSeconds = LastBootSeconds();
            }
            finally
            {
                try { Process.GetCurrentProcess().PriorityClass = old; } catch { }
            }
            report("Done", 1);
            return r;
        }

        // ---------- CPU: Mandelbrot iterations per second (floating point + branches) ----------
        static double CpuScore(int threads, int ms)
        {
            long total = 0;
            var sw = Stopwatch.StartNew();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
            {
                long local = 0;
                int row = t;
                while (sw.ElapsedMilliseconds < ms)
                {
                    local += MandelRow(row++ % 512);
                }
                Interlocked.Add(ref total, local);
            });
            double seconds = sw.Elapsed.TotalSeconds;
            return total / seconds / 1_000_000.0; // million iterations per second
        }

        static long MandelRow(int y)
        {
            long iters = 0;
            double ci = (y / 512.0) * 2.4 - 1.2;
            for (int x = 0; x < 512; x++)
            {
                double cr = (x / 512.0) * 3.0 - 2.1, zr = 0, zi = 0;
                int i = 0;
                while (i < 256 && zr * zr + zi * zi < 4)
                {
                    double t = zr * zr - zi * zi + cr;
                    zi = 2 * zr * zi + ci;
                    zr = t;
                    i++;
                }
                iters += i + 1;
            }
            return iters;
        }

        // ---------- RAM: multi-threaded copy bandwidth ----------
        static double RamSpeed()
        {
            const int size = 256 * 1024 * 1024;
            byte[] a = GC.AllocateUninitializedArray<byte>(size), b = GC.AllocateUninitializedArray<byte>(size);
            new Random(1).NextBytes(a.AsSpan(0, 1024 * 1024));
            int parts = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            int chunk = size / parts;
            // warm up (page in)
            a.AsSpan().CopyTo(b);
            var sw = Stopwatch.StartNew();
            const int passes = 8;
            for (int pass = 0; pass < passes; pass++)
                Parallel.For(0, parts, p => a.AsSpan(p * chunk, chunk).CopyTo(b.AsSpan(p * chunk, chunk)));
            double s = sw.Elapsed.TotalSeconds;
            GC.KeepAlive(a); GC.KeepAlive(b);
            return (double)size * passes * 2 / s / 1e9; // read + write GB/s
        }

        // ---------- Disk: unbuffered sequential read/write + 4K random reads ----------
        static unsafe void DiskSpeed(BenchResult r, Action<string, double> report)
        {
            const FileOptions NoBuffering = (FileOptions)0x20000000;
            const int block = 4 * 1024 * 1024;
            const long fileSize = 1024L * 1024 * 1024; // 1 GB
            string path = Path.Combine(Path.GetTempPath(), "zenith_bench.tmp");
            void* buf = NativeMemory.AlignedAlloc(block, 4096);
            try
            {
                var span = new Span<byte>(buf, block);
                new Random(7).NextBytes(span);

                using (var h = File.OpenHandle(path, FileMode.Create, FileAccess.Write, FileShare.None, NoBuffering | FileOptions.WriteThrough))
                {
                    var sw = Stopwatch.StartNew();
                    for (long off = 0; off < fileSize; off += block) RandomAccess.Write(h, span, off);
                    r.DiskWriteMBs = fileSize / sw.Elapsed.TotalSeconds / 1e6;
                }

                report("Disk · sequential read", 0.70);
                using (var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None, NoBuffering))
                {
                    var sw = Stopwatch.StartNew();
                    for (long off = 0; off < fileSize; off += block) RandomAccess.Read(h, span, off);
                    r.DiskReadMBs = fileSize / sw.Elapsed.TotalSeconds / 1e6;
                }

                report("Disk · 4K random read", 0.84);
                using (var h = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None, NoBuffering))
                {
                    var small = new Span<byte>(buf, 4096);
                    var rnd = new Random(42);
                    long pages = fileSize / 4096, ops = 0;
                    var sw = Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 3000)
                    {
                        RandomAccess.Read(h, small, rnd.NextInt64(pages) * 4096);
                        ops++;
                    }
                    r.Disk4kIops = ops / sw.Elapsed.TotalSeconds;
                }
            }
            catch (Exception ex) { Log.Write("Disk bench: " + ex.Message); }
            finally
            {
                NativeMemory.AlignedFree(buf);
                try { File.Delete(path); } catch { }
            }
        }

        // ---------- Boot time from Windows' own diagnostics log ----------
        public static double? LastBootSeconds()
        {
            var res = Shell.Run(Shell.Exe("wevtutil.exe"),
                "qe Microsoft-Windows-Diagnostics-Performance/Operational /q:\"*[System[(EventID=100)]]\" /c:1 /rd:true /f:xml", 30_000);
            var m = Regex.Match(res.Output, @"Name='BootTime'>(\d+)<");
            if (!m.Success) m = Regex.Match(res.Output, "Name=\"BootTime\">(\\d+)<");
            return m.Success ? Math.Round(long.Parse(m.Groups[1].Value) / 1000.0, 1) : (double?)null;
        }
    }
}
