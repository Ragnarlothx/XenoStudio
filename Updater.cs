using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XenoStudio
{
    /// <summary>
    /// Updates XenoStudio from the latest GitHub release: check, download (SHA-256 checked against GitHub's digest),
    /// unpack, then a small script waits for the app to close, copies the new files over this folder (data\ and runtime\
    /// are never touched) and reopens it. Nothing happens without the user clicking Update and confirming.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "Ragnarlothx/XenoStudio";
        public static readonly string Dir = Path.Combine(Paths.Base, "_selfupdate");
        public static readonly string Lock = Path.Combine(Dir, "installing.lock");
        static readonly HttpClient Http = MakeClient();

        static HttpClient MakeClient()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("XenoStudio-updater");
            h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return h;
        }

        public class Info
        {
            public string Current { get; set; }
            public string Latest { get; set; }
            public bool Newer { get; set; }
            public string Notes { get; set; }
            public string Url { get; set; }          // release page
            public string Published { get; set; }
            public string Asset { get; set; }
            public long Size { get; set; }
            public string Digest { get; set; }       // "sha256:…" from GitHub
            public string DownloadUrl { get; set; }
            public bool CanInstall { get; set; }     // install folder is writable and the release has the app zip
            public string Why { get; set; }          // why it can't install here
        }

        static Version V(string s) => Version.TryParse(Regex.Match(s ?? "", @"\d+(\.\d+){1,3}").Value, out var v) ? v : new Version(0, 0);

        public static async Task<Info> Check(string current, CancellationToken ct = default)
        {
            var j = JsonNode.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
            var tag = (string)j["tag_name"] ?? "";
            var asset = (j["assets"] as JsonArray)?.FirstOrDefault(a => Regex.IsMatch((string)a?["name"] ?? "", @"^XenoStudio-v[\d.]+-win-x64\.zip$", RegexOptions.IgnoreCase));
            var i = new Info
            {
                Current = current, Latest = tag.TrimStart('v', 'V'), Newer = V(tag) > V(current),
                Notes = (string)j["body"] ?? "", Url = (string)j["html_url"], Published = (string)j["published_at"],
                Asset = (string)asset?["name"], Size = (long?)asset?["size"] ?? 0, Digest = (string)asset?["digest"],
                DownloadUrl = (string)asset?["browser_download_url"],
            };
            if (asset == null) i.Why = "The latest release has no Windows download.";
            else if (!Writable()) i.Why = "This folder can't be written to (for example Program Files). Download the new version from the release page instead.";
            i.CanInstall = i.Why == null;
            return i;
        }

        static bool Writable()
        {
            try { Directory.CreateDirectory(Dir); var t = Path.Combine(Dir, ".write-test"); File.WriteAllText(t, "x"); File.Delete(t); return true; }
            catch { return false; }
        }

        /// <summary>Downloads the zip, checks its SHA-256 against GitHub's digest and unpacks it. Returns the folder holding the new XenoStudio.exe.</summary>
        public static async Task<string> DownloadAndStage(Info i, Action<int> progress, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Dir);
            var zip = Path.Combine(Dir, i.Asset);
            using (var resp = await Http.GetAsync(i.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? i.Size;
                using var src = await resp.Content.ReadAsStreamAsync(ct);
                using var dst = File.Create(zip);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buf = new byte[1 << 16]; long done = 0; int last = -1, n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    sha.AppendData(buf, 0, n);
                    done += n;
                    var pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last) { last = pct; progress?.Invoke(pct); }
                }
                var hash = "sha256:" + Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                if (!string.IsNullOrEmpty(i.Digest) && !string.Equals(hash, i.Digest, StringComparison.OrdinalIgnoreCase))
                {
                    dst.Close(); File.Delete(zip);
                    throw new Exception("The download didn't match GitHub's checksum, so it was deleted. Try again later.");
                }
            }

            var stage = Path.Combine(Dir, "new");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            ZipFile.ExtractToDirectory(zip, stage);   // throws on entries that would land outside the folder
            File.Delete(zip);
            var exe = Directory.GetFiles(stage, "XenoStudio.exe", SearchOption.AllDirectories).OrderBy(p => p.Length).FirstOrDefault()
                      ?? throw new Exception("The download doesn't contain XenoStudio.exe.");
            var root = Path.GetDirectoryName(exe);
            var dll = Path.Combine(root, "XenoStudio.dll");
            if (!File.Exists(dll)) throw new Exception("The download is incomplete (XenoStudio.dll missing).");
            var got = FileVersionInfo.GetVersionInfo(dll).ProductVersion?.Split('+')[0];
            if (V(got) != V(i.Latest)) throw new Exception($"The download is version {got}, expected {i.Latest}.");
            // never let an update replace your data or the pinned CLIs
            foreach (var d in new[] { "data", "runtime", "_selfupdate", "_update" })
                if (Directory.Exists(Path.Combine(root, d))) Directory.Delete(Path.Combine(root, d), true);
            return root;
        }

        /// <summary>Starts the hidden install script. It waits for this process to exit; the caller then closes the window.</summary>
        public static void LaunchInstaller(string staged)
        {
            var script = Path.Combine(Dir, "apply.ps1");
            File.WriteAllText(script, Script, new UTF8Encoding(true));
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir };
            foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                                      "-ProcId", Environment.ProcessId.ToString(), "-Src", staged, "-Dst", Paths.Base.TrimEnd('\\'), "-Lock", Lock })
                psi.ArgumentList.Add(a);
            Process.Start(psi);
        }

        // Runs after XenoStudio exits. Holds installing.lock while copying (XenoStudio refuses to start meanwhile),
        // keeps one backup of the previous version in _selfupdate\previous, retries locked files, then reopens the app.
        const string Script = @"param([int]$ProcId, [string]$Src, [string]$Dst, [string]$Lock)
