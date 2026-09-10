using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;

namespace Zenith.Core
{
    public sealed class DiskItem
    {
        public string Name { get; init; }
        public string FullPath { get; init; }
        public long Size { get; init; }
        public bool IsFolder { get; init; }
        public double Fraction { get; init; }
        public string SizeText => Format.Bytes(Size);
        public string Glyph => IsFolder ? "\uE8B7" : "\uE8A5";
        public double BarWidth => Math.Max(2, Fraction * 220);
    }

    /// <summary>Scans a drive once and keeps folder sizes in memory for instant drill-down.</summary>
    public sealed class DiskScan
    {
        readonly Dictionary<string, long> _folderSize = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, HashSet<string>> _children = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<(string Path, long Size)>> _filesIn = new Dictionary<string, List<(string, long)>>(StringComparer.OrdinalIgnoreCase);
        readonly List<(string Path, long Size)> _largest = new List<(string, long)>();

        public string Root { get; }
        public long FileCount { get; private set; }
        public long TotalBytes => _folderSize.TryGetValue(Root, out var s) ? s : 0;

        DiskScan(string root) { Root = root.TrimEnd('\\') + "\\"; }

        public static DiskScan Run(string root, Action<long> progress, CancellationToken ct)
        {
            var scan = new DiskScan(root);
            scan.Scan(progress, ct);
            return scan;
        }

        static string Norm(string dir) => dir.Length <= 3 ? dir.TrimEnd('\\') + "\\" : dir.TrimEnd('\\');

        void Scan(Action<long> progress, CancellationToken ct)
        {
            var own = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline | (FileAttributes)0x400000 /*cloud placeholder*/,
                ReturnSpecialDirectories = false
            };
            var files = new FileSystemEnumerable<(string Dir, string Path, long Len)>(Root,
                (ref FileSystemEntry e) => (e.Directory.ToString(), e.ToFullPath(), e.Length), opts)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory
            };

            var top = new SortedSet<(long Size, string Path)>();
            foreach (var f in files)
            {
                if (ct.IsCancellationRequested) return;
                string dir = Norm(f.Dir);
                own[dir] = own.TryGetValue(dir, out var s) ? s + f.Len : f.Len;
                if (!_filesIn.TryGetValue(dir, out var list)) _filesIn[dir] = list = new List<(string, long)>();
                if (f.Len > 1024 * 1024) list.Add((f.Path, f.Len)); // keep only files > 1 MB for drill-down lists
                top.Add((f.Len, f.Path));
                if (top.Count > 100) top.Remove(top.Min);
                if (++FileCount % 5000 == 0) progress?.Invoke(FileCount);
            }

            // roll sizes up to every ancestor
            foreach (var kv in own)
            {
                string d = kv.Key;
                while (d != null && d.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                {
                    _folderSize[d] = _folderSize.TryGetValue(d, out var s) ? s + kv.Value : kv.Value;
                    string parent = d.Length <= Root.Length ? null : Norm(Path.GetDirectoryName(d) ?? Root);
                    if (parent != null)
                    {
                        if (!_children.TryGetValue(parent, out var kids)) _children[parent] = kids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        kids.Add(d);
                    }
                    d = parent;
                }
            }
            _largest.AddRange(top.Reverse().Select(t => (t.Path, t.Size)));
        }

        public List<DiskItem> ItemsIn(string folder)
        {
            folder = Norm(folder);
            long total = _folderSize.TryGetValue(folder, out var t) ? Math.Max(1, t) : 1;
            var items = new List<DiskItem>();
            if (_children.TryGetValue(folder, out var kids))
                items.AddRange(kids.Select(k => new DiskItem
                {
                    Name = Path.GetFileName(k), FullPath = k, IsFolder = true,
                    Size = _folderSize[k], Fraction = (double)_folderSize[k] / total
                }));
            if (_filesIn.TryGetValue(folder, out var files))
                items.AddRange(files.Select(f => new DiskItem
                {
                    Name = Path.GetFileName(f.Path), FullPath = f.Path, IsFolder = false,
                    Size = f.Size, Fraction = (double)f.Size / total
                }));
            return items.OrderByDescending(i => i.Size).Take(200).ToList();
        }

        public long SizeOf(string folder) => _folderSize.TryGetValue(Norm(folder), out var s) ? s : 0;

        public List<DiskItem> LargestFiles()
        {
            long max = _largest.Count > 0 ? Math.Max(1, _largest[0].Size) : 1;
            return _largest.Select(f => new DiskItem
            {
                Name = Path.GetFileName(f.Path), FullPath = f.Path, IsFolder = false, Size = f.Size, Fraction = (double)f.Size / max
            }).ToList();
        }
    }
}
