// SubPicker (C# / .NET Framework 4.8) - core logic ported 1:1 from SubPicker.py
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SubPicker
{
    static class Core
    {
        public static readonly string[] SubExts = { ".srt", ".ass", ".ssa", ".vtt", ".sup" };
        public static readonly string[] ArchiveExts = { ".zip", ".rar", ".7z" };
        public static readonly string[] MkvExts = { ".mkv", ".mks", ".mka" };

        public static readonly Dictionary<string, string> Providers = new Dictionary<string, string>
        {
            {"it","iTunes"},{"itunes","iTunes"},{"atvp","Apple TV+"},{"nf","Netflix"},{"amzn","Amazon"},
            {"dsnp","Disney+"},{"hmax","HBO Max"},{"max","Max"},{"pcok","Peacock"},{"hulu","Hulu"},
            {"pmtp","Paramount+"},{"megogo","MEGOGO"},{"viap","Viaplay"},{"mstp","Movistar+"},
            {"cgo","CGO"},{"crav","Crave"},{"stan","Stan"},{"sky","SkyShowtime"},{"skst","SkyShowtime"},
            {"hbo","HBO"},{"roku","Roku"},{"tubi","Tubi"},{"mubi","MUBI"},{"kinopoisk","Kinopoisk"},
            {"ivi","ivi"},{"okko","Okko"},{"zee5","ZEE5"},{"jc","JioCinema"},{"hs","Hotstar"},
            {"mi","Mediaset Infinity"},{"hoop","Hoopla"},{"hoopla","Hoopla"},{"ma","Movies Anywhere"},
        };
        public static readonly string[] DefaultPriority = { "iTunes", "Blu-ray", "MEGOGO", "Netflix", "Amazon",
            "Disney+", "Apple TV+", "Max", "HBO Max", "Viaplay", "CGO", "Movistar+" };
        public static readonly HashSet<string> FlagWords = new HashSet<string> { "sdh", "cc", "hi", "forced", "full", "signs" };
        public static readonly HashSet<string> SdhFlags = new HashSet<string> { "sdh", "cc", "hi" };

        public static readonly Dictionary<string, string> Iso3 = new Dictionary<string, string>
        {
            {"eng","en"},{"fre","fr"},{"fra","fr"},{"ger","de"},{"deu","de"},{"spa","es"},{"ita","it"},
            {"por","pt"},{"rus","ru"},{"jpn","ja"},{"kor","ko"},{"chi","zh"},{"zho","zh"},{"ara","ar"},
            {"heb","he"},{"hin","hi"},{"tha","th"},{"tur","tr"},{"pol","pl"},{"dut","nl"},{"nld","nl"},
            {"swe","sv"},{"nor","no"},{"nob","no"},{"nb","no"},{"dan","da"},{"fin","fi"},{"cze","cs"},
            {"ces","cs"},{"slo","sk"},{"slk","sk"},{"hun","hu"},{"gre","el"},{"ell","el"},{"ukr","uk"},
            {"vie","vi"},{"ind","id"},{"may","ms"},{"msa","ms"},{"rum","ro"},{"ron","ro"},{"bul","bg"},
            {"est","et"},{"lav","lv"},{"lit","lt"},{"slv","sl"},{"ice","is"},{"isl","is"},{"hrv","hr"},
            {"srp","sr"},{"kaz","kk"},{"kir","ky"},{"aze","az"},{"cat","ca"},{"tam","ta"},{"tel","te"},
            {"ben","bn"},{"urd","ur"},{"per","fa"},{"fas","fa"},{"fil","fil"},{"tgl","fil"},{"tl","fil"},
            {"yue","yue"},{"baq","eu"},{"eus","eu"},{"glg","gl"},{"mac","mk"},{"mkd","mk"},{"alb","sq"},
            {"sqi","sq"},{"bos","bs"},{"arm","hy"},{"hye","hy"},{"geo","ka"},{"kat","ka"},{"mal","ml"},
            {"kan","kn"},{"mar","mr"},{"gle","ga"},{"wel","cy"},{"cym","cy"},{"khm","km"},{"lao","lo"},
            {"bur","my"},{"mya","my"},{"sin","si"},{"nep","ne"},{"pan","pa"},{"guj","gu"},{"swa","sw"},
            {"afr","af"},{"zul","zu"},{"uzb","uz"},{"bel","be"},{"mon","mn"},{"tib","bo"},{"bod","bo"},
            {"uig","ug"},
        };

        // English names as MediaInfo shows them (sorting uses these, so Kirghiz comes before Korean)
        public static readonly Dictionary<string, string> LangNames = new Dictionary<string, string>
        {
            {"af","Afrikaans"},{"am","Amharic"},{"ar","Arabic"},{"az","Azerbaijani"},{"be","Belarusian"},
            {"bg","Bulgarian"},{"bn","Bengali"},{"bo","Tibetan"},{"bs","Bosnian"},{"ca","Catalan"},
            {"cs","Czech"},{"cy","Welsh"},{"da","Danish"},{"de","German"},{"el","Greek"},{"en","English"},
            {"es","Spanish"},{"et","Estonian"},{"eu","Basque"},{"fa","Persian"},{"fi","Finnish"},
            {"fil","Filipino"},{"fr","French"},{"ga","Irish"},{"gl","Galician"},{"gu","Gujarati"},
            {"he","Hebrew"},{"hi","Hindi"},{"hr","Croatian"},{"hu","Hungarian"},{"hy","Armenian"},
            {"id","Indonesian"},{"is","Icelandic"},{"it","Italian"},{"ja","Japanese"},{"ka","Georgian"},
            {"kk","Kazakh"},{"km","Khmer"},{"kn","Kannada"},{"ko","Korean"},{"ky","Kirghiz"},{"lo","Lao"},
            {"lt","Lithuanian"},{"lv","Latvian"},{"mk","Macedonian"},{"ml","Malayalam"},{"mn","Mongolian"},
            {"mr","Marathi"},{"ms","Malay"},{"my","Burmese"},{"ne","Nepali"},{"nl","Dutch"},
            {"no","Norwegian"},{"pa","Panjabi"},{"pl","Polish"},{"pt","Portuguese"},{"ro","Romanian"},
            {"ru","Russian"},{"si","Sinhala"},{"sk","Slovak"},{"sl","Slovenian"},{"sq","Albanian"},
            {"sr","Serbian"},{"sv","Swedish"},{"sw","Swahili"},{"ta","Tamil"},{"te","Telugu"},{"th","Thai"},
            {"tr","Turkish"},{"ug","Uighur"},{"uk","Ukrainian"},{"ur","Urdu"},{"uz","Uzbek"},
            {"vi","Vietnamese"},{"yue","Cantonese"},{"zh","Chinese"},{"zu","Zulu"},{"und","Unknown"},
        };

        // Region variants per language, in MKV order: (codes, track title)
        public static readonly Dictionary<string, (string[] codes, string title)[]> Variants =
            new Dictionary<string, (string[], string)[]>
        {
            {"en", new[]{ (new[]{"US",""},""), (new[]{"GB","UK"},"British"), (new[]{"AU"},"Australian"),
                          (new[]{"CA"},"Canadian"), (new[]{"IE"},"Irish"), (new[]{"NZ"},"New Zealand"), (new[]{"IN"},"Indian") }},
            {"es", new[]{ (new[]{"ES"},"Castilian"), (new[]{""},""), (new[]{"419"},"Latin American"), (new[]{"MX"},"Mexican") }},
            {"pt", new[]{ (new[]{"PT"},"Iberian"), (new[]{""},""), (new[]{"BR"},"Brazilian") }},
            {"fr", new[]{ (new[]{"FR"},"European"), (new[]{""},""), (new[]{"CA"},"Canadian"), (new[]{"BE"},"Belgian"), (new[]{"CH"},"Swiss") }},
            {"zh", new[]{ (new[]{"Hans","CN","SG"},"Simplified"), (new[]{""},""), (new[]{"Hant","TW"},"Traditional"), (new[]{"HK"},"Traditional (Hong Kong)") }},
            {"yue", new[]{ (new[]{"Hant","HK"},"Cantonese (Traditional)"), (new[]{""},"Cantonese"), (new[]{"Hans"},"Cantonese (Simplified)") }},
            {"de", new[]{ (new[]{"DE",""},""), (new[]{"AT"},"Austrian"), (new[]{"CH"},"Swiss") }},
            {"nl", new[]{ (new[]{"NL",""},""), (new[]{"BE"},"Flemish") }},
            {"sr", new[]{ (new[]{"","Latn"},"Latin"), (new[]{"Cyrl"},"Cyrillic") }},
        };

        public static readonly Regex TimeRe = new Regex(@"(?:\d+:)?\d{2}:\d{2}[,.]\d{3}\s*-->", RegexOptions.Compiled);
        // Music notes are NOT counted: normal (non-SDH) subs mark song lyrics with them too.
        public static readonly Regex SdhRe = new Regex(@"\[[^\]]{2,}\]|\([^)]{2,}\)|^\s*-?\s*[A-Z][A-Z0-9 .'\-]{1,25}:",
            RegexOptions.Multiline | RegexOptions.Compiled);
        public const double SdhThreshold = 0.05;   // >= 5% of cues with (sounds), [sounds] or NAME: -> SDH
        public const double ForcedRatio = 0.20;    // < 20% of the folder's median cue count -> forced
        public static readonly Regex LangTagRe = new Regex(@"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.Compiled);
        public static readonly HashSet<string> SkipDirs = new HashSet<string> { "selected", "synced" };
        static readonly Regex ProvCodeRe = new Regex(@"[.\-_ ]([A-Za-z0-9+]+)[.\-_ ](?:WEB|WEB-DL|WEBRip)\b", RegexOptions.IgnoreCase);
        static readonly Regex BluRayRe = new Regex(@"blu-?ray|bdrip|bdremux|remux|uhd[.\- ]?bd|\bbd\b", RegexOptions.IgnoreCase);
        public static readonly Regex TagFileRe = new Regex(@"^(.+?)\s+Source Tag\.xml$", RegexOptions.IgnoreCase);

        public static bool HasExt(string path, string[] exts) =>
            exts.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------ providers
        public static string ProviderCode(string stem)
        {
            var m = ProvCodeRe.Match(stem);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string DetectProvider(string stem)
        {
            if (BluRayRe.IsMatch(stem)) return "Blu-ray";
            var m = ProvCodeRe.Match(stem);
            if (m.Success)
            {
                string c = m.Groups[1].Value;
                return Providers.TryGetValue(c.ToLowerInvariant(), out var v) ? v : c.ToUpperInvariant();
            }
            foreach (var tok in Regex.Split(stem, @"[.\-_ \[\]]+"))
                if (tok.Length > 2 && Providers.TryGetValue(tok.ToLowerInvariant(), out var v)) return v;
            return "Unknown";
        }

        public static string ProviderLabel(string code)
        {
            string low = code.Trim().ToLowerInvariant();
            if (Providers.TryGetValue(low, out var v)) return v;
            var hit = Providers.Values.FirstOrDefault(x => x.ToLowerInvariant() == low);
            return hit ?? code.Trim();
        }

        public static bool KnownProvider(string code)
        {
            string low = code.Trim().ToLowerInvariant();
            return Providers.ContainsKey(low) || Providers.Values.Any(x => x.ToLowerInvariant() == low);
        }

        public static HashSet<string> ProviderKeys(Sub s)
        {
            var keys = new HashSet<string> { s.Provider.ToLowerInvariant() };
            string raw = ProviderCode(Path.GetFileNameWithoutExtension(s.Name));
            if (!string.IsNullOrEmpty(raw)) keys.Add(raw.ToLowerInvariant());
            foreach (var kv in Providers) if (kv.Value == s.Provider) keys.Add(kv.Key);
            if (s.Provider == "Blu-ray") { keys.Add("bluray"); keys.Add("blu-ray"); keys.Add("bd"); }
            return keys;
        }

        /// '<code> Source Tag.xml' files -> ({code: path} for known providers, [names not recognised])
        public static (Dictionary<string, string> found, List<string> unknown) ScanTagsDir(string folder)
        {
            var found = new Dictionary<string, string>();
            var unknown = new List<string>();
            string[] names;
            try { names = Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray(); }
            catch { return (found, unknown); }
            foreach (var fn in names)
            {
                var m = TagFileRe.Match(fn);
                if (!m.Success) continue;
                string code = m.Groups[1].Value.Trim();
                if (KnownProvider(code)) found[code] = Path.Combine(folder, fn);
                else unknown.Add(code);
            }
            return (found, unknown);
        }

        // ------------------------------------------------------------ languages
        public static (string tag, HashSet<string> flags) ParseLang(string stem)
        {
            var flags = new HashSet<string>();
            var toks = stem.Split('.').ToList();
            while (toks.Count > 1 && FlagWords.Contains(toks[toks.Count - 1].ToLowerInvariant())
                   && toks[toks.Count - 1].ToLowerInvariant() != "hi")          // bare ".hi" = Hindi
            {
                flags.Add(toks[toks.Count - 1].ToLowerInvariant());
                toks.RemoveAt(toks.Count - 1);
            }
            string last = toks[toks.Count - 1].Split('_').Last();             // ..._track3_[eng] -> [eng]
            foreach (Match m in Regex.Matches(last, @"\[([A-Za-z]+)\]"))
                if (FlagWords.Contains(m.Groups[1].Value.ToLowerInvariant())) flags.Add(m.Groups[1].Value.ToLowerInvariant());
            last = Regex.Replace(last, @"\[(?:" + string.Join("|", FlagWords) + @")\]", "", RegexOptions.IgnoreCase);
            last = last.Trim('[', ']', '(', ')', ' ');
            last = Regex.Replace(last, @"-\d{1,2}$", "");                     // duplicate suffix "-1" (keeps es-419)
            if (!LangTagRe.IsMatch(last)) return ("und", flags);
            var parts = last.Split('-');
            parts[0] = parts[0].ToLowerInvariant();
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i];
                bool alpha = p.All(char.IsLetter);
                if (alpha && p.Length == 2) parts[i] = p.ToUpperInvariant();
                else if (alpha && p.Length == 4) parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant();
            }
            return (string.Join("-", parts), flags);
        }

        public static string BaseOf(string tag)
        {
            string b = tag.Split('-')[0].ToLowerInvariant();
            return Iso3.TryGetValue(b, out var v) ? v : b;
        }

        public static string LangName(string b) => LangNames.TryGetValue(b, out var v) ? v : b;
        public static string Family(string b) => b == "yue" ? "Chinese" : LangName(b);

        public static string LangDisplay(string tag)
        {
            string b = BaseOf(tag);
            string region = tag.Contains("-") ? tag.Substring(tag.IndexOf('-') + 1) : "";
            switch (region.ToLowerInvariant())
            {
                case "hans": region = "Simplified"; break;
                case "hant": region = "Traditional"; break;
                case "419": region = "Latin America"; break;
                case "latn": region = "Latin"; break;
                case "cyrl": region = "Cyrillic"; break;
            }
            return LangName(b) + (region != "" ? $" ({region})" : "");
        }

        /// (rank, track title, unknown-region key) of a sub's region variant
        public static (int rank, string title, string key) RegionInfo(Sub s)
        {
            string reg = s.Region;
            if (Variants.TryGetValue(s.Base, out var table))
            {
                var cands = new List<string> { reg };
                if (reg.Contains("-")) cands.Add(reg.Split('-')[0]);
                foreach (var c in cands)
                    for (int i = 0; i < table.Length; i++)
                        if (table[i].codes.Any(x => x.ToLowerInvariant() == c.ToLowerInvariant()))
                            return (i, table[i].title, "");
                return (table.Length, "", reg.ToLowerInvariant());
            }
            return reg == "" ? (0, "", "") : (1, "", reg.ToLowerInvariant());
        }

        // ------------------------------------------------------------ subtitle content
        static IEnumerable<(int off, int type, int p, int size)> PgsSegments(byte[] data)
        {
            int i = 0, n = data.Length;
            while (i + 13 <= n && data[i] == (byte)'P' && data[i + 1] == (byte)'G')
            {
                int size = (data[i + 11] << 8) | data[i + 12];
                yield return (i, data[i + 10], i + 13, size);
                i += 13 + size;
            }
        }

        /// PGS/.sup: (subtitle events, share of events whose objects all carry the forced flag)
        public static (int events, double forcedShare) PgsInfo(string path)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); } catch { return (0, 0); }
            int events = 0, forced = 0;
            foreach (var (_, typ, p, size) in PgsSegments(data))
            {
                if (typ != 0x16 || size < 11 || p + 10 >= data.Length) continue;          // PCS
                int nobj = data[p + 10];
                if (nobj == 0) continue;                                                 // clear screen
                events++;
                int q = p + 11;
                bool allForced = true;
                for (int k = 0; k < nobj; k++)
                {
                    if (q + 8 > p + size || q + 3 >= data.Length) break;
                    int fl = data[q + 3];
                    allForced &= (fl & 0x40) != 0;
                    q += 8 + ((fl & 0x80) != 0 ? 8 : 0);
                }
                if (allForced) forced++;
            }
            return (events, events > 0 ? (double)forced / events : 0);
        }

        /// Copy a .sup moving every PTS/DTS (90 kHz) by ms milliseconds.
        public static void ShiftPgs(string src, string dst, int ms)
        {
            byte[] data = File.ReadAllBytes(src);
            long d = (long)ms * 90;
            foreach (var (off, _, _, _) in PgsSegments(data).ToList())
                foreach (int k in new[] { off + 2, off + 6 })
                {
                    uint v = (uint)((data[k] << 24) | (data[k + 1] << 16) | (data[k + 2] << 8) | data[k + 3]);
                    if (v != 0 || k == off + 2)                                     // DTS 0 = unused, keep it
                    {
                        uint nv = (uint)Math.Max(0, v + d);
                        data[k] = (byte)(nv >> 24); data[k + 1] = (byte)(nv >> 16);
                        data[k + 2] = (byte)(nv >> 8); data[k + 3] = (byte)nv;
                    }
                }
            File.WriteAllBytes(dst, data);
        }

        public static string ReadText(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            int start = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, false).GetString(raw, start, raw.Length - start);
        }

        /// (cue count, share of cues that look like SDH). PGS: events counted, SDH unknown.
        public static (int cues, double sdh) Analyse(string path)
        {
            if (path.EndsWith(".sup", StringComparison.OrdinalIgnoreCase)) return (PgsInfo(path).events, 0);
            string data;
            try { data = ReadText(path); } catch { return (0, 0); }
            var texts = new List<string>();
            if (HasExt(path, new[] { ".ass", ".ssa" }))
            {
                foreach (var ln in data.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
                    if (ln.StartsWith("Dialogue:"))
                    {
                        var parts = ln.Split(new[] { ',' }, 10);
                        texts.Add(Regex.Replace(parts[parts.Length - 1], @"\{[^}]*\}", "").Replace("\\N", "\n"));
                    }
            }
            else
            {
                data = data.Replace("\r\n", "\n").Replace("\r", "\n");
                foreach (var block in Regex.Split(data, @"\n\s*\n"))
                {
                    var m = TimeRe.Match(block);
                    if (!m.Success) continue;
                    string rest = block.Substring(m.Index + m.Length);
                    int nl = rest.IndexOf('\n');
                    string body = nl >= 0 ? rest.Substring(nl + 1) : "";
                    texts.Add(Regex.Replace(body, @"<[^>]+>", ""));
                }
            }
            int n = texts.Count;
            return (n, n > 0 ? (double)texts.Count(t => SdhRe.IsMatch(t)) / n : 0);
        }

        /// Copy a subtitle file, moving every timestamp by ms milliseconds.
        public static void ShiftFile(string src, string dst, int ms)
        {
            byte[] raw = File.ReadAllBytes(src);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            string text = ReadText(src);
            Func<string, string> fix;
            if (HasExt(src, new[] { ".ass", ".ssa" }))
            {
                var re = new Regex(@"(\d+):(\d{2}):(\d{2})\.(\d{2})");
                fix = line => !line.StartsWith("Dialogue:") ? line : re.Replace(line, m =>
                {
                    long t = Math.Max(0, ((long.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value)) * 60
                                           + int.Parse(m.Groups[3].Value)) * 1000 + int.Parse(m.Groups[4].Value) * 10 + ms);
                    long cs = t / 10, h = cs / 360000, r = cs % 360000, mi = r / 6000; r %= 6000;
                    long s = r / 100, c = r % 100;
                    return $"{h}:{mi:00}:{s:00}.{c:00}";
                }, 2);
            }
            else
            {
                var re = new Regex(@"(?:(\d+):)?(\d{2}):(\d{2})([,.])(\d{3})");
                fix = line => !line.Contains("-->") ? line : re.Replace(line, m =>
                {
                    long h = m.Groups[1].Success ? long.Parse(m.Groups[1].Value) : 0;
                    long t = Math.Max(0, ((h * 60 + int.Parse(m.Groups[2].Value)) * 60 + int.Parse(m.Groups[3].Value)) * 1000
                                         + int.Parse(m.Groups[5].Value) + ms);
                    long hh = t / 3600000, r = t % 3600000, mm = r / 60000; r %= 60000;
                    long ss = r / 1000, ff = r % 1000;
                    return $"{hh:00}:{mm:00}:{ss:00}{m.Groups[4].Value}{ff:000}";
                });
            }
            string outText = string.Join("\n", text.Split('\n').Select(fix));
            using (var fs = File.Create(dst))
            {
                if (bom) fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                byte[] b = new UTF8Encoding(false).GetBytes(outText);
                fs.Write(b, 0, b.Length);
            }
        }

        public static bool IsXmlPath(string v) =>
            !string.IsNullOrEmpty(v) && v.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && (v.Contains("\\") || v.Contains("/") || File.Exists(v));

        static string XmlEsc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        public static string SourceXml(string text) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Tags>\n  <Tag>\n    <Targets />\n    <Simple>\n" +
            "      <Name>Source</Name>\n" +
            $"      <String>{XmlEsc(text)}</String>\n" +
            "    </Simple>\n  </Tag>\n</Tags>\n";

        public static string CommonTitle(IList<string> names, string fallback)
        {
            if (names.Count == 0) return fallback;
            string pre = names[0];
            foreach (var n in names)
            {
                int i = 0;
                while (i < pre.Length && i < n.Length && pre[i] == n[i]) i++;
                pre = pre.Substring(0, i);
            }
            pre = pre.Contains(".") ? pre.Substring(0, pre.LastIndexOf('.')) : "";
            pre = pre.Trim('.', '_', '-', ' ');
            return pre != "" ? pre : fallback;
        }

        /// Subtitle files in folder and its sub-folders (export folders skipped).
        public static List<string> ListSubs(string folder)
        {
            var outp = new List<string>();
            void Walk(string dir)
            {
                string[] files, dirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    dirs = Directory.GetDirectories(dir);
                }
                catch { return; }
                Array.Sort(files, StringComparer.Ordinal);
                outp.AddRange(files.Where(f => HasExt(f, SubExts)));
                Array.Sort(dirs, StringComparer.Ordinal);
                foreach (var d in dirs)
                {
                    string n = Path.GetFileName(d);
                    if (SkipDirs.Contains(n.ToLowerInvariant()) || n.StartsWith(".") || n.StartsWith("_")) continue;
                    Walk(d);
                }
            }
            Walk(folder);
            return outp;
        }

        public static string UniquePath(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string stem = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path));
            string ext = Path.GetExtension(path);
            int k = 2;
            while (File.Exists($"{stem}_{k}{ext}") || Directory.Exists($"{stem}_{k}{ext}")) k++;
            return $"{stem}_{k}{ext}";
        }

        public static double Median(IList<int> xs)
        {
            if (xs.Count == 0) return 1;
            var s = xs.OrderBy(x => x).ToList();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
        }

        /// Python-style tuple comparison (False < True, numbers, ordinal strings).
        public static int CmpKeys(object[] a, object[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                int c;
                if (a[i] is string sa && b[i] is string sb) c = string.CompareOrdinal(sa, sb);
                else if (a[i] is bool ba && b[i] is bool bb) c = ba.CompareTo(bb);
                else c = Convert.ToDouble(a[i]).CompareTo(Convert.ToDouble(b[i]));
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }

        public static string Kb(long size) => $"{size / 1024.0:0} KB";
    }

    class Sub
    {
        public string Path, Name, Provider, Tag, Base, Region;
        public HashSet<string> Flags, Auto = new HashSet<string>();
        public long Size;
        public bool Pgs;
        public int Cues;
        public double SdhRatio;

        public Sub(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            string stem = System.IO.Path.GetFileNameWithoutExtension(Name);
            Provider = Core.DetectProvider(stem);
            (Tag, Flags) = Core.ParseLang(stem);
            string b = Tag.Split('-')[0].ToLowerInvariant();
            Base = Core.Iso3.TryGetValue(b, out var v) ? v : b;
            if (!Tag.Contains("-") && Tag != Base) Tag = Base;                    // eng -> en
            Region = Tag.Contains("-") ? Tag.Substring(Tag.IndexOf('-') + 1) : "";
            Size = new FileInfo(path).Length;
            Pgs = path.EndsWith(".sup", StringComparison.OrdinalIgnoreCase);
            if (Pgs)
            {
                var (ev, share) = Core.PgsInfo(path);
                Cues = ev;
                SdhRatio = 0;
                if (!Flags.Contains("forced") && Cues > 0 && share >= 0.9) Auto.Add("forced");   // PGS forced flag
            }
            else (Cues, SdhRatio) = Core.Analyse(path);
            if (!Flags.Overlaps(Core.SdhFlags) && SdhRatio >= Core.SdhThreshold) Auto.Add("sdh");
        }

        public bool IsSdh => Flags.Overlaps(Core.SdhFlags) || Auto.Overlaps(Core.SdhFlags);
        public bool IsForced => Flags.Contains("forced") || Auto.Contains("forced");

        public string Variant
        {
            get
            {
                var sb = new StringBuilder(Tag + (Pgs ? "  [PGS]" : ""));
                foreach (var f in Flags.OrderBy(x => x, StringComparer.Ordinal)) sb.Append($"  [{f.ToUpperInvariant()}]");
                foreach (var f in Auto.OrderBy(x => x, StringComparer.Ordinal)) sb.Append($"  [{f.ToUpperInvariant()} auto]");
                return sb.ToString();
            }
        }
    }

    /// One row of the Track order page.
    class Track
    {
        public string PathKey;      // original subtitle path (identity)
        public Sub Sub;
        public string Lang, Name, Tags = "", Synced = "", Src;
        public bool Sdh, Forced, Default;
        public int Delay, I;
    }
}
