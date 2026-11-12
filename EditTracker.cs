using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace XenoStudio
{
    /// <summary>One file an AI changed during a reply: shown as a change card, opened as a diff.</summary>
    public class FileEdit
    {
        public string Path { get; set; } = "";
        public string Who { get; set; } = "";
        public int Added { get; set; }
        public int Removed { get; set; }
        public bool Created { get; set; }
        public bool Deleted { get; set; }
        public bool BeforeUnknown { get; set; }   // the CLI only reported the edit after it happened
        public string Snap { get; set; } = "";    // folder under data\snapshots holding before.txt / after.txt
    }

    /// <summary>
    /// Watches the files an AI touches during one reply. The CLIs report an edit just before it happens, so the file is
    /// snapshotted then; when the reply ends, each file is compared with its snapshot and the differences are kept
    /// (data\snapshots\&lt;msg&gt;\&lt;n&gt;\before.txt / after.txt) for the diff view.
    /// Edits made through shell commands (e.g. a script writing a file) aren't reported by the CLIs, so they aren't tracked.
    /// </summary>
    public class EditTracker
    {
        const long MaxBytes = 4 * 1024 * 1024;
        static readonly string Root = System.IO.Path.Combine(Paths.Data, "snapshots");

        readonly string msgId, who;
        readonly Func<string, string> resolve;
        readonly ConcurrentDictionary<string, string> before = new(StringComparer.OrdinalIgnoreCase);   // path → content ("\0" = didn't exist)
        readonly ConcurrentDictionary<string, bool> unknown = new(StringComparer.OrdinalIgnoreCase);

        public EditTracker(string msgId, string who, Func<string, string> resolve) { this.msgId = msgId; this.who = who; this.resolve = resolve; }

        public bool Any => !before.IsEmpty || !unknown.IsEmpty;

        /// <summary>Called just before an AI edits <paramref name="path"/>. Returns the full path, or null to ignore.</summary>
        public string Before(string path)
        {
            var full = resolve(path);
            if (full == null) return null;
            before.GetOrAdd(full, p => Read(p) ?? "\0");
            return full;
        }

        /// <summary>
        /// Snapshots files before the AI's turn starts: ones its prompt mentions and ones edited earlier in the chat.
        /// Needed for Codex, which reports an edit only once the patch is already on disk.
        /// </summary>
        public void Prime(IEnumerable<string> paths)
        {
            foreach (var p in paths.Take(80))
            {
                var full = resolve(p);
                if (full != null && File.Exists(full)) before.TryAdd(full, Read(full) ?? "\0");
            }
        }

        /// <summary>
        /// An edit announcement that may arrive after the file already changed (Codex): uses the primed snapshot if
        /// there is one, otherwise records the "before" as unknown rather than snapshotting a half-edited file.
        /// </summary>
        public string Late(string path)
        {
            var full = resolve(path);
            if (full == null) return null;
            if (!before.ContainsKey(full)) unknown.TryAdd(full, true);
            return full;
        }

        /// <summary>Called after an edit the CLI didn't announce beforehand.</summary>
        public string After(string path)
        {
            var full = resolve(path);
            if (full == null || before.ContainsKey(full)) return full;
            unknown.TryAdd(full, true);
            return full;
        }

        /// <summary>Compares every touched file with its snapshot. Unchanged files are dropped.</summary>
        public List<FileEdit> Finish()
        {
            var result = new List<FileEdit>();
            int n = 0;
            foreach (var path in before.Keys.Concat(unknown.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                bool beforeKnown = before.TryGetValue(path, out var old);
                string now = Read(path);
                bool existedBefore = beforeKnown && old != "\0";
                if (beforeKnown && ((now == null && !existedBefore) || now == old)) continue;   // touched but not changed
                var oldText = existedBefore ? old : "";
                var (added, removed) = beforeKnown ? CountLines(oldText, now ?? "") : (0, 0);
                var snap = System.IO.Path.Combine(msgId, (n++).ToString());
                try
                {
                    var dir = System.IO.Path.Combine(Root, snap);
                    Directory.CreateDirectory(dir);
                    if (beforeKnown) File.WriteAllText(System.IO.Path.Combine(dir, "before.txt"), oldText, new UTF8Encoding(false));
                    File.WriteAllText(System.IO.Path.Combine(dir, "after.txt"), now ?? "", new UTF8Encoding(false));
                }
                catch { }
                result.Add(new FileEdit
                {
                    Path = path, Who = who, Added = added, Removed = removed, Snap = snap.Replace('\\', '/'),
                    Created = beforeKnown && !existedBefore && now != null, Deleted = now == null, BeforeUnknown = !beforeKnown,
                });
            }
            return result;
        }

        static string Read(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > MaxBytes) return null;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var rd = new StreamReader(fs, new UTF8Encoding(false), true);
                return rd.ReadToEnd();
            }
            catch { return null; }
        }

        /// <summary>Lines added / removed (multiset difference: fast and close to what a diff shows for typical edits).</summary>
        static (int added, int removed) CountLines(string a, string b)
        {
            var la = a.Replace("\r\n", "\n").Split('\n');
            var lb = b.Replace("\r\n", "\n").Split('\n');
            var counts = new Dictionary<string, int>();
            foreach (var l in la) counts[l] = counts.TryGetValue(l, out var c) ? c + 1 : 1;
            int added = 0;
            foreach (var l in lb)
                if (counts.TryGetValue(l, out var c) && c > 0) counts[l] = c - 1; else added++;
            return (added, counts.Values.Sum());
        }

        /// <summary>The stored before/after of one change card, for the diff view.</summary>
        public static object Diff(string snap)
        {
            if (string.IsNullOrWhiteSpace(snap) || snap.Contains("..")) throw new InvalidOperationException("bad snapshot id");
            var dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, snap));
            if (!dir.StartsWith(System.IO.Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("bad snapshot id");
            string R(string f) { var p = System.IO.Path.Combine(dir, f); return File.Exists(p) ? File.ReadAllText(p) : null; }
            return new { before = R("before.txt"), after = R("after.txt") ?? "", beforeUnknown = !File.Exists(System.IO.Path.Combine(dir, "before.txt")) };
        }

        /// <summary>Snapshots belong to a chat message: removed with the chat.</summary>
        public static void DeleteForMessages(IEnumerable<string> msgIds)
        {
            foreach (var id in msgIds)
                try { var d = System.IO.Path.Combine(Root, id); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }
    }
}
