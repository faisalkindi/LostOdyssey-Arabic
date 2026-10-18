// Lost Odyssey - Arabic installer (Enma World). The Arabic is a language pack for LostOdysseyRecomp: the folder
// mods\ar next to LostOdysseyRecomp.exe (language.ini, text\, fonts\), with the tutorial pictures in Arabic as the mod
// mods\ar-images. Install puts both there, checks every file, lists ar-images first in mod-list.ini when the player has
// one (a listed mod such as the HD texture pack would win otherwise), and selects the pack in settings.ini
// (game_language=2 Japanese, the pack's base; game_language_pack=ar). Uninstall removes both and puts the previous
// language back. The game's own files are never touched.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;

namespace LostOdysseyArabic
{
    static class Program
    {
        public const string Version = "1.0.0";
        public const string Exe = "LostOdysseyRecomp.exe";
        const string ModsDir = "mods";                                   // manifest paths are under it: ar/..., ar-images/...
        const string PackDir = @"mods\ar";
        const string ImagesId = "ar-images";
        const string ModList = "mod-list.ini";
        const string StateDir = @"Enma\Lost Odyssey Arabic";              // the install record and the font licences
        const string Settings = "settings.ini";
        const string PackId = "ar";
        const string JapaneseId = "2";                                   // game_language: the pack's base (jpn)
        // Text only in recomp builds that draw right-to-left packs (language.ini direction=rtl, PR 591).
        // Any one is enough, so a reworded message alone does not turn a working build away; none found only warns.
        static readonly byte[][] RtlMarkers = { Encoding.ASCII.GetBytes("direction must be ltr or rtl"), Encoding.ASCII.GetBytes("an optional direction=") };
        // The first release with PR 636 (Arabic pop-ups not cut every 26 letters, which the pack's longer rows need;
        // also PR 630), read from the manifest.json a release keeps next to the exe; a build without one (built
        // from source) is judged by the markers. v1.0.5 (2026-10-18) is the first release with PR 636.
        static readonly Version MinRecomp = new(1, 0, 5);
        public const string RecompReleases = "https://github.com/freefrank/LostOdysseyRecomp/releases";
        public const string Site = "https://enmaworld.com";
        public const string Kofi = "https://ko-fi.com/kindiboy";
        static readonly string[] GameProcesses = { "LostOdysseyRecomp" };
        static readonly (string entry, string name)[] Licences = { ("licenses/Rubik-OFL.txt", "Rubik - OFL.txt"), ("licenses/AlYamama-OFL.txt", "AlYamama - OFL.txt") };
        public static bool NoDetect;
        public static string ShotGame, ShotShow;          // screenshot only: this game folder, shown as this path
        static JsonElement Manifest;
        static string[] PackFiles;