$ErrorActionPreference = 'Continue'
$log = Join-Path (Split-Path $Lock) 'update.log'
function Log($t) { ""$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $t"" | Add-Content $log }
function Busy { @(Get-Process XenoStudio, claude, codex -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Dst, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 }
Set-Content $Lock (Get-Date -Format o)
Log ""waiting for XenoStudio (pid $ProcId) to close""
try { Wait-Process -Id $ProcId -Timeout 120 -ErrorAction SilentlyContinue } catch { }
for ($i = 0; (Busy) -and $i -lt 60; $i++) { Start-Sleep 2 }
Start-Sleep 1
$prev = Join-Path (Split-Path $Lock) 'previous'
Log ""backing up the current version to $prev""
robocopy $Dst $prev /MIR /XD data runtime _selfupdate _update /NFL /NDL /NJH /NJS | Out-Null
for ($attempt = 1; $attempt -le 10; $attempt++) {
    Set-Content $Lock (Get-Date -Format o)
    robocopy $Src $Dst /E /XD data runtime /R:3 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    $rc = $LASTEXITCODE
    Log ""copy attempt ${attempt}: robocopy exit $rc (0-7 = ok)""
    if ($rc -lt 8) {
        Remove-Item $Lock -ErrorAction SilentlyContinue
        Remove-Item (Split-Path $Src) -Recurse -Force -ErrorAction SilentlyContinue
        Start-Process (Join-Path $Dst 'XenoStudio.exe') -WorkingDirectory $Dst
        Log 'updated; XenoStudio reopened'
        exit 0
    }
    Start-Sleep 3
}
Remove-Item $Lock -ErrorAction SilentlyContinue
Log ""FAILED: some files stayed locked. The previous version is in $prev; copy it back over $Dst if XenoStudio won't start.""
Start-Process (Join-Path $Dst 'XenoStudio.exe') -WorkingDirectory $Dst
";
    }
}
