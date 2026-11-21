// Settings, tools, processes, subtitle site, archives
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace SubPicker
{
    // ------------------------------------------------------------------ settings
    class Cfg
    {
        public Dictionary<string, object> D = new Dictionary<string, object>();
        public static readonly string[] ToolKeys = { "mkvmerge", "mkvextract", "batchsync", "alass", "ffsubsync", "sevenzip", "winrar" };
        public static string AppDir => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        public static string Path => System.IO.Path.Combine(AppDir, "settings.json");
        static string LegacyPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SubPicker", "settings.json");

        public string this[string k]
        {
            get => D.TryGetValue(k, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
            set => D[k] = value;
        }

        public Dictionary<string, string> Map(string k)
        {
            var res = new Dictionary<string, string>();
            if (D.TryGetValue(k, out var v) && v is IDictionary dict)
                foreach (DictionaryEntry e in dict) res[(string)e.Key] = Convert.ToString(e.Value);
            return res;
        }

        public void SetMap(string k, Dictionary<string, string> m) =>
            D[k] = m.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

        public static Dictionary<string, object> Defaults()
        {
            string here = AppDir;
            return new Dictionary<string, object>
            {
                {"mkvmerge", Tools.FindMkvTool("mkvmerge")},
                {"mkvextract", Tools.FindMkvTool("mkvextract")},
                {"batchsync", Tools.FirstFile(System.IO.Path.Combine(here, "batchSync.py"), System.IO.Path.Combine(here, "batchsync.py"))},
                {"alass", Tools.Which("alass", "alass-cli")},
                {"ffsubsync", Tools.Which("ffsubsync")},
                {"sevenzip", Tools.Which("7z") is var z && z != "" ? z : Tools.FirstFile(
                    System.IO.Path.Combine(Tools.PF, "7-Zip", "7z.exe"), System.IO.Path.Combine(Tools.PF86, "7-Zip", "7z.exe"))},
                {"winrar", Tools.FirstFile(System.IO.Path.Combine(Tools.PF, "WinRAR", "WinRAR.exe"), System.IO.Path.Combine(Tools.PF86, "WinRAR", "WinRAR.exe"))},
                {"site_url", ""}, {"site_user", ""}, {"site_pass", ""},
                {"download_dir", ""}, {"sync_tool", "alass"}, {"sync_method", "batch"},
                {"ref_dir", ""}, {"tags_dir", ""}, {"source_tags", new Dictionary<string, object>()},
            };
        }

        public static Cfg Load()
        {
            var c = new Cfg { D = Defaults() };
            // only the settings.json next to the exe - a fresh copy always starts empty (safe to share)
            string path = File.Exists(Path) ? Path : null;
            if (path != null)
            {
                try
                {
                    var saved = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                    if (saved != null)
                        foreach (var kv in saved)
                            if (kv.Value != null && (!(kv.Value is string s) || s != "" || kv.Key.StartsWith("site")))
                                c.D[kv.Key] = kv.Value;
                }
                catch { }
            }
            var detected = Defaults();
            foreach (var k in ToolKeys)                          // saved path gone / never set -> detect
                if (Tools.Resolve(c[k]) == "" && (detected[k] as string ?? "") != "") c.D[k] = detected[k];
            c.D.Remove("python");
            return c;
        }

        public void Save()
        {
            try { File.WriteAllText(Path, Json.Write(D), new UTF8Encoding(false)); }
            catch (Exception ex) { System.Windows.Forms.MessageBox.Show("Could not save settings:\n" + ex.Message, "SubPicker"); }
        }

        // ---- DPAPI (current user), same format as the Python version
        public static string Protect(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try { return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser)); }
            catch { return "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)); }
        }

        public static string Unprotect(string v)
        {
            try
            {
                if (v.StartsWith("dpapi:"))
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v.Substring(6)), null, DataProtectionScope.CurrentUser));
                if (v.StartsWith("b64:")) return Encoding.UTF8.GetString(Convert.FromBase64String(v.Substring(4)));
            }
            catch { return ""; }
            return v;
        }
    }

    // ------------------------------------------------------------------ JSON
    static class Json
    {
        public static object Parse(string s) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(s);

        public static string Write(object o, int ind = 0)
        {
            var sb = new StringBuilder();
            W(sb, o, ind);
            return sb.ToString();
        }

        static void W(StringBuilder sb, object o, int ind)
        {
            string pad = new string(' ', ind + 1), end = new string(' ', ind);
            switch (o)
            {
                case null: sb.Append("null"); break;
                case string s: Str(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary d:
                    if (d.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    int i = 0;
                    foreach (DictionaryEntry e in d)
                    {
                        sb.Append(pad); Str(sb, (string)e.Key); sb.Append(": ");
                        W(sb, e.Value, ind + 1);
                        sb.Append(++i < d.Count ? ",\n" : "\n");
                    }
                    sb.Append(end).Append('}');
                    break;
                case IEnumerable list:
                    var items = list.Cast<object>().ToList();
                    if (items.Count == 0) { sb.Append("[]"); break; }
                    sb.Append("[\n");
                    for (int k = 0; k < items.Count; k++)
                    {
                        sb.Append(pad); W(sb, items[k], ind + 1);
                        sb.Append(k < items.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append(end).Append(']');
                    break;
                default: sb.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); break;
            }
        }

        public static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c > 0x7e) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static string Quote(string s) { var sb = new StringBuilder(); Str(sb, s); return sb.ToString(); }
    }

    // ------------------------------------------------------------------ tools / processes
    static class Tools
    {
        public static string PF => Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
        public static string PF86 => Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)";

        public static string FirstFile(params string[] paths) => paths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? "";

        public static string Which(params string[] names)
        {
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Trim() != "").ToList();
            var exts = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';');
            foreach (var n in names)
                foreach (var d in dirs)
                {
                    try
                    {
                        string b = System.IO.Path.Combine(d.Trim().Trim('"'), n);
                        if (System.IO.Path.HasExtension(n) && File.Exists(b)) return b;
                        foreach (var e in exts) if (e != "" && File.Exists(b + e)) return b + e;
                    }
                    catch { }
                }
            return "";
        }

        /// Path or command name -> runnable path ('' if not found)
        public static string Resolve(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            try { if (File.Exists(v)) return v; } catch { }
            return Which(v);
        }

        static List<string> MkvToolNixDirs()
        {
            var dirs = new List<string>();
            foreach (var p in new[] { Which("mkvextract"), Which("mkvmerge") })
                if (p != "") dirs.Add(System.IO.Path.GetDirectoryName(p));
            dirs.Add(System.IO.Path.Combine(PF, "MKVToolNix"));
            dirs.Add(System.IO.Path.Combine(PF86, "MKVToolNix"));
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var sub in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MKVToolNix",
                                            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\MKVToolNix" })
                {
                    try
                    {
                        using (var k = hive.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            string loc = k.GetValue("InstallLocation") as string;
                            if (string.IsNullOrEmpty(loc))
                            {
                                string icon = (k.GetValue("DisplayIcon") as string ?? "").Split(',')[0].Trim('"');
                                if (icon != "") loc = System.IO.Path.GetDirectoryName(icon);
                            }
                            if (!string.IsNullOrEmpty(loc)) dirs.Add(loc);
                        }
                    }
                    catch { }
                }
            return dirs;
        }

        /// mkvmerge / mkvextract: next to `near` (the other tool) first, then the usual install places
        public static string FindMkvTool(string name, string near = "")
        {
            var dirs = new List<string>();
            if (near != "") dirs.Add(System.IO.Path.GetDirectoryName(near));
            dirs.AddRange(MkvToolNixDirs());
            return FirstFile(dirs.Select(d => System.IO.Path.Combine(d, name + ".exe")).ToArray());
        }

        public static string PythonCmd()
        {
            string exe = Which("py", "python");
            return exe != "" ? exe : "py";
        }

        public static string QuoteArg(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2).Append('"');
            return sb.ToString();
        }

        public static ProcessStartInfo Psi(string exe, IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo(exe, string.Join(" ", args.Select(QuoteArg)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            return psi;
        }

        /// Run and wait: (exit code, stdout, stderr)
        public static (int code, string stdout, string stderr) Run(string exe, IEnumerable<string> args)
        {
            using (var p = Process.Start(Psi(exe, args)))
            {
                var err = p.StandardError.ReadToEndAsync();
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, o, err.Result);
            }
        }

        /// Run and stream every output line (stdout + stderr) to onLine.
        public static int RunLines(string exe, IEnumerable<string> args, Action<string> onLine)
        {
            var psi = Psi(exe, args);
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        public static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { System.Windows.Forms.MessageBox.Show(ex.Message, "SubPicker"); }
        }

        public static string TempDir(string prefix)
        {
            string d = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        public static void RmTree(string d) { try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

        public static bool ExtractArchive(string arc, string dest, Cfg cfg, Action<string, string> log)
        {
            Directory.CreateDirectory(dest);
            string sz = Resolve(cfg["sevenzip"]);
            if (sz != "")
            {
                var r = Run(sz, new[] { "x", "-y", "-o" + dest, arc });
                if (r.code == 0) return true;
                string o = r.stdout.Trim();
                log($"7-Zip failed on {System.IO.Path.GetFileName(arc)}: {(o.Length > 200 ? o.Substring(o.Length - 200) : o)}", "err");
            }
            string wr = Resolve(cfg["winrar"]);
            if (wr != "")
            {
                var r = Run(wr, new[] { "x", "-ibck", "-y", "-o+", arc, dest + "\\" });
                if (r.code == 0) return true;
                log($"WinRAR failed on {System.IO.Path.GetFileName(arc)} (code {r.code})", "err");
            }
            if (arc.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var z = ZipFile.OpenRead(arc))
                    foreach (var e in z.Entries)
                    {
                        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(dest, e.FullName));
                        if (!target.StartsWith(System.IO.Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) continue;
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                        e.ExtractToFile(target, true);
                    }
                return true;
            }
            return false;
        }
    }

    // ------------------------------------------------------------------ subtitle site (copyparty)
    class SiteClient
    {
        const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SubPicker/2.0";
        readonly string baseUrl, pw;

        public SiteClient(string url, string user, string password)
        {
            baseUrl = url.TrimEnd('/');
            pw = !string.IsNullOrEmpty(user) ? $"{user}:{password}" : password;
        }

        HttpWebRequest Req(string path, int timeoutSec)
        {
            var r = (HttpWebRequest)WebRequest.Create(baseUrl + path);
            r.UserAgent = UA;
            r.Timeout = r.ReadWriteTimeout = timeoutSec * 1000;
            if (!string.IsNullOrEmpty(pw)) r.Headers["PW"] = pw;
            return r;
        }

        static string Err(WebException e)
        {
            if (!(e.Response is HttpWebResponse resp)) return e.Message;
            string body = "";
            try { using (var sr = new StreamReader(resp.GetResponseStream())) body = sr.ReadToEnd(); } catch { }
            body = Regex.Replace(body, "<[^>]+>", " ").Trim();
            if (body.Length > 200) body = body.Substring(0, 200);
            return $"HTTP {(int)resp.StatusCode}: {(body != "" ? body : resp.StatusDescription)}";
        }

        public Dictionary<string, object> Search(string query, int n = 5000)
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"q\": " + Json.Quote(query) + ", \"n\": " + n + "}");
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    var r = Req("/?srch", 60);
                    r.Method = "POST";
                    r.ContentType = "text/plain";
                    using (var s = r.GetRequestStream()) s.Write(payload, 0, payload.Length);
                    using (var resp = r.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        return Json.Parse(sr.ReadToEnd()) as Dictionary<string, object> ?? new Dictionary<string, object>();
                }
                catch (WebException e) when (e.Response is HttpWebResponse hr)
                {
                    int code = (int)hr.StatusCode;
                    if (code == 429 && attempt < 5) { Thread.Sleep(1200); continue; }      // rate limit
                    if (code == 401 || code == 403) throw new Exception("Login failed - check the username / password in Settings.");
                    throw new Exception(Err(e));
                }
            }
        }

        public void Download(string urlPath, string dst, Action<long, long> progress)
        {
            try
            {
                var r = Req(urlPath, 120);
                using (var resp = r.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var fh = File.Create(dst))
                {
                    long total = resp.ContentLength > 0 ? resp.ContentLength : 0, got = 0;
                    var buf = new byte[256 * 1024];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0)
                    {
                        fh.Write(buf, 0, n);
                        got += n;
                        progress?.Invoke(got, total);
                    }
                }
            }
            catch (WebException e) { throw new Exception(Err(e)); }
        }

        /// '1698863', '[1698863]', 'tt1234567' -> id search; anything else -> path contains text
        public static (string query, string needle) BuildQuery(string text)
        {
            string t = text.Trim();
            var m = Regex.Match(t, @"^\[?\s*(tt\d+|\d+)\s*\]?$", RegexOptions.IgnoreCase);
            if (m.Success) return ($"path like *[{m.Groups[1].Value}]*", $"[{m.Groups[1].Value}]");
            t = t.Replace("\"", "");
            return ($"path like \"*{t}*\"", t);
        }

        public class Group { public int Files; public long Size; public SortedDictionary<string, int> Sub = new SortedDictionary<string, int>(StringComparer.Ordinal); }

        /// Group search hits by the folder whose name contains the needle (the [id] folder)
        public static SortedDictionary<string, Group> GroupHits(IEnumerable hits, string needle)
        {
            var groups = new SortedDictionary<string, Group>(StringComparer.Ordinal);
            string nl = needle.ToLowerInvariant();
            foreach (var hobj in hits)
            {
                if (!(hobj is Dictionary<string, object> h)) continue;
                string rp = Uri.UnescapeDataString(Convert.ToString(h["rp"]).Split('?')[0]);
                var parts = rp.Split('/');
                int idx = parts.Length - 2;
                for (int i = 0; i < parts.Length - 1; i++) if (parts[i].ToLowerInvariant().Contains(nl)) { idx = i; break; }
                if (idx < 0) idx = 0;
                string root = string.Join("/", parts.Take(idx + 1));
                if (!groups.TryGetValue(root, out var g)) groups[root] = g = new Group();
                g.Files++;
                if (h.TryGetValue("sz", out var sz)) g.Size += Convert.ToInt64(sz);
                string subdir = string.Join("/", parts.Skip(idx + 1).Take(Math.Max(0, parts.Length - idx - 2)));
                if (subdir == "") subdir = "·";
                g.Sub[subdir] = (g.Sub.TryGetValue(subdir, out var c) ? c : 0) + 1;
            }
            return groups;
        }

        public static string Quote(string root) => string.Join("/", root.Split('/').Select(Uri.EscapeDataString));
    }
}