        [STAThread]
        static int Main(string[] args)
        {
            using (var z = OpenPayload())
            using (var r = new StreamReader(z.GetEntry("manifest.json").Open())) Manifest = JsonDocument.Parse(r.ReadToEnd()).RootElement;
            PackFiles = Manifest.GetProperty("files").EnumerateObject().Select(p => p.Name).ToArray();
            // headless mode for testing: --detect | --find <dir> | --status <game> | --install <game> | --uninstall <game> | --screenshot <png> [--not-found]
            if (args.Length > 0)
            {
                try
                {
                    if (args[0] == "--detect") { Console.WriteLine(DetectGamePath() ?? "(not found)"); return 0; }
                    if (args[0] == "--find") { Console.WriteLine(FindGameFolder(args[1]) ?? "(not found)"); return 0; }
                    if (args[0] == "--status")
                    {
                        Console.WriteLine(Detect(args[1], true) + (SupportsArabic(args[1]) ? " recomp:ok" : " recomp:old") +
                                          " language:" + (ArabicSelected(args[1]) ? "ar" : "other"));
                        return 0;
                    }
                    if (args[0] == "--install")
                    {
                        var rep = Install(args[1], Console.WriteLine, args.Length > 2 && args[2] == "--force");
                        Console.WriteLine($"selected: {rep.Selected}, other packs with id ar: {string.Join(",", rep.OtherPacks)}");
                        Console.WriteLine("state: " + Detect(args[1], true)); return 0;
                    }
                    if (args[0] == "--uninstall") { Uninstall(args[1], Console.WriteLine); Console.WriteLine("state: " + Detect(args[1], true)); return 0; }
                    if (args[0] == "--screenshot")                     // the window drawn to a PNG, never shown (README / Nexus media)
                    {
                        NoDetect = args.Length > 2 && args[2] == "--not-found";
                        if (args.Length > 4 && args[2] == "--game") { ShotGame = args[3]; ShotShow = args[4]; NoDetect = true; }   // --game <dir> <shown path>
                        if (ShotGame != null) SupportsArabic(ShotGame);
                        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(true);
                        using var f = new MainForm { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
                        f.Show(); Application.DoEvents();
                        if (!NoDetect) { f.WaitForDetection(); Application.DoEvents(); }
                        using var bmp = new Bitmap(f.Width, f.Height); f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        // DrawToBitmap leaves stray pixels with alpha 0: opaque inside the window's rounded shape, clear outside
                        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadWrite,
                                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        var buf = new byte[data.Stride * bmp.Height];
                        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                        using (var shape = new Region(Ui.Round(new Rectangle(0, 0, bmp.Width, bmp.Height), (int)(20 * f.DeviceDpi / 96f))))
                            for (int y = 0; y < bmp.Height; y++)
                                for (int x = 0; x < bmp.Width; x++)
                                    buf[y * data.Stride + x * 4 + 3] = shape.IsVisible(x, y) ? (byte)255 : (byte)0;
                        Marshal.Copy(buf, 0, data.Scan0, buf.Length);
                        bmp.UnlockBits(data);
                        bmp.Save(args[1]); return 0;
                    }
                }
                catch (Exception ex) { Console.Error.WriteLine("error: " + ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(true);
            Application.Run(new MainForm());
            return 0;
        }

        static ZipArchive OpenPayload() =>
            new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                           ?? throw new Exception("ملفات التعريب المضمّنة غير موجودة داخل المثبّت."), ZipArchiveMode.Read);

        // ---- finding LostOdysseyRecomp ---------------------------------------
        public static bool IsValidGameFolder(string folder) => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, Exe));

        // the picked folder, or the game folder up to three levels inside it
        public static string FindGameFolder(string picked, int depth = 3)
        {
            if (IsValidGameFolder(picked)) return picked;
            if (depth == 0) return null;
            try
            {
                foreach (string d in Directory.EnumerateDirectories(picked))
                    if (FindGameFolder(d, depth - 1) is string found) return found;
            }
            catch { }
            return null;
        }

        // The recomp has no installer or registry entry: the running game, the folder this installer sits in (and the
        // ones above it), then every copy in the usual places a portable game is unpacked to, a few levels deep. Players
        // often keep one folder per release, so of several copies the one that shows Arabic wins, then the newest exe.
        public static int FoundCount;
        public static string DetectGamePath()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(GameProcesses[0]))
                    try { if (Path.GetDirectoryName(p.MainModule?.FileName) is string d && IsValidGameFolder(d)) { FoundCount = 1; return d; } } catch { }
            }
            catch { }
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (IsValidGameFolder(d.FullName)) { FoundCount = 1; return d.FullName; }
            var deadline = DateTime.UtcNow.AddSeconds(15);
            string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.Combine(user, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(user, "Games"),
            };
            foreach (var drive in DriveInfo.GetDrives())
                try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) roots.Add(drive.RootDirectory.FullName); } catch { }
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
                Search(root, 3, deadline, found);
            FoundCount = found.Count;
            return found.OrderByDescending(SupportsArabic)
                        .ThenByDescending(g => { try { return File.GetLastWriteTimeUtc(Path.Combine(g, Exe)); } catch { return DateTime.MinValue; } })
                        .FirstOrDefault();
        }

        static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData", "AppData", "node_modules", ".git",
            "Recovery", "PerfLogs", "$WinREAgent", "WindowsApps", "Microsoft", "steamapps",
        };

        // breadth first, every game folder down to depth (a game folder is not searched further); up to 32 copies
        static void Search(string root, int depth, DateTime deadline, HashSet<string> found)
        {
            var level = new List<string> { root };
            for (int i = 0; i <= depth && level.Count > 0 && DateTime.UtcNow < deadline && found.Count < 32; i++)
            {
                var next = new List<string>();
                foreach (string d in level)
                {
                    if (IsValidGameFolder(d)) { found.Add(Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar)); continue; }
                    if (i == depth || DateTime.UtcNow >= deadline) continue;
                    try
                    {
                        foreach (var sub in new DirectoryInfo(d).EnumerateDirectories())
                            if (!Skip.Contains(sub.Name) && !sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) next.Add(sub.FullName);
                    }
                    catch { }
                }
                level = next;
            }
        }

        // ---- the recomp build ----------------------------------------------------
        static readonly Dictionary<string, (DateTime, long, bool)> RtlCache = new(StringComparer.OrdinalIgnoreCase);
        public static bool SupportsArabic(string game) => !ReleaseOlderThanMin(game) && HasRtlMarkers(game);

        // A release whose manifest.json names a version before MinRecomp; false when there is none or it does not parse.
        static bool ReleaseOlderThanMin(string game)
        {
            try
            {
                string path = Path.Combine(game, "manifest.json");
                if (!File.Exists(path)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("version", out var v) || v.ValueKind != JsonValueKind.String) return false;
                return System.Version.TryParse(v.GetString().TrimStart('v', 'V'), out var version) && version < MinRecomp;
            }
            catch { return false; }
        }

        static bool HasRtlMarkers(string game)
        {
            string exe = Path.Combine(game, Exe);
            try
            {
                var info = new FileInfo(exe);
                lock (RtlCache)
                    if (RtlCache.TryGetValue(exe, out var c) && c.Item1 == info.LastWriteTimeUtc && c.Item2 == info.Length) return c.Item3;
                bool found = RtlMarkers.Any(m => Contains(exe, m));
                lock (RtlCache) RtlCache[exe] = (info.LastWriteTimeUtc, info.Length, found);
                return found;
            }
            catch { return false; }
        }

        static bool Contains(string path, byte[] needle)
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
            var buf = new byte[(1 << 20) + needle.Length];
            int carry = 0, read;
            while ((read = f.Read(buf, carry, buf.Length - carry)) > 0)
            {
                int n = carry + read;
                if (buf.AsSpan(0, n).IndexOf(needle) >= 0) return true;
                carry = Math.Min(needle.Length - 1, n);
                Array.Copy(buf, n - carry, buf, 0, carry);
            }
            return false;
        }

        // ---- settings.ini (next to the exe) -----------------------------------------
        static string SettingsPath(string game) => Path.Combine(game, Settings);

        static string ReadSetting(string game, string key)
        {
            string p = SettingsPath(game);
            if (!File.Exists(p)) return null;
            foreach (string line in File.ReadAllLines(p))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line.Substring(0, eq).Trim() == key) return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        // Sets keys that are there and adds the ones that are not; other lines stay as they are.
        static void WriteSettings(string game, IDictionary<string, string> values)
        {
            string p = SettingsPath(game);
            string text = File.ReadAllText(p);
            const string crlf = "\r\n", lf = "\n";
            string nl = text.Contains(crlf) ? crlf : lf;              // the file keeps its own line ends
            bool endsWithNewline = text.EndsWith(lf);
            var lines = text.Replace(crlf, lf).Split('\n').ToList();
            if (endsWithNewline) lines.RemoveAt(lines.Count - 1);
            var left = new Dictionary<string, string>(values);
            for (int i = 0; i < lines.Count; i++)
            {
                int eq = lines[i].IndexOf('=');
                if (eq <= 0) continue;
                string key = lines[i].Substring(0, eq).Trim();
                if (left.TryGetValue(key, out var v)) { lines[i] = key + "=" + v; left.Remove(key); }
            }
            lines.AddRange(left.Select(kv => kv.Key + "=" + kv.Value));
            File.WriteAllText(p, string.Join(nl, lines) + (endsWithNewline || left.Count > 0 ? nl : ""), new UTF8Encoding(false));
        }

        public static bool ArabicSelected(string game) => ReadSetting(game, "game_language_pack") == PackId && ReadSetting(game, "game_language") == JapaneseId;

        // ---- state -------------------------------------------------------------
        public enum State { NotInstalled, Installed, OlderVersion }
        public class InstallState
        {
            public string Version { get; set; }
            public List<string> Files { get; set; } = new();
            public string PreviousLanguage { get; set; }       // settings.ini before the first install (null: no settings.ini)
            public string PreviousPack { get; set; }
            public bool ModListAdded { get; set; }              // the ar-images line in mod-list.ini is ours
        }
        public class Report { public bool Selected; public List<string> OtherPacks = new(); }

        static string StatePath(string game) => Path.Combine(game, StateDir, "install.json");
        static InstallState LoadState(string game)
        {
            try { return File.Exists(StatePath(game)) ? JsonSerializer.Deserialize<InstallState>(File.ReadAllText(StatePath(game))) : null; }
            catch { return null; }
        }
        static void SaveState(string game, InstallState s)
        {
            Directory.CreateDirectory(Path.Combine(game, StateDir));
            File.WriteAllText(StatePath(game), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }

        static string Md5(string path)
        {
            if (!File.Exists(path)) return "";
            using var md = MD5.Create(); using var f = File.OpenRead(path);
            return Convert.ToHexString(md.ComputeHash(f)).ToLowerInvariant();
        }
        static string Want(string file) => Manifest.GetProperty("files").GetProperty(file).GetString();
        static long WantSize(string file) => Manifest.GetProperty("sizes").GetProperty(file).GetInt64();
        static string Local(string rel) => rel.Replace('/', '\\');

        // deep: every file's MD5; otherwise sizes and the install record
        public static State Detect(string game, bool deep = false)
        {
            var st = LoadState(game);
            string mods = Path.Combine(game, ModsDir);
            bool all = PackFiles.All(f =>
            {
                string p = Path.Combine(mods, Local(f));
                return File.Exists(p) && new FileInfo(p).Length == WantSize(f) && (!deep || Md5(p) == Want(f));
            });
            if (all && (st == null || st.Version == Version)) return State.Installed;   // no record: the manual zip
            if (st != null) return State.OlderVersion;
            return State.NotInstalled;
        }

        // other folders in mods\ whose language.ini also says id=ar: the game would keep only the first by name
        static List<string> OtherArabicPacks(string game)
        {
            var found = new List<string>();
            string mods = Path.Combine(game, "mods");
            if (!Directory.Exists(mods)) return found;
            foreach (string d in Directory.EnumerateDirectories(mods))
            {
                if (Path.GetFullPath(d).TrimEnd('\\').Equals(Path.GetFullPath(Path.Combine(game, PackDir)).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                string ini = Path.Combine(d, "language.ini");
                try
                {
                    if (File.Exists(ini) && File.ReadAllLines(ini).Any(l => l.Trim().Replace(" ", "").Equals("id=" + PackId, StringComparison.OrdinalIgnoreCase)))
                        found.Add(Path.GetFileName(d));
                }
                catch { }
            }
            return found;
        }

        // ---- install / uninstall ---------------------------------------------
        static void Extract(ZipArchive z, string entry, string dst, string md5)
        {
            var e = z.GetEntry(entry) ?? throw new Exception("ملف مفقود داخل المثبّت: " + entry);
            string tmp = dst + ".arabic_tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            e.ExtractToFile(tmp, true);
            if (Md5(tmp) != md5) { File.Delete(tmp); throw new Exception("فشل التحقق من الملف: " + entry); }
            File.Move(tmp, dst, true);
        }

        public static Report Install(string game, Action<string> progress, bool force = false)
        {
            EnsureGameClosed();
            if (!force && !SupportsArabic(game))
                throw new Exception("نسخة LostOdysseyRecomp عندك قديمة ولا تعرض العربية بشكل صحيح.\nحدّثها إلى أحدث إصدار ثم شغّل المثبّت مرة أخرى.");
            var rep = new Report();
            var st = LoadState(game);
            bool first = st == null;
            st ??= new InstallState();
            string mods = Path.Combine(game, ModsDir);
            try
            {
                using var z = OpenPayload();
                // files an older version had and this one does not
                foreach (string f in st.Files.Except(PackFiles).ToArray())
                {
                    DeleteFile(Path.Combine(mods, Local(f)));
                    RemoveEmptyUpTo(Path.GetDirectoryName(Path.Combine(mods, Local(f))), mods);
                    st.Files.Remove(f);
                }
                st.Version = Version;
                progress("جارٍ تثبيت ملفات التعريب…");
                int n = 0;
                foreach (string f in PackFiles)
                {
                    string dst = Path.Combine(mods, Local(f));
                    if (!(File.Exists(dst) && new FileInfo(dst).Length == WantSize(f) && Md5(dst) == Want(f)))
                        Extract(z, f, dst, Want(f));
                    if (!st.Files.Contains(f)) st.Files.Add(f);
                    if (++n % 50 == 0) progress($"جارٍ تثبيت ملفات التعريب… {n} من {PackFiles.Length}");
                }
                Directory.CreateDirectory(Path.Combine(game, StateDir));
                foreach (var (entry, name) in Licences)
                    using (var lic = z.GetEntry(entry).Open())
                    using (var o = File.Create(Path.Combine(game, StateDir, name))) lic.CopyTo(o);
                if (AddToModList(game)) st.ModListAdded = true;
                // Arabic selected: the language the game had before is kept for uninstall (first install only)
                if (File.Exists(SettingsPath(game)))
                {
                    if (first)
                    {
                        st.PreviousLanguage = ReadSetting(game, "game_language");
                        st.PreviousPack = ReadSetting(game, "game_language_pack") ?? "";
                    }
                    WriteSettings(game, new Dictionary<string, string> { ["game_language"] = JapaneseId, ["game_language_pack"] = PackId });
                    rep.Selected = true;
                }
            }
            finally { if (st.Files.Count > 0) SaveState(game, st); }
            if (Detect(game, true) != State.Installed) throw new Exception("فشل التحقق النهائي من ملفات التعريب.");
            rep.OtherPacks = OtherArabicPacks(game);
            progress("تم التثبيت بنجاح ✔");
            return rep;
        }

        public static void Uninstall(string game, Action<string> progress)
        {
            EnsureGameClosed();
            progress("جارٍ إزالة ملفات التعريب…");
            var st = LoadState(game);
            string mods = Path.Combine(game, ModsDir);
            var files = (st?.Files ?? new List<string>()).Union(PackFiles).ToList();
            foreach (string f in files) DeleteFile(Path.Combine(mods, Local(f)));
            foreach (string d in files.Select(f => Path.GetDirectoryName(Path.Combine(mods, Local(f)))).Distinct().OrderByDescending(d => d.Length))
                RemoveEmptyUpTo(d, game);
            RemoveEmptyUpTo(Path.Combine(game, PackDir), game);
            RemoveEmptyUpTo(Path.Combine(mods, ImagesId), game);
            if (st?.ModListAdded == true) RemoveFromModList(game);
            // the language goes back to what it was, or to the pack's base without a pack
            if (File.Exists(SettingsPath(game)) && ReadSetting(game, "game_language_pack") == PackId)
                WriteSettings(game, new Dictionary<string, string>
                {
                    ["game_language"] = st?.PreviousLanguage ?? JapaneseId,
                    ["game_language_pack"] = st != null && st.PreviousPack != PackId ? st.PreviousPack ?? "" : "",
                });
            DeleteFile(StatePath(game));
            foreach (var (_, name) in Licences) DeleteFile(Path.Combine(game, StateDir, name));
            RemoveEmptyUpTo(Path.Combine(game, StateDir), game);
            progress("تمت الإزالة ✔");
        }

        static void DeleteFile(string p) { if (File.Exists(p)) File.Delete(p); }

        // mod-list.ini (next to settings.ini): listed mods come before unlisted ones, so without a line of its own
        // ar-images would lose to a listed mod with the same pictures (the HD texture pack). Without the file the
        // game orders by priority, where ar-images (10) already wins. A line the player has is left as it is.
        static bool AddToModList(string game)
        {
            string p = Path.Combine(game, ModList);
            if (!File.Exists(p)) return false;
            var (lines, nl, _) = ReadLines(p);
            if (lines.Any(l => ModListId(l) == ImagesId)) return false;
            int at = lines.FindIndex(l => { string s = l.Trim(); return s.Length > 0 && s[0] != '#' && s[0] != ';'; });
            lines.Insert(at < 0 ? lines.Count : at, ImagesId + "=on");
            File.WriteAllText(p, string.Join(nl, lines) + nl, new UTF8Encoding(false));
            return true;
        }

        static void RemoveFromModList(string game)
        {
            string p = Path.Combine(game, ModList);
            if (!File.Exists(p)) return;
            var (lines, nl, end) = ReadLines(p);
            if (lines.RemoveAll(l => ModListId(l) == ImagesId) > 0)
                File.WriteAllText(p, string.Join(nl, lines) + (end && lines.Count > 0 ? nl : ""), new UTF8Encoding(false));
        }

        static string ModListId(string line) { int eq = line.IndexOf('='); return eq > 0 ? line.Substring(0, eq).Trim() : null; }

        // the lines, the file's own line end, and whether it ends with one
        static (List<string> lines, string nl, bool end) ReadLines(string p)
        {
            string text = File.ReadAllText(p);
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            bool end = text.EndsWith("\n");
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (end) lines.RemoveAt(lines.Count - 1);
            return (lines, nl, end);
        }

        static void RemoveEmptyUpTo(string dir, string root)
        {
            root = Path.GetFullPath(root).TrimEnd('\\');
            for (var d = Path.GetFullPath(dir).TrimEnd('\\'); d.Length > root.Length && d.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                 Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any(); d = Path.GetDirectoryName(d))
            {
                if (d.Equals(Path.Combine(root, "mods"), StringComparison.OrdinalIgnoreCase)) break;   // the game's own mods folder stays
                Directory.Delete(d);
            }
        }

        static void EnsureGameClosed()
        {
            if (GameProcesses.Any(n => Process.GetProcessesByName(n).Length > 0))
                throw new Exception("اللعبة قيد التشغيل. الرجاء إغلاقها تمامًا ثم إعادة المحاولة.");
        }

        public static void OpenUrl(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    }

    // ===================== UI (Enma World: enmaworld.com, Shu palette, Rubik) =====================

    static class Ui
    {
        public static readonly Color Night = Color.FromArgb(0x14, 0x10, 0x13);
        public static readonly Color Card = Color.FromArgb(0x1D, 0x17, 0x1A);
        public static readonly Color Line = Color.FromArgb(0x33, 0x2A, 0x2E);
        public static readonly Color Bone = Color.FromArgb(0xF3, 0xEC, 0xE6);
        public static readonly Color Muted = Color.FromArgb(0xA8, 0x9A, 0x94);
        public static readonly Color Shu = Color.FromArgb(0xD8, 0x28, 0x3F);
        public static readonly Color ShuHover = Color.FromArgb(0xE5, 0x45, 0x5A);

        static PrivateFontCollection _pfc;
        public static FontFamily Family;

        public static void LoadFont()
        {
            try
            {
                _pfc = new PrivateFontCollection();
                foreach (string name in new[] { "Rubik-Regular.ttf", "Rubik-Bold.ttf" })
                {
                    using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                    byte[] data = new byte[s.Length];
                    s.ReadExactly(data, 0, data.Length);
                    IntPtr ptr = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, ptr, data.Length);
                    _pfc.AddMemoryFont(ptr, data.Length);       // GDI+ keeps reading the memory: never freed
                }
                Family = _pfc.Families[0];
            }
            catch { Family = new FontFamily("Segoe UI"); }
        }

        public static Font F(float size, FontStyle style = FontStyle.Regular) => new Font(Family, size, style, GraphicsUnit.Point);

        public static Image Load(string name)
        {
            try
            {
                using Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                using var img = Image.FromStream(s);
                return new Bitmap(img);
            }
            catch { return null; }
        }

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    public class RoundButton : Button
    {
        public Color Base = Ui.Shu;
        public Color Hover = Ui.ShuHover;
        public Color Fg = Ui.Bone;
        public int Radius = 12;
        public Color Outline = Color.Empty;
        bool _hover;

        public RoundButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; Invalidate(); };
        }

        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Ui.Night); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled ? Color.FromArgb(0x2A, 0x22, 0x25) : (_hover ? Hover : Base);
            using (var path = Ui.Round(rect, Radius))
            using (var b = new SolidBrush(fill))
            {
                g.FillPath(b, path);
                if (Outline != Color.Empty) using (var pen = new Pen(_hover && Enabled ? Ui.Shu : Outline, 1f)) g.DrawPath(pen, path);
            }
            var sf = new StringFormat(StringFormatFlags.DirectionRightToLeft) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(Enabled ? Fg : Color.FromArgb(0x6E, 0x62, 0x5E));
            g.DrawString(Text, Font, tb, rect, sf);
        }
    }

    public class MainForm : Form
    {
        string gamePath;
        bool searching, busy;
        readonly ManualResetEventSlim detected = new(false);
        TLabel lblStatus, lblPath, lblFolder, btnBrowse;
        RoundButton btnInstall, btnUninstall;

        public MainForm()
        {
            Ui.LoadFont();
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 700);
            BackColor = Ui.Night;
            RightToLeft = RightToLeft.Yes;
            Font = Ui.F(11f);
            Text = "Lost Odyssey Arabic Installer";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            MouseDown += DragStart;
            int w = ClientSize.Width, inner = 480, left = (w - inner) / 2;

            var close = new TLabel
            {
                Text = "✕", Font = new Font("Segoe UI", 12f, FontStyle.Bold), ForeColor = Ui.Muted,
                Size = new Size(34, 30), Location = new Point(w - 48, 14), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand
            };
            close.Click += (s, e) => Close();
            close.MouseEnter += (s, e) => close.ForeColor = Ui.Shu;
            close.MouseLeave += (s, e) => close.ForeColor = Ui.Muted;
            Controls.Add(close);

            var ver = new TLabel
            {
                Text = "v" + Program.Version, Font = Ui.F(9f), ForeColor = Ui.Muted,
                Size = new Size(140, 30), Location = new Point(14, 14), TextAlign = ContentAlignment.MiddleLeft, RightToLeft = RightToLeft.No
            };
            ver.MouseDown += DragStart; Controls.Add(ver);

            var logo = new PictureBox
            {
                Image = Ui.Load("ui_logo.png"), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent,
                Size = new Size(190, 92), Location = new Point((w - 190) / 2, 34)
            };
            logo.MouseDown += DragStart; Controls.Add(logo);

            var rule = new Rule { Location = new Point((w - 40) / 2, 136), Size = new Size(40, 3) };
            Controls.Add(rule);

            var title = new TLabel
            {
                Text = "Lost Odyssey", Font = Ui.F(22f, FontStyle.Bold), ForeColor = Ui.Bone, RightToLeft = RightToLeft.No,
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(w - 4, 52), Location = new Point(2, 150)
            };
            title.MouseDown += DragStart; Controls.Add(title);

            var subtitle = new TLabel
            {
                Text = "التعريب الكامل", Font = Ui.F(14f, FontStyle.Bold), ForeColor = Ui.Shu,
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(w - 4, 42), Location = new Point(2, 202)
            };
            subtitle.MouseDown += DragStart; Controls.Add(subtitle);

            var tagline = new TLabel
            {
                Text = "نصوص اللعبة كاملة بالعربية مع الصوت الياباني", Font = Ui.F(9f), ForeColor = Ui.Muted,
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(w - 4, 32), Location = new Point(2, 244)
            };
            tagline.MouseDown += DragStart; Controls.Add(tagline);

            var card = new RoundPanel { Size = new Size(inner, 76), Location = new Point(left, 292), Fill = Ui.Card, Border = Ui.Line, Padding = new Padding(12, 6, 12, 6) };
            // two lines: the message (right to left) and the folder (left to right, so a path keeps its order)
            lblFolder = new TLabel
            {
                Dock = DockStyle.Bottom, Height = 30, ForeColor = Ui.Muted, UseMnemonic = false, RightToLeft = RightToLeft.No,
                Font = Ui.F(8.5f), TextAlign = ContentAlignment.MiddleCenter, Fill = Ui.Card, PathEllipsis = true
            };
            lblPath = new TLabel
            {
                Dock = DockStyle.Fill, ForeColor = Ui.Bone, UseMnemonic = false,
                Font = Ui.F(9.5f), TextAlign = ContentAlignment.MiddleCenter, Fill = Ui.Card
            };
            card.Controls.Add(lblPath); card.Controls.Add(lblFolder); Controls.Add(card);

            btnInstall = new RoundButton { Text = "تثبيت اللغة العربية", Font = Ui.F(13f, FontStyle.Bold), Size = new Size(inner, 58), Location = new Point(left, 384) };
            btnInstall.Click += (s, e) => { if (Program.IsValidGameFolder(gamePath)) OnInstall(); else OnBrowse(s, e); };
            Controls.Add(btnInstall);

            btnUninstall = new RoundButton
            {
                Text = "إزالة اللغة العربية", Font = Ui.F(11f, FontStyle.Bold), Size = new Size(inner, 48), Location = new Point(left, 452),
                Base = Ui.Night, Hover = Ui.Card, Fg = Ui.Bone, Outline = Ui.Line
            };
            btnUninstall.Click += (s, e) => OnUninstall(); Controls.Add(btnUninstall);

            lblStatus = new TLabel { Font = Ui.F(9.5f), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted, Size = new Size(w - 4, 36), Location = new Point(2, 508) };
            lblStatus.MouseDown += DragStart; Controls.Add(lblStatus);

            btnBrowse = new TLabel
            {
                Text = "تحديد مجلد اللعبة يدويًا", Font = Ui.F(9f), ForeColor = Ui.Muted, Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(w - 4, 30), Location = new Point(2, 542)
            };
            btnBrowse.Click += OnBrowse; Controls.Add(btnBrowse);
            btnBrowse.MouseEnter += (s, e) => btnBrowse.ForeColor = Ui.Bone;
            btnBrowse.MouseLeave += (s, e) => btnBrowse.ForeColor = Ui.Muted;

            var kofi = new RoundButton
            {
                Text = "أعجبك التعريب؟ ادعمني على Ko-fi", Font = Ui.F(9.5f, FontStyle.Bold), Size = new Size(inner, 44), Location = new Point(left, 588),
                Base = Ui.Card, Hover = Color.FromArgb(0x2A, 0x20, 0x24), Fg = Ui.Bone, Outline = Ui.Line
            };
            kofi.Click += (s, e) => Program.OpenUrl(Program.Kofi);
            Controls.Add(kofi);

            var footer = new TLabel
            {
                Text = "من عالم إنْما  ·  enmaworld.com", Font = Ui.F(9f, FontStyle.Bold), ForeColor = Ui.Muted, Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleCenter, Size = new Size(w - 4, 34), Location = new Point(2, 646)
            };
            footer.Click += (s, e) => Program.OpenUrl(Program.Site);
            footer.MouseEnter += (s, e) => footer.ForeColor = Ui.Bone;
            footer.MouseLeave += (s, e) => footer.ForeColor = Ui.Muted;
            Controls.Add(footer);

            if (Program.NoDetect) { gamePath = Program.ShotGame; detected.Set(); RefreshState(); }
            else
            {
                searching = true;
                RefreshState();
                new Thread(() =>
                {
                    string found = Program.DetectGamePath();
                    if (found != null) Program.SupportsArabic(found);         // read once, off the window's thread
                    void done() { gamePath ??= found; searching = false; RefreshState(); detected.Set(); }
                    if (IsHandleCreated) BeginInvoke(new Action(done)); else { gamePath ??= found; searching = false; detected.Set(); }
                }) { IsBackground = true }.Start();
            }
        }

        // headless screenshot: wait for the search, then draw its result
        public void WaitForDetection()
        {
            while (!detected.IsSet) { Application.DoEvents(); Thread.Sleep(20); }
            RefreshState();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Region = new Region(Ui.Round(new Rectangle(0, 0, Width, Height), (int)(20 * DeviceDpi / 96f)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Ui.Line, 1);
            using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), (int)(20 * DeviceDpi / 96f));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int wp, int lp);
        void DragStart(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }
        }

        void RefreshState()
        {
            if (searching && !Program.IsValidGameFolder(gamePath))
            {
                lblPath.ForeColor = Ui.Muted;
                lblPath.Text = "جارٍ البحث عن اللعبة…";
                lblFolder.Text = "LostOdysseyRecomp"; lblFolder.Visible = true;
                btnInstall.Enabled = false; btnUninstall.Enabled = false;
                SetStatus("لحظات من فضلك", Ui.Muted);
            }
            else if (Program.IsValidGameFolder(gamePath))
            {
                var state = Program.Detect(gamePath);
                lblPath.ForeColor = Ui.Bone;
                lblPath.Text = Program.FoundCount > 1 ? $"وُجدت {Program.FoundCount} نسخ من اللعبة · اخترنا الأحدث" : "تم العثور على اللعبة";
                lblFolder.Text = Program.ShotShow ?? gamePath; lblFolder.Visible = true;
                btnInstall.Enabled = !busy;
                btnUninstall.Enabled = !busy && state != Program.State.NotInstalled;
                btnInstall.Text = state == Program.State.OlderVersion ? "تحديث اللغة العربية" : "تثبيت اللغة العربية";
                if (!Program.SupportsArabic(gamePath)) SetStatus("يبدو أن نسخة اللعبة قديمة · حدّثها قبل التثبيت", Ui.Shu);
                else if (state == Program.State.Installed && Program.ArabicSelected(gamePath)) SetStatus("✔ اللغة العربية مُثبّتة ومفعّلة", Ui.Bone);
                else if (state == Program.State.Installed) SetStatus("العربية مُثبّتة وغير مختارة · اضغط تثبيت", Ui.Bone);
                else if (state == Program.State.OlderVersion) SetStatus("إصدار أقدم من التعريب مُثبّت · اضغط تحديث", Ui.Bone);
                else SetStatus("اللغة العربية غير مُثبّتة", Ui.Muted);
            }
            else
            {
                lblPath.ForeColor = Ui.Shu;
                lblPath.Text = "لم يتم العثور على اللعبة تلقائيًا" + Environment.NewLine + "اضغط الزر بالأسفل واختر مجلد اللعبة";
                lblFolder.Visible = false;
                btnInstall.Enabled = !busy;                                       // the main button picks the folder until one is found
                btnInstall.Text = "تحديد مجلد اللعبة";
                btnUninstall.Enabled = false;
                SetStatus("في انتظار تحديد مجلد اللعبة", Ui.Muted);
            }
            btnInstall.Invalidate();
            btnUninstall.Invalidate();
        }

        void SetStatus(string text, Color color) { lblStatus.Text = text; lblStatus.ForeColor = color; lblStatus.Invalidate(); }

        void Progress(string text)
        {
            if (InvokeRequired) BeginInvoke(new Action(() => SetStatus(text, Ui.Bone)));
            else SetStatus(text, Ui.Bone);
        }

        void SetBusy(bool b) { busy = b; Cursor = b ? Cursors.WaitCursor : Cursors.Default; RefreshState(); }

        void OnBrowse(object sender, EventArgs e)
        {
            if (busy) return;
            using var dlg = new FolderBrowserDialog { Description = "اختر مجلد اللعبة (المجلد الذي فيه LostOdysseyRecomp.exe)", UseDescriptionForTitle = true };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (Program.FindGameFolder(dlg.SelectedPath) is string found) { gamePath = found; searching = false; }
            else MessageBox.Show(this, "هذا المجلد لا يحتوي على LostOdysseyRecomp.exe.", "مجلد غير صالح", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshState();
        }

        void OnInstall()
        {
            if (busy) return;
            bool force = false;
            if (!Program.SupportsArabic(gamePath))
            {
                var answer = MessageBox.Show(this, "يبدو أن نسخة LostOdysseyRecomp عندك قديمة ولا تعرض العربية بشكل صحيح (النص من اليمين وأحلام الألف عام).\n\n" +
                        "• «نعم»: افتح صفحة الإصدارات لتحديث اللعبة أولًا (مُستحسن).\n" +
                        "• «لا»: ثبّت التعريب على أي حال.",
                        "تحديث مطلوب", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Yes) Program.OpenUrl(Program.RecompReleases);
                if (answer != DialogResult.No) return;
                force = true;
            }
            if (MessageBox.Show(this,
                    "سيُضاف التعريب إلى المجلدين mods\\ar و mods\\ar-images داخل مجلد اللعبة، وتُختار العربية لغةً للعبة.\n" +
                    "لن تُعدَّل ملفات اللعبة الأصلية. تأكد من إغلاق اللعبة.\n\nالمتابعة؟",
                    "تثبيت", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            SetBusy(true);
            new Thread(() =>
            {
                try
                {
                    var rep = Program.Install(gamePath, Progress, force);
                    BeginInvoke(new Action(() => { SetBusy(false); AfterInstall(rep); }));
                }
                catch (Exception ex) { BeginInvoke(new Action(() => { SetBusy(false); ShowError(ex, "خطأ في التثبيت"); })); }
            }) { IsBackground = true }.Start();
        }

        void AfterInstall(Program.Report rep)
        {
            string msg = "تم تثبيت اللغة العربية بنجاح!\n\n";
            msg += rep.Selected ? "• اخترنا العربية لغةً للعبة، فشغّلها مباشرة.\n"
                                : "• شغّل اللعبة، ثم اختر «العربية» من Settings ← System ← Game language، واحفظ وأعد التشغيل.\n";
            msg += "• الصوت ياباني، والنصوص كلها بالعربية.";
            if (rep.OtherPacks.Count > 0)
                msg += "\n\nتنبيه: في المجلد mods حزمة عربية أخرى بالمعرّف نفسه (" + string.Join("، ", rep.OtherPacks) + ")، فاحذفها حتى لا تأخذ اللعبة تلك بدلًا من هذه.";
            MessageBox.Show(this, msg, "تم التثبيت", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnUninstall()
        {
            if (busy) return;
            SetBusy(true);
            new Thread(() =>
            {
                try
                {
                    Program.Uninstall(gamePath, Progress);
                    BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        MessageBox.Show(this, "تمت إزالة اللغة العربية.\nتعود اللعبة إلى لغتها السابقة.", "تمت الإزالة", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch (Exception ex) { BeginInvoke(new Action(() => { SetBusy(false); ShowError(ex, "خطأ في الإزالة"); })); }
            }) { IsBackground = true }.Start();
        }

        void ShowError(Exception ex, string title)
        {
            string msg = ex is UnauthorizedAccessException ? "تعذّر الكتابة في مجلد اللعبة. شغّل المثبّت كمسؤول (Run as administrator)." : ex.Message;
            MessageBox.Show(this, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // the short red stroke under the logo (the slash of the ENMA / إنْما mark)
    public class Rule : Control
    {
        public Rule() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.Night);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(Ui.Shu);
            using var p = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), 1);
            e.Graphics.FillPath(b, p);
        }
    }

    public class RoundPanel : Panel
    {
        public Color Fill = Ui.Card;
        public Color Border = Color.Empty;
        public int Radius = 12;
        public RoundPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Ui.Night;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Night);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Radius);
            using var b = new SolidBrush(Fill);
            g.FillPath(b, path);
            if (Border != Color.Empty) using (var pen = new Pen(Border, 1f)) g.DrawPath(pen, path);
        }
    }

    // a label drawn anti-aliased on a solid fill (the window's night, or a card's colour)
    public class TLabel : Label
    {
        public Color Fill = Ui.Night;
        public bool PathEllipsis;                       // one line, the middle of a long path replaced by …
        public TLabel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
            BackColor = Ui.Night;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Fill);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var flags = RightToLeft == RightToLeft.Yes ? StringFormatFlags.DirectionRightToLeft : 0;
            var near = TextAlign == ContentAlignment.MiddleLeft || TextAlign == ContentAlignment.MiddleRight;
            if (PathEllipsis) flags |= StringFormatFlags.NoWrap;
            using var sf = new StringFormat(flags) { Alignment = near ? StringAlignment.Near : StringAlignment.Center, LineAlignment = StringAlignment.Center,
                                                     Trimming = PathEllipsis ? StringTrimming.EllipsisPath : StringTrimming.Character };
            using var b = new SolidBrush(ForeColor);
            g.DrawString(Text, Font, b, new RectangleF(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical), sf);
        }
    }
}
