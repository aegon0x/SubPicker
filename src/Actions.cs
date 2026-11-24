// Dialogs and long-running actions: settings, download, extract from MKV, sync, export
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SubPicker
{
    partial class MainForm
    {
        void Ui(Action a) { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); }

        // ================================================================ settings
        void OpenSettings()
        {
            var d = new Dlg(this, "Settings", 800, 680);
            var seg = new Seg(new[] { ("general", "General"), ("tags", "Source tags") }, "general", Th.Panel) { Dock = DockStyle.Top };
            var segWrap = new Panel { Dock = DockStyle.Top, Height = Th.Px(44), BackColor = Th.Panel };
            seg.Dock = DockStyle.None; seg.Location = new Point(0, 0);
            segWrap.Controls.Add(seg);
            var gen = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, BackColor = Th.Panel };
            gen.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gen.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            gen.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gen.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var tagsPanel = new Panel { Dock = DockStyle.Fill, BackColor = Th.Panel, Visible = false };
            var fields = new Dictionary<string, Field>();
            var rows = new (string label, string key, string kind)[]
            {
                ("TOOLS", null, null),
                ("mkvextract (MKVToolNix)", "mkvextract", "file"), ("batchsync.py", "batchsync", "file"),
                ("alass", "alass", "file"), ("ffsubsync", "ffsubsync", "file"),
                ("7-Zip (7z.exe)", "sevenzip", "file"), ("WinRAR (WinRAR.exe)", "winrar", "file"),
                ("SUBTITLE SITE", null, null),
                ("Site URL", "site_url", null), ("Username", "site_user", null), ("Password", "site_pass", "pw"),
                ("FOLDERS", null, null), ("Default download folder", "download_dir", "dir"),
            };
            int r = 0;
            foreach (var (label, key, kind) in rows)
            {
                if (key == null)
                {
                    var h = Th.Head(label); h.Margin = new Padding(0, r > 0 ? Th.Px(14) : 0, 0, Th.Px(6));
                    gen.Controls.Add(h, 0, r); gen.SetColumnSpan(h, 4); r++;
                    continue;
                }
                string val = kind == "pw" ? Cfg.Unprotect(cfg[key]) : cfg[key];
                var l = Th.Lbl(label, Th.Fg, Th.Base, Th.Panel); l.Margin = new Padding(0, Th.Px(8), Th.Px(12), 0);
                var f = new Field(val, kind == "pw") { Margin = new Padding(0, Th.Px(3), 0, Th.Px(3)) };
                fields[key] = f;
                gen.Controls.Add(l, 0, r); gen.Controls.Add(f, 1, r);
                if (kind == "file" || kind == "dir")
                {
                    var b = Th.Btn("Browse…", (s, e) =>
                    {
                        string p = kind == "dir" ? Ask.Folder(d, label, f.Text) : Ask.File(d, label, "All files|*.*");
                        if (p != null) f.Text = p;
                    });
                    b.Margin = new Padding(Th.Px(6), Th.Px(3), 0, 0);
                    gen.Controls.Add(b, 2, r);
                }
                if (kind == "file")
                {
                    bool ok = Tools.Resolve(val) != "";
                    var mark = Th.Lbl(ok ? "✔" : "✘", ok ? Th.Ok : Th.Err, Th.Bold, Th.Panel); mark.Margin = new Padding(Th.Px(8), Th.Px(8), 0, 0);
                    gen.Controls.Add(mark, 3, r);
                }
                r++;
            }
            var note = Th.Lbl("\nTools found on PATH or installed (MKVToolNix) are filled in automatically; mkvmerge is taken " +
                              "from the same folder as mkvextract, and Python is found on its own. The password is stored " +
                              "encrypted for your Windows account in " + Cfg.Path, Th.Muted, Th.Small, Th.Panel);
            note.MaximumSize = new Size(Th.Px(700), 0);
            gen.Controls.Add(note, 0, r); gen.SetColumnSpan(note, 4);

            var tagTable = cfg.Map("source_tags");
            var tagsDir = BuildTagsSettings(d, tagsPanel, tagTable);
            seg.Changed += () => { gen.Visible = seg.Value == "general"; tagsPanel.Visible = seg.Value == "tags"; };
            d.Body.Controls.Add(tagsPanel);
            d.Body.Controls.Add(gen);
            d.Body.Controls.Add(segWrap);

            d.AddButton("Save", (s, e) =>
            {
                foreach (var kv in fields) cfg[kv.Key] = kv.Key == "site_pass" ? Cfg.Protect(kv.Value.Text.Trim()) : kv.Value.Text.Trim();
                string ext = Tools.Resolve(cfg["mkvextract"]);
                if (ext != "") { string m = Tools.FindMkvTool("mkvmerge", ext); if (m != "") cfg["mkvmerge"] = m; }
                cfg["tags_dir"] = tagsDir.Text.Trim();
                cfg.SetMap("source_tags", tagTable);
                cfg.Save();
                if (dlDir.Text == "") dlDir.Text = cfg["download_dir"];
                d.Close();
                RefreshTracks();
            }, true);
            d.AddButton("Cancel", (s, e) => d.Close());
            d.AddLeftButton("Auto-detect tools", (s, e) =>
            {
                foreach (var kv in Cfg.Defaults())
                    if (fields.TryGetValue(kv.Key, out var f) && kv.Value is string v && v != "" && !kv.Key.StartsWith("site") && kv.Key != "download_dir")
                        f.Text = v;
            });
            d.ShowDialog(this);
            d.Dispose();
        }

        /// Settings > Source tags: provider code -> Source tags XML, used automatically in Track order
        Field BuildTagsSettings(Form d, Panel p, Dictionary<string, string> table)
        {
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = Th.Px(40), ColumnCount = 4, BackColor = Th.Panel };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var l = Th.Lbl("Tags folder", Th.Fg, Th.Base, Th.Panel); l.Margin = new Padding(0, Th.Px(8), Th.Px(12), 0);
            var dirF = new Field(cfg["tags_dir"]) { Margin = new Padding(0, Th.Px(3), 0, 0) };
            var info = Th.Lbl("Files named \"<code> Source Tag.xml\" (e.g. \"iT Source Tag.xml\", \"MEGOGO Source Tag.xml\") are matched " +
                              "to providers automatically. The code is what appears in subtitle names (FLMN, iT, NF…).", Th.Muted, Th.Small, Th.Panel);
            info.Dock = DockStyle.Top; info.AutoSize = false; info.Height = Th.Px(44); info.Padding = new Padding(0, Th.Px(6), 0, 0);

            var grid = new Grid { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false };
            Th.StyleGrid(grid, Th.Panel);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CODE", Width = Th.Px(150), SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "PROVIDER", Width = Th.Px(160), SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SOURCE TAG XML", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
            void Fill()
            {
                grid.Rows.Clear();
                foreach (var kv in table.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    int i = grid.Rows.Add(kv.Key, Core.ProviderLabel(kv.Key), kv.Value);
                    if (!File.Exists(kv.Value)) grid.Rows[i].Cells[2].Style.ForeColor = Th.Err;
                }
            }
            Fill();

            void Scan(bool quiet)
            {
                string dir = dirF.Text.Trim();
                if (!Directory.Exists(dir)) { if (!quiet) Ask.Warn(d, "Choose an existing tags folder first."); return; }
                var (found, unknown) = Core.ScanTagsDir(dir);
                var have = new HashSet<string>(table.Keys.Select(k => k.ToLowerInvariant()));
                int added = 0;
                foreach (var kv in found) if (!have.Contains(kv.Key.ToLowerInvariant())) { table[kv.Key] = kv.Value; added++; }
                Fill();
                if (!quiet)
                    Ask.Info(d, $"{added} new source tag file(s) added." +
                                (unknown.Count > 0 ? "\n\nNot recognised (use Add new to set them):\n" + string.Join(", ", unknown) : ""));
            }
            var browse = Th.Btn("Browse…", (s, e) => { var x = Ask.Folder(d, "Tags folder", dirF.Text); if (x != null) { dirF.Text = x; Scan(true); } });
            browse.Margin = new Padding(Th.Px(6), Th.Px(3), 0, 0);
            var scan = Th.Btn("Scan", (s, e) => Scan(false)); scan.Margin = new Padding(Th.Px(6), Th.Px(3), 0, 0);
            top.Controls.Add(l, 0, 0); top.Controls.Add(dirF, 1, 0); top.Controls.Add(browse, 2, 0); top.Controls.Add(scan, 3, 0);

            void Edit(string code)
            {
                using (var e = new Dlg(d, code == null ? "Add source tag" : "Change source tag", 640, 340))
                {
                    var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, BackColor = Th.Panel };
                    t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    var cl = Th.Lbl("Provider code", Th.Fg, Th.Base, Th.Panel); cl.Margin = new Padding(0, Th.Px(8), Th.Px(12), 0);
                    var cf = new Field(code ?? "") { Margin = new Padding(0, Th.Px(3), 0, Th.Px(3)) };
                    var live = Th.Lbl("", Th.Accent, Th.Base, Th.Panel); live.Margin = new Padding(Th.Px(8), Th.Px(8), 0, 0);
                    cf.Box.TextChanged += (s, ev) => live.Text = cf.Text.Trim() == "" ? "" : "→ " + Core.ProviderLabel(cf.Text);
                    var xl = Th.Lbl("Source tag XML", Th.Fg, Th.Base, Th.Panel); xl.Margin = new Padding(0, Th.Px(8), Th.Px(12), 0);
                    var xf = new Field(code != null && table.TryGetValue(code, out var cur) ? cur : "") { Dock = DockStyle.Fill, Margin = new Padding(0, Th.Px(3), 0, Th.Px(3)) };
                    var xb = Th.Btn("Browse…", (s, ev) =>
                    {
                        var f = Ask.File(e, "Source tag XML", "Matroska tags XML|*.xml|All files|*.*", dirF.Text);
                        if (f == null) return;
                        xf.Text = f;
                        var m = Core.TagFileRe.Match(Path.GetFileName(f));
                        if (m.Success && cf.Text.Trim() == "") cf.Text = m.Groups[1].Value.Trim();
                    });
                    xb.Margin = new Padding(Th.Px(6), Th.Px(3), 0, 0);
                    var hint = Th.Lbl("The code is the provider tag as it appears in subtitle names, e.g. FLMN in Movie.FLMN.WEB.es.srt.", Th.Muted, Th.Small, Th.Panel);
                    hint.MaximumSize = new Size(Th.Px(560), 0); hint.Margin = new Padding(0, Th.Px(10), 0, 0);
                    t.Controls.Add(cl, 0, 0); t.Controls.Add(cf, 1, 0); t.Controls.Add(live, 2, 0);
                    t.Controls.Add(xl, 0, 1); t.Controls.Add(xf, 1, 1); t.Controls.Add(xb, 2, 1);
                    t.Controls.Add(hint, 0, 2); t.SetColumnSpan(hint, 3);
                    e.Body.Controls.Add(t);
                    e.AddButton("OK", (s, ev) =>
                    {
                        string c = cf.Text.Trim(), x = xf.Text.Trim().Trim('"');
                        if (c == "" || x == "") { Ask.Warn(e, "Enter the provider code and choose the XML file."); return; }
                        if (!File.Exists(x)) { Ask.Warn(e, $"File not found:\n{x}"); return; }
                        if (code != null) table.Remove(code);
                        foreach (var k in table.Keys.Where(k => k.ToLowerInvariant() == c.ToLowerInvariant()).ToList()) table.Remove(k);
                        table[c] = x;
                        Fill();
                        e.Close();
                    }, true);
                    e.AddButton("Cancel", (s, ev) => e.Close());
                    e.ShowDialog(d);
                }
            }
            string Current() => grid.CurrentRow != null && grid.CurrentRow.Index >= 0 ? grid.CurrentRow.Cells[0].Value as string : null;
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Edit(Current()); };
            var btns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Th.Px(44), BackColor = Th.Panel, Padding = new Padding(0, Th.Px(8), 0, 0) };
            var add = Th.Btn("+ Add new…", (s, e) => Edit(null), true); add.Margin = new Padding(0, 0, Th.Px(6), 0);
            var chg = Th.Btn("Change…", (s, e) => { if (Current() != null) Edit(Current()); }); chg.Margin = new Padding(0, 0, Th.Px(6), 0);
            var rem = Th.Btn("Remove", (s, e) => { var c = Current(); if (c != null) { table.Remove(c); Fill(); } });
            rem.Margin = new Padding(0);                                  // same margins as the other two buttons
            btns.Controls.AddRange(new Control[] { add, chg, rem });
            var gridCard = new Card { Dock = DockStyle.Fill, MinimumSize = new Size(0, Th.Px(240)) };
            gridCard.Controls.Add(grid);
            p.Controls.Add(gridCard);
            p.Controls.Add(btns);
            p.Controls.Add(info);
            p.Controls.Add(top);
            p.MinimumSize = new Size(0, Th.Px(420));
            return dirF;
        }

        // ================================================================ download
        SiteClient SiteClientFor()
        {
            string url = cfg["site_url"].Trim();
            if (url == "") throw new Exception("Set the site URL in Settings.");
            return new SiteClient(url, cfg["site_user"], Cfg.Unprotect(cfg["site_pass"]));
        }

        void DlSearch()
        {
            string text = dlQuery.Text.Trim();
            if (text == "") return;
            var (query, needle) = SiteClient.BuildQuery(text);
            dlSearchBtn.Enabled = false;
            dlInfo.Text = "Searching…";
            Task.Run(() =>
            {
                try
                {
                    var res = SiteClientFor().Search(query);
                    var hits = res.TryGetValue("hits", out var h) ? h as System.Collections.IEnumerable : null;
                    var groups = SiteClient.GroupHits(hits ?? new object[0], needle);
                    bool trunc = res.TryGetValue("trunc", out var tr) && tr is bool b && b;
                    Ui(() => DlShow(groups, trunc, needle));
                }
                catch (Exception ex) { Ui(() => dlInfo.Text = "Search failed: " + ex.Message); }
                finally { Ui(() => dlSearchBtn.Enabled = true); }
            });
        }

        void DlShow(SortedDictionary<string, SiteClient.Group> groups, bool trunc, string needle)
        {
            dlGroups = groups;
            dlSel = groups.Keys.ToDictionary(k => k, k => true);
            dlClosed.Clear();
            dlOpened.Clear();
            DlRebuildRows();
            int nfiles = groups.Values.Sum(g => g.Files);
            dlInfo.Text = groups.Count == 0 ? $"No folder with {needle} found."
                : $"{groups.Count} folder(s), {nfiles} files" + (trunc ? "  (results truncated)" : "");
            if (groups.Count > 0 && dlDir.Text == "")
                dlDir.Text = cfg["download_last"] != "" ? cfg["download_last"] : cfg["download_dir"] != "" ? cfg["download_dir"]
                           : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        void DlStart()
        {
            var roots = dlSel.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            if (roots.Count == 0) { Ask.Info(this, "Search first and tick at least one folder."); return; }
            string baseDir = dlDir.Text.Trim();
            if (baseDir == "") { Ask.Warn(this, "Choose where to download."); return; }
            if (cfg["download_last"] != baseDir) { cfg["download_last"] = baseDir; cfg.Save(); }    // remembered for next time
            string name = Regex.Replace(roots[0].Substring(roots[0].LastIndexOf('/') + 1), @"[<>:""/\\|?*]", "_").Trim();
            if (name == "") name = "subtitles";
            string target = Path.Combine(baseDir, name);
            dlBtn.Enabled = false;
            dlProg.Value = 0;
            dlLog.Clear();
            bool openAfter = dlOpen.Checked;
            Task.Run(() => DlRun(roots, target, openAfter));
        }

        void DlRun(List<string> roots, string target, bool openAfter)
        {
            void log(string t, string tag = null) => dlLog.Write(t, tag);
            int added = 0;
            learned = new Dictionary<string, string>();
            try
            {
                var site = SiteClientFor();
                Directory.CreateDirectory(target);
                string tmp = Tools.TempDir("subpicker_dl_");
                try
                {
                    for (int n = 1; n <= roots.Count; n++)
                    {
                        string root = roots[n - 1], name = root.Substring(root.LastIndexOf('/') + 1);
                        log($"[{n}/{roots.Count}] Downloading {root}", "head");
                        string zpath = Path.Combine(tmp, $"{n}.zip");
                        string url = "/" + SiteClient.Quote(root) + "/?zip";
                        int nn = n;
                        site.Download(url, zpath, (got, total) =>
                        {
                            double frac = total > 0 ? (double)got / total : 0.5;
                            double pct = ((nn - 1) + frac) / roots.Count * 100;
                            Ui(() => { dlProg.Value = pct; dlInfo.Text = $"Downloading {name}: {got / 1048576.0:0.0} MB"; });
                        });
                        log($"    zip {new FileInfo(zpath).Length / 1024.0:0} KB, extracting…");
                        string xdir = Path.Combine(tmp, $"x{n}");
                        if (!Tools.ExtractArchive(zpath, xdir, cfg, (t, g) => log(t, g))) { log("    could not extract the archive", "err"); continue; }
                        for (int k = 0; k < 3; k++)                                  // nested archives
                        {
                            var nested = Directory.GetFiles(xdir, "*", SearchOption.AllDirectories).Where(f => Core.HasExt(f, Core.ArchiveExts)).ToList();
                            if (nested.Count == 0) break;
                            foreach (var arc in nested)
                            {
                                Tools.ExtractArchive(arc, Path.Combine(Path.GetDirectoryName(arc), Path.GetFileNameWithoutExtension(arc)), cfg, (t, g) => log(t, g));
                                try { File.Delete(arc); } catch { }
                            }
                        }
                        added += Flatten(xdir, target, log);
                    }
                }
                finally { Tools.RmTree(tmp); }
                if (learned.Count > 0)
                {
                    log("Learned provider names: " + string.Join(", ", learned.Select(kv => $"{kv.Key.ToUpperInvariant()} = {kv.Value}")));
                    var copy = new Dictionary<string, string>(learned);
                    Ui(() => { var pn = cfg.Map("provider_names"); foreach (var kv in copy) pn[kv.Key] = kv.Value; cfg.SetMap("provider_names", pn); cfg.Save(); });
                }
                log($"Done: {added} subtitle file(s) in {target}", "ok");
                int a = added;
                Ui(() => { dlInfo.Text = $"Downloaded {a} file(s)"; dlProg.Value = 100; if (a > 0 && openAfter) { LoadFolder(target, downloaded: true); ShowPage("pick"); } });
            }
            catch (Exception ex)
            {
                log("Failed: " + ex.Message, "err");
                Ui(() => dlInfo.Text = "Download failed");
            }
            finally { Ui(() => dlBtn.Enabled = true); }
        }

        /// Move subtitle files into target; provider sub-folder names go into the file name if needed
        int Flatten(string xdir, string target, Action<string, string> log)
        {
            int n = 0;
            foreach (var src in Directory.GetFiles(xdir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (!Core.HasExt(src, Core.SubExts)) continue;
                string f = Path.GetFileName(src), stem = Path.GetFileNameWithoutExtension(f), ext = Path.GetExtension(f);
                var rel = Path.GetDirectoryName(src).Substring(xdir.Length).Trim('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                string fold = rel.Length > 1 ? rel[rel.Length - 1] : "";
                string code = Core.ProviderCode(stem);
                if (fold != "" && code != null && !Core.Providers.ContainsKey(code.ToLowerInvariant()) && !Regex.IsMatch(fold, @"^(S\d+|E\d+)$"))
                {
                    Core.Providers[code.ToLowerInvariant()] = fold;               // learn "BB" -> "Blockbuster"
                    learned[code.ToLowerInvariant()] = fold;
                }
                if (fold != "" && Core.DetectProvider(stem) == "Unknown")
                {
                    var toks = stem.Split('.').ToList();
                    toks.Insert(toks.Count > 1 ? toks.Count - 1 : 1, fold.Replace(" ", "") + ".WEB");
                    stem = string.Join(".", toks);
                }
                string dst = Core.UniquePath(Path.Combine(target, stem + ext));
                File.Move(src, dst);
                n++;
            }
            log($"    {n} subtitle file(s) added", null);
            return n;
        }

        // ================================================================ extract from MKV
        (string merge, string extract) MkvTools()
        {
            string extract = Tools.Resolve(cfg["mkvextract"]);
            if (extract == "") extract = Tools.FindMkvTool("mkvextract");
            if (extract == "")
            {
                Ask.Info(this, "MKVToolNix was not found.\n\nPlease locate mkvextract.exe (usually in C:\\Program Files\\MKVToolNix). You can also set it in Settings.");
                extract = Ask.File(this, "Locate mkvextract.exe", "mkvextract|mkvextract*.exe|All files|*.*") ?? "";
            }
            string merge = Tools.Resolve(cfg["mkvmerge"]);
            if (merge == "" && extract != "") merge = Tools.FindMkvTool("mkvmerge", extract);
            if (extract == "" || merge == "")
            {
                if (extract != "") Ask.Warn(this, "mkvmerge.exe was not found next to mkvextract.exe.");
                return (null, null);
            }
            if (cfg["mkvextract"] != extract || cfg["mkvmerge"] != merge) { cfg["mkvextract"] = extract; cfg["mkvmerge"] = merge; cfg.Save(); }
            return (merge, extract);
        }

        /// Extract subtitle tracks from any MKV - into a folder you choose (default: next to the MKV)
        void ExtractFromMkv(string mkv)
        {
            if (mkv == null) mkv = Ask.File(this, "Video with subtitle tracks", "Matroska|*.mkv;*.mks;*.mka|All files|*.*");
            if (mkv == null) return;
            var (merge, extract) = MkvTools();
            if (merge == null) return;
            Dictionary<string, object> info;
            try
            {
                var p = Tools.Run(merge, new[] { "-J", mkv });
                info = Json.Parse(p.stdout) as Dictionary<string, object>;
                if (info == null) throw new Exception();
            }
            catch { Ask.Error(this, "mkvmerge could not read the file."); return; }
            var extByCodec = new Dictionary<string, string> { { "S_TEXT/UTF8", ".srt" }, { "S_TEXT/ASCII", ".srt" }, { "S_TEXT/ASS", ".ass" },
                                                              { "S_TEXT/SSA", ".ssa" }, { "S_TEXT/WEBVTT", ".vtt" }, { "S_HDMV/PGS", ".sup" } };
            var tracks = ((info.TryGetValue("tracks", out var tv) ? tv : null) as System.Collections.IEnumerable ?? new object[0])
                         .OfType<Dictionary<string, object>>().Where(t => (t.TryGetValue("type", out var ty) ? ty as string : "") == "subtitles").ToList();
            if (tracks.Count == 0) { Ask.Info(this, "This file has no subtitle tracks."); return; }

            var d = new Dlg(this, "Extract subtitle tracks", 860, 680);
            var items = new List<(Dictionary<string, object> t, Dictionary<string, object> pr, string ext, string lang, Func<bool> get, Action<bool> set)>();
            Button extractBtn = null;
            void Count()
            {
                int n = items.Count(i => i.ext != null && i.get());
                if (extractBtn != null) { extractBtn.Text = n > 0 ? $"Extract {n} track" + (n != 1 ? "s" : "") : "Extract"; extractBtn.Enabled = n > 0; }
            }

            // top bar
            var top = new Panel { Dock = DockStyle.Top, Height = Th.Px(40), BackColor = Th.Panel };
            var cnt = Th.Lbl($"{tracks.Count} subtitle track" + (tracks.Count != 1 ? "s" : ""), Th.Fg, Th.Bold, Th.Panel);
            cnt.Location = new Point(0, Th.Px(8));
            var fn = Th.Lbl("  ·  " + Path.GetFileName(mkv), Th.Muted, Th.Base, Th.Panel);
            fn.Location = new Point(cnt.PreferredWidth, Th.Px(8));
            fn.AutoSize = false; fn.AutoEllipsis = true; fn.Width = Th.Px(440); fn.Height = Th.Px(20);
            var quick = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = Th.Panel, WrapContents = false };
            var bNone = Th.Btn("None", (s, e) => { foreach (var i in items) if (i.ext != null) i.set(false); Count(); });
            var bAll = Th.Btn("All", (s, e) => { foreach (var i in items) if (i.ext != null) i.set(true); Count(); });
            bNone.Margin = bAll.Margin = new Padding(Th.Px(6), 0, 0, 0);
            quick.Controls.Add(bNone); quick.Controls.Add(bAll);
            top.Controls.Add(quick); top.Controls.Add(cnt); top.Controls.Add(fn);

            // one compact card per track; click anywhere to tick / untick
            var list = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Th.Panel };
            int cardW = Th.Px(790);
            foreach (var t in tracks)
            {
                var pr = t.TryGetValue("properties", out var pv) ? pv as Dictionary<string, object> ?? new Dictionary<string, object>() : new Dictionary<string, object>();
                string P(string k) => pr.TryGetValue(k, out var v) && v != null ? Convert.ToString(v) : "";
                bool B(string k) => pr.TryGetValue(k, out var v) && v is bool bb && bb;
                extByCodec.TryGetValue(P("codec_id"), out var ext);
                string lang = P("language_ietf") != "" ? P("language_ietf") : P("language") != "" ? P("language") : "und";
                bool on = ext != null;
                var info2 = new List<string> { t.TryGetValue("codec", out var cv) ? Convert.ToString(cv) : P("codec_id") };
                string lines = P("tag_number_of_frames") != "" ? P("tag_number_of_frames") : P("num_index_entries");
                if (long.TryParse(lines, out var ln) && ln > 0) info2.Add(ext == ".sup" ? $"{ln:N0} images" : $"{ln:N0} lines");
                if (long.TryParse(P("tag_number_of_bytes"), out var nb)) info2.Add($"{nb / 1024.0:N0} KB");
                if (ext == null) info2.Add("not supported - cannot be extracted");
                var flags = new List<(string, Color)>();
                if (B("default_track")) flags.Add(("DEFAULT", Th.Ok));
                if (B("forced_track")) flags.Add(("FORCED", Th.Edit));
                if (B("flag_hearing_impaired")) flags.Add(("SDH", Th.Accent));
                string titleTxt = Core.LangDisplay(lang) + (P("track_name") != "" ? "  —  " + P("track_name") : "");
                string id = t.TryGetValue("id", out var idv) ? Convert.ToString(idv) : "?";

                var card = new TrackCard(id, lang, titleTxt, string.Join("  ·  ", info2), flags, ext != null, on) { Width = cardW, Margin = new Padding(0, 0, 0, Th.Px(3)) };
                card.Changed += Count;
                items.Add((t, pr, ext, lang, () => card.On, v => card.On = v));
                list.Controls.Add(card);
            }
            d.Body.Resize += (s, e) => { int w = d.Body.ClientSize.Width - d.Body.Padding.Horizontal - Th.Px(4); foreach (Control c in list.Controls) c.Width = Math.Max(Th.Px(500), w); };
            d.Body.Controls.Add(list);
            d.Body.Controls.Add(top);

            // save to: editable path, in the fixed footer
            string stem = Path.GetFileNameWithoutExtension(mkv);
            string newDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mkv)), stem);
            if (File.Exists(newDir)) newDir += " Subs";
            var foot = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Th.Px(40), ColumnCount = 5, Padding = new Padding(0, 0, 0, Th.Px(6)) };
            foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var st = Th.Head("Save to"); st.Margin = new Padding(Th.Px(2), Th.Px(9), Th.Px(10), 0);
            var dest = new Field(newDir) { Margin = new Padding(0, Th.Px(1), 0, 0) };
            Button FB(string s, Action a) { var b = Th.Btn(s, (o, e) => a()); b.Margin = new Padding(Th.Px(6), 0, 0, 0); return b; }
            foot.Controls.Add(st, 0, 0); foot.Controls.Add(dest, 1, 0);
            foot.Controls.Add(FB("Browse…", () =>
            {
                string cur = dest.Text.Trim();
                var x = Ask.Folder(d, "Save extracted subtitles to", Directory.Exists(cur) ? cur : Path.GetDirectoryName(mkv));
                if (x != null) dest.Text = x;
            }), 2, 0);
            foot.Controls.Add(FB("Next to MKV", () => dest.Text = newDir), 3, 0);
            if (folder != "") foot.Controls.Add(FB("Open folder", () => dest.Text = folder), 4, 0);
            d.Controls.Add(foot);
            foot.SendToBack();                 // docking: buttons at the very bottom, SAVE TO right above them
            d.Footer.SendToBack();

            extractBtn = d.AddButton("Extract", (s, e) =>
            {
                var todo = items.Where(i => i.ext != null && i.get()).ToList();
                string raw = dest.Text.Trim().Trim('"');
                if (raw == "") { Ask.Info(d, "Choose a folder to save to."); return; }
                string outDir = Environment.ExpandEnvironmentVariables(raw);
                if (!Path.IsPathRooted(outDir)) outDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mkv)), outDir);
                outDir = Path.GetFullPath(outDir).TrimEnd('\\');
                bool intoOpen = folder != "" && string.Equals(outDir, folder, StringComparison.OrdinalIgnoreCase);
                d.Close();
                if (todo.Count == 0) return;
                try { Directory.CreateDirectory(outDir); }
                catch (Exception ex) { Ask.Error(this, $"Could not create the folder:\n{outDir}\n\n{ex.Message}"); return; }
                var specs = new List<string>(); var outs = new List<string>();
                foreach (var (t, pr, ext, lang, _, _) in todo)
                {
                    bool B(string k) => pr.TryGetValue(k, out var v) && v is bool bb && bb;
                    string fl = (B("flag_hearing_impaired") ? "[sdh]" : "") + (B("forced_track") ? "[forced]" : "");
                    string id = Convert.ToString(t["id"]);
                    string o = Core.UniquePath(Path.Combine(outDir, $"{stem}_track{id}_[{lang}]{fl}{ext}"));
                    specs.Add($"{id}:{o}");
                    outs.Add(o);
                }
                RunTask("Extracting subtitle tracks", extract, new[] { "--gui-mode", mkv, "tracks" }.Concat(specs).ToList(), ok =>
                {
                    var got = outs.Where(File.Exists).ToList();
                    if (got.Count == 0) return;
                    if (intoOpen) LoadFolder(folder, true, got); else LoadFolder(outDir, false, got);
                    ShowPage("pick");
                });
            }, true);
            d.AddButton("Cancel", (s, e) => d.Close());
            Count();
            d.Show(this);
        }

        /// Run a command in the background with a progress window.
        void RunTask(string title, string exe, List<string> args, Action<bool> onDone)
        {
            var d = new Dlg(this, title, 720, 400);
            var pb = new Bar { Dock = DockStyle.Top };
            var log = new LogBox { Dock = DockStyle.Fill };
            d.Body.Controls.Add(log);
            d.Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Th.Px(10), BackColor = Th.Panel });
            d.Body.Controls.Add(pb);
            d.Body.AutoScroll = false;
            var close = d.AddButton("Close", (s, e) => d.Close());
            close.Enabled = false;
            d.Show(this);
            Task.Run(() =>
            {
                int rc = -1;
                try
                {
                    rc = Tools.RunLines(exe, args, line =>
                    {
                        var m = Regex.Match(line, @"#GUI#progress (\d+)%");
                        if (m.Success) Ui(() => pb.Value = int.Parse(m.Groups[1].Value));
                        else if (line.Trim() != "" && !line.StartsWith("#GUI#")) log.Write(line);
                    });
                }
                catch (Exception ex) { log.Write(ex.Message, "err"); }
                bool ok = rc == 0 || rc == 1;
                log.Write(ok ? "Done." : $"Failed (exit code {rc}).", ok ? "ok" : "err");
                Ui(() => { pb.Value = ok ? 100 : 0; close.Enabled = true; onDone?.Invoke(ok); });
            });
        }

        // ================================================================ sync
        void SyncDialog()
        {
            var allTs = Tracks();
            if (allTs.Count == 0) { Ask.Info(this, "No tracks selected on the Pick subtitles page."); return; }
            var ts = allTs.Where(t => !t.Sub.Pgs).ToList();                   // PGS (image) subs are never synced
            var targets = ts.Where(t => !noSync.Contains(t.PathKey)).ToList();
            int nPgs = allTs.Count - ts.Count;
            if (ts.Count == 0) { Ask.Info(this, "All tracks are PGS (.sup) - those are not synced.\nUse the DELAY column to shift them instead."); return; }
            if (targets.Count == 0) { Ask.Info(this, "No track is ticked in the SYNC column.\nTick the tracks to sync (click the SYNC header to tick all)."); return; }

            var d = new Dlg(this, "Sync subtitles", 840, 640);
            d.Body.AutoScroll = true;
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = Th.Panel };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Th.Px(120)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Label H(string s) { var l = Th.Head(s); l.Margin = new Padding(0, Th.Px(10), 0, 0); return l; }
            grid.Controls.Add(H("Tracks"), 0, 0);
            int nT = targets.Count;
            var tl = Th.Lbl((nT == ts.Count ? $"All {nT} tracks" : $"{nT} of {ts.Count} tracks (SYNC ticked)") + (nPgs > 0 ? $"   ·   {nPgs} PGS excluded" : ""), Th.Fg, Th.Base, Th.Panel);
            tl.Margin = new Padding(0, Th.Px(10), 0, 0);
            grid.Controls.Add(tl, 1, 0);

            grid.Controls.Add(H("Reference"), 0, 1);
            var refMode = new Seg(new[] { ("file", "From PC"), ("track", "A track in this list") }, "file", Th.Panel) { Margin = new Padding(0, Th.Px(6), 0, 0) };
            grid.Controls.Add(refMode, 1, 1);
            var refRow = new TableLayoutPanel { Dock = DockStyle.Fill, Height = Th.Px(36), ColumnCount = 2, BackColor = Th.Panel, Margin = new Padding(0, Th.Px(6), 0, 0) };
            refRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            refRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var refPath = new Field { Margin = new Padding(0, Th.Px(3), 0, 0) };
            void BrowseRef()
            {
                string start = cfg["ref_dir"];
                if (refPath.Text.Trim() != "") start = Path.GetDirectoryName(refPath.Text.Trim());
                var f = Ask.File(d, "Reference (video, audio or a synced subtitle)",
                    "Video / audio / subtitle|*.mkv;*.mp4;*.m2ts;*.ts;*.avi;*.mov;*.webm;*.mka;*.ac3;*.eac3;*.ec3;*.ac4;*.dts;*.dtsma;*.dtshd;*.dtshr;*.dtse;*.cpt;*.thd;*.truehd;*.mlp;*.flac;*.aac;*.adts;*.m4a;*.m4b;*.alac;*.wav;*.w64;*.rf64;*.bwf;*.pcm;*.lpcm;*.aiff;*.aif;*.aifc;*.caf;*.au;*.snd;*.mp3;*.mp2;*.mp1;*.mpa;*.opus;*.ogg;*.oga;*.spx;*.wma;*.ape;*.wv;*.tta;*.tak;*.mpc;*.ofr;*.amr;*.awb;*.3ga;*.ra;*.rm;*.dsf;*.dff;*.voc;*.gsm;*.srt;*.ass;*.ssa;*.vtt|All files|*.*", start);
                if (f != null) refPath.Text = f;
            }
            var rb = Th.Btn("Browse…", (s, e) => BrowseRef()); rb.Margin = new Padding(Th.Px(6), 0, 0, 0);
            refRow.Controls.Add(refPath, 0, 0); refRow.Controls.Add(rb, 1, 0);
            var choices = ts.Select((t, n) => $"#{n + 1}  {t.Lang}  ·  {t.Sub.Provider}  ·  {(t.Name != "" ? t.Name : "no name")}  ·  {t.Sub.Name}").ToList();
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, BackColor = Th.Field, ForeColor = Th.Fg, FlatStyle = FlatStyle.Flat, Visible = false, Margin = new Padding(0, Th.Px(6), 0, 0) };
            cb.Items.AddRange(choices.ToArray());
            int en = Math.Max(0, ts.FindIndex(t => t.Sub.Base == "en"));
            cb.SelectedIndex = en;
            var refHolder = new Panel { Dock = DockStyle.Fill, Height = Th.Px(40), BackColor = Th.Panel, Margin = new Padding(0) };
            refHolder.Controls.Add(refRow); refHolder.Controls.Add(cb);
            refMode.Changed += () => { refRow.Visible = refMode.Value == "file"; cb.Visible = !refRow.Visible; };
            grid.Controls.Add(refHolder, 0, 2); grid.SetColumnSpan(refHolder, 2);

            grid.Controls.Add(H("Run"), 0, 3);
            var method = new Seg(new[] { ("batch", "Batch sync"), ("direct", "Single file") }, cfg["sync_method"] == "direct" ? "direct" : "batch", Th.Panel) { Margin = new Padding(0, Th.Px(8), 0, 0) };
            grid.Controls.Add(method, 1, 3);
            grid.Controls.Add(H("Tool"), 0, 4);
            var tool = new Seg(new[] { ("alass", "alass"), ("ffsubsync", "ffsubsync") }, cfg["sync_tool"] == "ffsubsync" ? "ffsubsync" : "alass", Th.Panel) { Margin = new Padding(0, Th.Px(8), 0, 0) };
            grid.Controls.Add(tool, 1, 4);
            string outDir = Path.Combine(folder, "Synced");
            var note = Th.Lbl($"Synced copies are written to {outDir} and used for export. Originals are not changed. batchsync.py only handles .srt.", Th.Muted, Th.Small, Th.Panel);
            note.MaximumSize = new Size(Th.Px(760), 0); note.Margin = new Padding(0, Th.Px(12), 0, Th.Px(8));
            grid.Controls.Add(note, 0, 5); grid.SetColumnSpan(note, 2);

            var pb = new Bar { Dock = DockStyle.Top };
            var log = new LogBox { Dock = DockStyle.Fill, MinimumSize = new Size(0, Th.Px(160)) };
            d.Body.Controls.Add(log);
            d.Body.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Th.Px(10), BackColor = Th.Panel });
            d.Body.Controls.Add(pb);
            d.Body.Controls.Add(grid);
            d.Body.AutoScrollMinSize = new Size(0, Th.Px(480));

            Button start = null;
            start = d.AddButton("Start sync", (s, e) =>
            {
                string reff;
                if (refMode.Value == "file")
                {
                    reff = refPath.Text.Trim().Trim('"');
                    if (reff == "") { BrowseRef(); reff = refPath.Text.Trim(); if (reff == "") return; }
                }
                else reff = ts[cb.SelectedIndex].Src;
                if (!File.Exists(reff)) { Ask.Error(d, "Reference file not found."); return; }
                if (refMode.Value == "file") cfg["ref_dir"] = Path.GetDirectoryName(Path.GetFullPath(reff));
                cfg["sync_tool"] = tool.Value; cfg["sync_method"] = method.Value;
                cfg.Save();
                var jobs = targets.Where(t => !string.Equals(t.Src, reff, StringComparison.OrdinalIgnoreCase)).ToList();
                start.Enabled = false;
                string tl2 = tool.Value, me = method.Value;
                Task.Run(() => SyncRun(jobs, reff, tl2, me, outDir, log, pb, () => start.Enabled = true));
            }, true);
            d.AddButton("Close", (s, e) => d.Close());
            d.Show(this);
        }

        void SyncRun(List<Track> jobs, string reff, string tool, string method, string outDir, LogBox log, Bar pb, Action finished)
        {
            var results = new Dictionary<string, string>();
            try
            {
                Directory.CreateDirectory(outDir);
                string exe = Tools.Resolve(cfg[tool]);
                log.Write("Reference: " + reff, "head");
                if (method == "batch")
                {
                    string bs = cfg["batchsync"];
                    if (!File.Exists(bs)) throw new Exception("Set the batchsync.py path in Settings.");
                    var srt = jobs.Where(t => t.Src.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var t in jobs.Except(srt)) log.Write("skip (not .srt): " + t.Sub.Name, "err");
                    string tin = Tools.TempDir("sp_in_"), tout = Tools.TempDir("sp_out_");
                    try
                    {
                        var names = new Dictionary<string, Track>();
                        foreach (var t in srt)
                        {
                            string nm = Path.GetFileName(Core.UniquePath(Path.Combine(tin, t.Sub.Name)));
                            File.Copy(t.Src, Path.Combine(tin, nm));
                            names[nm] = t;
                        }
                        log.Write($"Running batchsync.py ({tool}) on {names.Count} file(s)…");
                        Ui(() => pb.Busy = true);
                        var p = Tools.Run(Tools.PythonCmd(), new[] { bs, "--use", tool, reff, tin, tout });
                        foreach (var line in (p.stdout + "\n" + p.stderr).Split('\n'))
                            if (line.Trim() != "" && !Regex.IsMatch(line, @"\d+ / \d+ \[|^\s*$"))
                                log.Write("  " + line.Trim(), line.Contains("rror") ? "err" : null);
                        foreach (var kv in names)
                        {
                            string o = Path.Combine(tout, kv.Key);
                            if (File.Exists(o) && new FileInfo(o).Length > 0)
                            {
                                string dst = Path.Combine(outDir, kv.Value.Sub.Name);
                                if (File.Exists(dst)) File.Delete(dst);
                                File.Move(o, dst);
                                results[kv.Value.PathKey] = dst;
                            }
                        }
                    }
                    finally
                    {
                        Tools.RmTree(tin); Tools.RmTree(tout);
                        Ui(() => { pb.Busy = false; pb.Value = 100; });
                    }
                }
                else
                {
                    if (exe == "") throw new Exception($"{tool} not found - set its path in Settings.");
                    for (int n = 1; n <= jobs.Count; n++)
                    {
                        var t = jobs[n - 1];
                        string dst = Path.Combine(outDir, t.Sub.Name);
                        var args = tool == "alass" ? new[] { reff, t.Src, dst } : new[] { reff, "-i", t.Src, "-o", dst };
                        log.Write($"[{n}/{jobs.Count}] {t.Sub.Name}");
                        var p = Tools.Run(exe, args);
                        if (p.code == 0 && File.Exists(dst) && new FileInfo(dst).Length > 0) { results[t.PathKey] = dst; log.Write("    ok", "ok"); }
                        else
                        {
                            var msg = (p.stderr.Trim() != "" ? p.stderr : p.stdout).Trim().Split('\n');
                            log.Write("    failed: " + (msg.Length > 0 && msg[msg.Length - 1].Trim() != "" ? msg[msg.Length - 1].Trim() : $"exit code {p.code}"), "err");
                        }
                        double pct = n * 100.0 / jobs.Count;
                        Ui(() => pb.Value = pct);
                    }
                }
                log.Write($"Synced {results.Count} of {jobs.Count} track(s).", results.Count > 0 ? "ok" : "err");
            }
            catch (Exception ex) { log.Write("Failed: " + ex.Message, "err"); }
            finally
            {
                Ui(() =>
                {
                    foreach (var kv in results)
                    {
                        if (!overrides.TryGetValue(kv.Key, out var ov)) overrides[kv.Key] = ov = new Dictionary<string, object>();
                        ov["synced"] = kv.Value;
                    }
                    RefreshTracks();
                    finished();
                });
            }
        }

        // ================================================================ export
        void Export()
        {
            var tracks = Tracks();
            if (tracks.Count == 0) { Ask.Info(this, "Nothing selected."); return; }
            string outp = outPath.Text.Trim();
            if (outp == "") { Ask.Warn(this, "Choose where to export first."); return; }
            if (fmtSeg.Value == "srt") ExportSrt(tracks, outp); else ExportMkv(tracks, outp);
        }

        void ExportSrt(List<Track> tracks, string outp)
        {
            if (string.Equals(Path.GetFullPath(outp).TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase))
            { Ask.Warn(this, "The export folder must differ from the source folder."); return; }
            var plan = tracks.Select((t, i) => (t, numChk.Checked ? $"{i + 1:00}.{t.Sub.Name}" : t.Sub.Name)).ToList();
            Directory.CreateDirectory(outp);
            int existing = plan.Count(x => File.Exists(Path.Combine(outp, x.Item2)));
            if (existing > 0 && !Ask.YesNo(this, $"{existing} file(s) already exist in the export folder.\nOverwrite them?")) return;
            int shifted = 0;
            foreach (var (t, name) in plan)
            {
                string dst = Path.Combine(outp, name);
                if (t.Delay != 0 && t.Src.EndsWith(".sup", StringComparison.OrdinalIgnoreCase)) { Core.ShiftPgs(t.Src, dst, t.Delay); shifted++; }
                else if (t.Delay != 0) { Core.ShiftFile(t.Src, dst, t.Delay); shifted++; }
                else File.Copy(t.Src, dst, true);
            }
            RememberExportDir(outp);
            string msg = $"Copied {plan.Count} file(s) to:\n{outp}" + (shifted > 0 ? $"\n\nDelay applied to {shifted} file(s)." : "");
            if (Ask.YesNo(this, msg + "\n\nOpen the folder?")) Tools.OpenPath(outp);
        }

        string GetMkvmerge()
        {
            string exe = Tools.Resolve(cfg["mkvmerge"]);
            if (exe != "") return exe;
            Ask.Info(this, "mkvmerge.exe (MKVToolNix) was not found. Please locate it.");
            var f = Ask.File(this, "Locate mkvmerge.exe", "mkvmerge|mkvmerge*|All files|*.*");
            if (f == null) return null;
            cfg["mkvmerge"] = f;
            cfg.Save();
            return f;
        }

        List<string> MkvArgs(List<Track> tracks, string outp, Dictionary<string, string> tagFiles)
        {
            var a = new List<string> { "--output", outp };
            foreach (var t in tracks)
            {
                if (tagFiles.TryGetValue(t.PathKey, out var tf)) a.AddRange(new[] { "--tags", "0:" + tf });
                a.AddRange(new[] { "--language", "0:" + t.Lang });
                if (t.Name != "") a.AddRange(new[] { "--track-name", "0:" + t.Name });
                a.AddRange(new[] { "--default-track-flag", "0:" + (t.Default ? "yes" : "no"),
                                   "--forced-display-flag", "0:" + (t.Forced ? "yes" : "no"),
                                   "--hearing-impaired-flag", "0:" + (t.Sdh ? "yes" : "no") });
                if (t.Delay != 0) a.AddRange(new[] { "--sync", "0:" + t.Delay });
                if (t.Src.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)) a.AddRange(new[] { "--sub-charset", "0:UTF-8" });
                a.Add(t.Src);
            }
            return a;
        }

        void ExportMkv(List<Track> tracks, string outp)
        {
            string fmt = fmtSeg.Value;
            if (!Core.HasExt(outp, new[] { ".mks", ".mkv" })) { outp += "." + fmt; outPath.Text = outp; }
            var missing = tracks.Select(t => t.Tags).Where(v => v.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !File.Exists(v)).Distinct().ToList();
            if (missing.Count > 0) { Ask.Error(this, "Tags file not found:\n" + string.Join("\n", missing)); return; }
            string exe = GetMkvmerge();
            if (exe == null) return;
            if (File.Exists(outp) && !Ask.YesNo(this, $"{outp}\nalready exists. Overwrite it?")) return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outp)));
            string tmp = Tools.TempDir("subpicker_");
            (int code, string stdout, string stderr) p;
            try
            {
                var tagFiles = new Dictionary<string, string>();
                var made = new Dictionary<string, string>();
                foreach (var t in tracks)
                {
                    string v = t.Tags;
                    if (string.IsNullOrEmpty(v)) continue;
                    if (Core.IsXmlPath(v)) tagFiles[t.PathKey] = v;
                    else                                                      // raw text -> generated Source tags XML
                    {
                        if (!made.ContainsKey(v))
                        {
                            made[v] = Path.Combine(tmp, $"source_{made.Count + 1}.xml");
                            File.WriteAllText(made[v], Core.SourceXml(v), new System.Text.UTF8Encoding(false));
                        }
                        tagFiles[t.PathKey] = made[v];
                    }
                }
                string opt = Path.Combine(tmp, "options.json");
                File.WriteAllText(opt, "[" + string.Join(", ", MkvArgs(tracks, outp, tagFiles).Select(Json.Quote)) + "]", new System.Text.UTF8Encoding(false));
                Cursor = Cursors.WaitCursor;
                p = Tools.Run(exe, new[] { "--output-charset", "UTF-8", "@" + opt });
            }
            catch (Exception ex) { Ask.Error(this, "Could not run mkvmerge:\n" + ex.Message); return; }
            finally { Cursor = Cursors.Default; Tools.RmTree(tmp); }
            var lines = p.stdout.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim() != "" && !l.StartsWith("Progress")).ToList();
            if (p.code == 0 || p.code == 1)
            {
                RememberExportDir(Path.GetDirectoryName(Path.GetFullPath(outp)));
                string msg = $"Created {Path.GetFileName(outp)}\n{tracks.Count} subtitle tracks";
                var warns = lines.Where(l => l.Contains("arning")).Take(6).ToList();
                if (warns.Count > 0) msg += "\n\nWarnings:\n" + string.Join("\n", warns);
                if (Ask.YesNo(this, msg + "\n\nOpen the folder?")) Tools.OpenPath(Path.GetDirectoryName(Path.GetFullPath(outp)));
            }
            else Ask.Error(this, "mkvmerge failed:\n\n" + string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 8))));
        }
    }

    /// One subtitle track in the Extract dialog: tick box, #id, language badge, name, info, flag badges
    class TrackCard : Control
    {
        readonly string id, lang, title, info;
        readonly List<(string, Color)> flags;
        readonly bool supported;
        bool on;
        public event Action Changed;

        public TrackCard(string id, string lang, string title, string info, List<(string, Color)> flags, bool supported, bool on)
        {
            this.id = id; this.lang = lang; this.title = title; this.info = info; this.flags = flags; this.supported = supported; this.on = on;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Th.Px(36);
            Cursor = supported ? Cursors.Hand : Cursors.Default;
        }

        public bool On { get => on && supported; set { if (!supported) return; on = value; Invalidate(); Changed?.Invoke(); } }

        protected override void OnMouseDown(MouseEventArgs e) { if (supported && e.Button == MouseButtons.Left) On = !on; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Th.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool ticked = On;
            using (var path = Th.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Th.Px(4)))
            {
                using (var br = new SolidBrush(Th.Field)) g.FillPath(br, path);
                using (var p = new Pen(ticked ? Th.Accent : Th.Border)) g.DrawPath(p, path);
            }
            var flagsT = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            int x = Th.Px(10);
            var img = Th.Box(ticked ? "on" : "off", Th.Field);
            g.DrawImage(img, x, (Height - img.Height) / 2);
            x += img.Width + Th.Px(10);
            TextRenderer.DrawText(g, "#" + id, Th.Mono, new Rectangle(x, 0, Th.Px(40), Height), Th.Faint, flagsT);
            x += Th.Px(40);
            int pw = TextRenderer.MeasureText(g, lang, Th.SmallBold).Width + Th.Px(10);
            var pr = new RectangleF(x, Th.Px(8), pw, Height - Th.Px(16));
            using (var path = Th.Round(pr, Th.Px(3))) using (var br = new SolidBrush(Th.PillBg)) g.FillPath(br, path);
            TextRenderer.DrawText(g, lang, Th.SmallBold, Rectangle.Round(pr), Th.PillFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            x += pw + Th.Px(12);
            // flag badges on the right
            int rx = Width - Th.Px(10);
            for (int i = flags.Count - 1; i >= 0; i--)
            {
                var (txt, col) = flags[i];
                int bw = TextRenderer.MeasureText(g, txt, Th.SmallBold).Width + Th.Px(8);
                var br2 = new RectangleF(rx - bw, Th.Px(8), bw, Height - Th.Px(16));
                using (var b = new SolidBrush(Th.PillBg)) g.FillRectangle(b, br2);
                TextRenderer.DrawText(g, txt, Th.SmallBold, Rectangle.Round(br2), col, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                rx -= bw + Th.Px(4);
            }
            int tw = TextRenderer.MeasureText(g, title, Th.Bold).Width;
            int avail = rx - x - Th.Px(8);
            TextRenderer.DrawText(g, title, Th.Bold, new Rectangle(x, 0, Math.Min(tw, avail), Height), supported ? Th.Fg : Th.Faint, flagsT);
            x += Math.Min(tw, avail) + Th.Px(14);
            if (rx - x > Th.Px(20))
                TextRenderer.DrawText(g, info, Th.Small, new Rectangle(x, 0, rx - x - Th.Px(8), Height), supported ? Th.Muted : Th.Err, flagsT);
        }
    }
}
