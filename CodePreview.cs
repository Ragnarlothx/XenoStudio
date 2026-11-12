using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XenoStudio
{
    /// <summary>
    /// Host side of the code preview panel: reads files the AIs mention or edit, saves code blocks to disk, and hands
    /// code to XenoEdit for full editing. The panel itself (Monaco, served from web\lib\vs) lives in web\.
    /// </summary>
    static class CodePreview
    {
        const long MaxPreviewBytes = 2 * 1024 * 1024;
        static readonly string PreviewDir = Path.Combine(Paths.Data, "previews");

        /// <summary>Reads a text file for the preview panel. Relative paths resolve against the shared working folders.</summary>
        public static object ReadFile(string path, Settings s)
        {
            var full = Resolve(path, s) ?? throw new InvalidOperationException("File not found: " + path);
            var fi = new FileInfo(full);
            bool truncated = fi.Length > MaxPreviewBytes;
            byte[] bytes;
            using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[Math.Min(fi.Length, MaxPreviewBytes)];
                int read = 0;
                while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n == 0) break; read += n; }
            }
            if (LooksBinary(bytes)) throw new InvalidOperationException("That file isn't text, so it can't be previewed.");
            return new
            {
                path = full, name = fi.Name, size = fi.Length, modified = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds(),
                truncated, text = Decode(bytes),
            };
        }

        static string Resolve(string path, Settings s)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim().Trim('"', '\'', '`');
            if (Path.IsPathRooted(path)) return File.Exists(path) ? Path.GetFullPath(path) : null;
            foreach (var dir in new[] { s.PrimaryWorkDir }.Concat(s.ExtraDirs(includeClaudeExtras: true)))
            {
                try { var p = Path.GetFullPath(Path.Combine(dir, path)); if (File.Exists(p)) return p; } catch { }
            }
            return null;
        }

        static bool LooksBinary(byte[] b)
        {
            int n = Math.Min(b.Length, 8000);
            for (int i = 0; i < n; i++) if (b[i] == 0) return !(i + 1 < n && (b[0] == 0xFF || b[0] == 0xFE));   // NULs, unless UTF-16
            return false;
        }

        static string Decode(byte[] b)
        {
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            return new UTF8Encoding(false).GetString(b).TrimStart('﻿');
        }

        /// <summary>Safe file name from what the UI suggests (a code block's filename hint or "snippet.ts").</summary>
        public static string SafeName(string name, string fallbackExt = ".txt")
        {
            var n = Path.GetFileName((name ?? "").Trim());
            n = Regex.Replace(n, @"[^\w\-. ]+", "_").Trim(' ', '.');
            if (n.Length == 0) n = "snippet" + fallbackExt;
            return n.Length > 80 ? n[^80..] : n;
        }

        /// <summary>Writes a code block to a temp file under data\previews so XenoEdit can open it.</summary>
        public static string WriteTemp(string text, string name)
        {
            Directory.CreateDirectory(PreviewDir);
            // keep the folder small: previews older than a week go
            foreach (var f in Directory.GetFiles(PreviewDir))
                try { if (File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)) File.Delete(f); } catch { }
            var path = Path.Combine(PreviewDir, $"{DateTime.Now:HHmmss}-{SafeName(name)}");
            File.WriteAllText(path, text ?? "", new UTF8Encoding(false));
            return path;
        }

        // ---------------------------------------------------------------- XenoEdit
        /// <summary>XenoEdit.exe: the Settings path, a running copy, or a Desktop / Start Menu shortcut named XenoEdit.</summary>
        public static string FindXenoEdit(Settings s)
        {
            var configured = (s.XenoEditPath ?? "").Trim().Trim('"');
            if (configured != "" && File.Exists(configured)) return configured;
            try
            {
                foreach (var p in Process.GetProcessesByName("XenoEdit"))
                    using (p) try { var f = p.MainModule?.FileName; if (f != null && File.Exists(f)) return f; } catch { }
            }
            catch { }
            var folders = new[]
            {
                Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms,
            };
            foreach (var sf in folders)
            {
                string root;
                try { root = Environment.GetFolderPath(sf); } catch { continue; }
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                IEnumerable<string> links;
                try { links = Directory.EnumerateFiles(root, "*XenoEdit*.lnk", SearchOption.AllDirectories); } catch { continue; }
                foreach (var lnk in links)
                {
                    var target = ShortcutTarget(lnk);
                    if (target != null && target.EndsWith("XenoEdit.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) return target;
                }
            }
            return null;
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return null;
                dynamic shell = Activator.CreateInstance(type)!;
                string t = shell.CreateShortcut(lnk).TargetPath;
                return string.IsNullOrEmpty(t) ? null : t;
            }
            catch { return null; }
        }

        public static object OpenInXenoEdit(string path, Settings s)
        {
            var exe = FindXenoEdit(s) ?? throw new InvalidOperationException(
                "XenoEdit wasn't found. Set its location in Settings → General → XenoEdit path (or put a XenoEdit shortcut on the Desktop).");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { path }, WorkingDirectory = Path.GetDirectoryName(exe)! });
            return new { opened = path, exe };
        }
    }
}
