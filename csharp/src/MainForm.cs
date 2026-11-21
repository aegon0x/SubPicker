// Main window: header, Download / Pick subtitles / Track order pages, export bar
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SubPicker
{
    /// DataGridView that can swallow a mouse press before it changes the selection
    class Grid : DataGridView
    {
        public Func<MouseEventArgs, bool> PreMouseDown;
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (PreMouseDown != null && PreMouseDown(e)) { Focus(); return; }
            base.OnMouseDown(e);
        }
    }

    partial class MainForm : DarkForm
    {
        // ---------------------------------------------------------------- state
        Cfg cfg;
        string folder = "", title = "";
        List<Sub> subs = new List<Sub>();
        Dictionary<string, bool> sel = new Dictionary<string, bool>();
        List<string> provOrder = new List<string>();
        Dictionary<string, bool> provOn = new Dictionary<string, bool>();
        Dictionary<string, Dictionary<string, object>> overrides = new Dictionary<string, Dictionary<string, object>>();
        HashSet<string> noSync = new HashSet<string>();
        List<string> manualOrder;
        string provFilter;
        readonly Dictionary<string, bool> groupOpen = new Dictionary<string, bool>();
        Dictionary<string, string> learned = new Dictionary<string, string>();

        // ---------------------------------------------------------------- widgets
        Seg tabs, fmtSeg;
        Label folderLbl, status, orderInfo;
        Panel page0, page1, page2;
        Field outPath;
        Check numChk;
        Button exportBtn;
        RadioGroup view, sortMode;
        ProviderList provList;
        Grid pickGrid, trackGrid;
        readonly List<PickRow> pickRows = new List<PickRow>();
        List<Track> trackRows = new List<Track>();

        class PickRow { public bool Group; public string Base; public Sub S; public bool HasPrim; public List<string> Provs; public int Shown; }

        public MainForm(string startFolder)
        {
            Text = "SubPicker";
            cfg = Cfg.Load();
            foreach (var kv in cfg.Map("provider_names")) Core.Providers[kv.Key.ToLowerInvariant()] = kv.Value;
            var scr = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Th.Px(1300), scr.Width - 20), Math.Min(Th.Px(820), scr.Height - 20));
            MinimumSize = new Size(Math.Min(Th.Px(900), scr.Width - 20), Math.Min(Th.Px(480), scr.Height - 20));
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildUi();
            KeyPreview = true;
            Shown += (s, e) =>
            {
                EnableDrop(this);
                if (string.IsNullOrEmpty(startFolder)) return;
                if (Directory.Exists(startFolder)) { LoadFolder(startFolder); ShowPage("pick"); }
                else if (File.Exists(startFolder) && Core.HasExt(startFolder, Core.MkvExts)) ExtractFromMkv(startFolder);
                else if (File.Exists(startFolder) && Core.HasExt(startFolder, Core.SubExts))
                { LoadFolder(Path.GetDirectoryName(startFolder), false, new[] { startFolder }); ShowPage("pick"); }
            };
        }

        // ================================================================ layout
        void BuildUi()
        {
            SuspendLayout();
            // header
            var hdr = new Panel { Dock = DockStyle.Top, Height = Th.Px(58), Padding = new Padding(Th.Px(16), Th.Px(12), Th.Px(16), Th.Px(8)) };
            var title = Th.Lbl("SubPicker", Th.Fg, Th.Title);
            title.Location = new Point(Th.Px(16), Th.Px(12));
            tabs = new Seg(new[] { ("download", "Download"), ("pick", "Pick subtitles"), ("order", "Track order") }, "download", Th.Bg);
            tabs.Location = new Point(Th.Px(150), Th.Px(14));
            tabs.Changed += () => ShowPage(tabs.Value);
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Padding = new Padding(0, Th.Px(2), 0, 0) };
            right.Controls.Add(Th.Btn("⚙  Settings", (s, e) => OpenSettings()));
            right.Controls.Add(Th.Btn("Open folder…", (s, e) => PickFolder()));
            right.Controls.Add(Th.Btn("From MKV…", (s, e) => ExtractFromMkv(null)));
            folderLbl = Th.Lbl("", Th.Muted);
            folderLbl.Margin = new Padding(Th.Px(12), Th.Px(8), Th.Px(12), 0);
            right.Controls.Add(folderLbl);
            foreach (Control c in right.Controls) if (c is Button) c.Margin = new Padding(Th.Px(6), 0, 0, 0);
            hdr.Controls.Add(right);
            hdr.Controls.Add(tabs);
            hdr.Controls.Add(title);

            status = new Label { Dock = DockStyle.Bottom, Height = Th.Px(28), ForeColor = Th.Muted, Padding = new Padding(Th.Px(18), Th.Px(4), Th.Px(18), Th.Px(6)), UseMnemonic = false, AutoEllipsis = true };
            var export = BuildExport();

            // content: scrolls as a whole only if the window gets very small
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(Th.Px(880), Th.Px(380)), Padding = new Padding(Th.Px(14), 0, Th.Px(14), 0) };
            Th.DarkScroll(host);
            page0 = new Panel { Dock = DockStyle.Fill };
            page1 = new Panel { Dock = DockStyle.Fill };
            page2 = new Panel { Dock = DockStyle.Fill };
            BuildDownload(page0);
            BuildPick(page1);
            BuildOrder(page2);
            host.Controls.AddRange(new Control[] { page0, page1, page2 });

            Controls.Add(host);
            Controls.Add(export);
            Controls.Add(status);
            Controls.Add(hdr);
            ResumeLayout();
            ShowPage("download");
            OnFmt();
        }

        void ShowPage(string p)
        {
            if (tabs.Value != p) { tabs.Value = p; return; }
            page0.Visible = p == "download";
            page1.Visible = p == "pick";
            page2.Visible = p == "order";
        }

        Control BuildExport()
        {
            var wrap = new Panel { Dock = DockStyle.Bottom, Height = Th.Px(70), Padding = new Padding(Th.Px(14), Th.Px(10), Th.Px(14), Th.Px(6)) };
            var card = new Card { Dock = DockStyle.Fill };
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Th.Panel, ColumnCount = 6, RowCount = 1, Padding = new Padding(Th.Px(12), Th.Px(10), Th.Px(12), Th.Px(8)) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var head = Th.Head("Export"); head.Margin = new Padding(0, Th.Px(8), Th.Px(12), 0);
            fmtSeg = new Seg(new[] { ("mks", "MKS"), ("mkv", "MKV"), ("srt", "SRT files") }, "mks", Th.Panel);
            fmtSeg.Changed += OnFmt;
            outPath = new Field { Margin = new Padding(Th.Px(12), Th.Px(1), Th.Px(6), 0) };
            var browse = Th.Btn("Browse…", (s, e) => PickOut()); browse.Margin = new Padding(0);
            numChk = new Check("Number in track order", false, Th.Panel) { Margin = new Padding(Th.Px(12), Th.Px(3), 0, 0) };
            exportBtn = Th.Btn("Export", (s, e) => Export(), true); exportBtn.Margin = new Padding(Th.Px(12), 0, 0, 0);
            bar.Controls.Add(head, 0, 0); bar.Controls.Add(fmtSeg, 1, 0); bar.Controls.Add(outPath, 2, 0);
            bar.Controls.Add(browse, 3, 0); bar.Controls.Add(numChk, 4, 0); bar.Controls.Add(exportBtn, 5, 0);
            card.Controls.Add(bar);
            wrap.Controls.Add(card);
            return wrap;
        }

        // ---------------------------------------------------------------- page 0: download
        Field dlQuery, dlDir;
        Label dlInfo;
        Button dlSearchBtn, dlBtn;
        Check dlOpen;
        Bar dlProg;
        LogBox dlLog;
        Grid dlGrid;
        SortedDictionary<string, SiteClient.Group> dlGroups = new SortedDictionary<string, SiteClient.Group>();
        Dictionary<string, bool> dlSel = new Dictionary<string, bool>();
        readonly List<(bool group, string root, string text, string files, string size, string where)> dlRows =
            new List<(bool, string, string, string, string, string)>();

        void BuildDownload(Panel t)
        {
            var top = new Card { Dock = DockStyle.Top, Height = Th.Px(86) };
            var tp = new Panel { Dock = DockStyle.Fill, BackColor = Th.Panel, Padding = new Padding(Th.Px(14), Th.Px(12), Th.Px(14), Th.Px(10)) };
            var h = Th.Head("Search subtitle site"); h.Location = new Point(Th.Px(14), Th.Px(12));
            dlQuery = new Field { Location = new Point(Th.Px(14), Th.Px(38)), Width = Th.Px(360), Anchor = AnchorStyles.Top | AnchorStyles.Left };
            dlQuery.Box.Font = Th.Bold;
            dlQuery.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DlSearch(); } };
            dlSearchBtn = Th.Btn("Search", (s, e) => DlSearch(), true);
            dlSearchBtn.Location = new Point(Th.Px(382), Th.Px(38));
            dlInfo = Th.Lbl("Enter a TMDB / IMDb id (e.g. 1698863 or tt1234567) or a title.", Th.Muted, Th.Base, Th.Panel);
            dlInfo.Location = new Point(Th.Px(480), Th.Px(44));
            tp.Controls.AddRange(new Control[] { h, dlQuery, dlSearchBtn, dlInfo });
            top.Controls.Add(tp);

            var mid = new Card { Dock = DockStyle.Fill };
            dlGrid = new Grid { Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, MultiSelect = false };
            Th.StyleGrid(dlGrid, Th.Panel);
            foreach (var (name, w, fill) in new[] { ("FOLDER ON THE SITE", 420, false), ("FILES", 80, false), ("SIZE", 90, false), ("LIBRARY / PATH", 300, true) })
                dlGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = name, Width = Th.Px(w), SortMode = DataGridViewColumnSortMode.NotSortable,
                    AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                });
            dlGrid.CellPainting += DlPaint;
            dlGrid.CellMouseDown += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= dlRows.Count || !dlRows[e.RowIndex].group) return;
                string root = dlRows[e.RowIndex].root;
                dlSel[root] = !dlSel[root];
                dlGrid.InvalidateRow(e.RowIndex);
            };
            mid.Controls.Add(dlGrid);

            var bottom = new Card { Dock = DockStyle.Bottom, Height = Th.Px(190) };
            var bp = new Panel { Dock = DockStyle.Fill, BackColor = Th.Panel };
            var bar = new TableLayoutPanel { Dock = DockStyle.Top, Height = Th.Px(52), ColumnCount = 5, BackColor = Th.Panel, Padding = new Padding(Th.Px(14), Th.Px(10), Th.Px(14), 0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var dh = Th.Head("Download to"); dh.Margin = new Padding(0, Th.Px(8), Th.Px(10), 0);
            dlDir = new Field(cfg["download_dir"]) { Margin = new Padding(0, Th.Px(1), Th.Px(6), 0) };
            var br = Th.Btn("Browse…", (s, e) => { var d = Ask.Folder(this, "Download to", dlDir.Text); if (d != null) dlDir.Text = d; });
            br.Margin = new Padding(0);
            dlOpen = new Check("Open in Pick subtitles when done", true, Th.Panel) { Margin = new Padding(Th.Px(12), Th.Px(3), Th.Px(12), 0) };
            dlBtn = Th.Btn("Download & extract", (s, e) => DlStart(), true); dlBtn.Margin = new Padding(0);
            bar.Controls.Add(dh, 0, 0); bar.Controls.Add(dlDir, 1, 0); bar.Controls.Add(br, 2, 0); bar.Controls.Add(dlOpen, 3, 0); bar.Controls.Add(dlBtn, 4, 0);
            var progWrap = new Panel { Dock = DockStyle.Top, Height = Th.Px(20), Padding = new Padding(Th.Px(14), Th.Px(6), Th.Px(14), Th.Px(6)), BackColor = Th.Panel };
            dlProg = new Bar { Dock = DockStyle.Fill };
            progWrap.Controls.Add(dlProg);
            dlLog = new LogBox { Dock = DockStyle.Fill };
            bp.Controls.Add(dlLog);
            bp.Controls.Add(progWrap);
            bp.Controls.Add(bar);
            bottom.Controls.Add(bp);

            t.Controls.Add(mid);
            t.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = Th.Px(10) });
            t.Controls.Add(bottom);
            t.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Th.Px(10) });
            t.Controls.Add(top);
            // dock order: last added docks first
            t.Controls.SetChildIndex(top, t.Controls.Count - 1);
        }

        void DlPaint(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dlRows.Count) return;
            var r = dlRows[e.RowIndex];
            var g = e.Graphics;
            bool selected = dlGrid.Rows[e.RowIndex].Selected;
            using (var bb = new SolidBrush(selected ? Th.Sel : Th.Panel)) g.FillRectangle(bb, e.CellBounds);
            var cb = e.CellBounds;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            if (e.ColumnIndex == 0)
            {
                int x = cb.X + Th.Px(8);
                if (r.group)
                {
                    var img = Th.Box(dlSel.TryGetValue(r.root, out var on) && on ? "on" : "off", selected ? Th.Sel : Th.Panel);
                    g.DrawImage(img, x, cb.Y + (cb.Height - img.Height) / 2);
                    x += img.Width + Th.Px(8);
                }
                else x += Th.Px(26);
                TextRenderer.DrawText(g, r.text, r.group ? Th.Bold : Th.Base, new Rectangle(x, cb.Y, cb.Right - x, cb.Height), r.group ? Th.Fg : Th.Muted, flags);
            }
            else
            {
                string v = e.ColumnIndex == 1 ? r.files : e.ColumnIndex == 2 ? r.size : r.where;
                var f = flags | (e.ColumnIndex < 3 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
                TextRenderer.DrawText(g, v, Th.Base, new Rectangle(cb.X + Th.Px(6), cb.Y, cb.Width - Th.Px(10), cb.Height), r.group ? Th.Fg : Th.Muted, f);
            }
            e.Handled = true;
        }

        // ---------------------------------------------------------------- page 1: pick
        void BuildPick(Panel t)
        {
            var side = new Card { Dock = DockStyle.Left, Width = Th.Px(280) };
            var scroll = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Th.Panel, Padding = new Padding(Th.Px(14)) };
            Th.DarkScroll(scroll);
            int w = Th.Px(240);
            Label H(string s, int top = 12) { var l = Th.Head(s); l.Margin = new Padding(0, Th.Px(top), 0, Th.Px(4)); return l; }
            scroll.Controls.Add(H("Providers · priority order", 0));
            var hint = Th.Lbl("Drag to reorder · double-click = only its files", Th.Muted, Th.Small, Th.Panel);
            hint.MaximumSize = new Size(w, 0);
            scroll.Controls.Add(hint);
            provList = new ProviderList(this) { Width = w, Margin = new Padding(0, Th.Px(6), 0, 0) };
            scroll.Controls.Add(provList);

            scroll.Controls.Add(H("Show"));
            view = new RadioGroup("all");
            foreach (var (k, txt) in new[] { ("all", "All languages"), ("missing", "Missing from primary"), ("primary", "Only in primary") })
                scroll.Controls.Add(view.Add(k, txt, Th.Panel));
            view.Changed += RebuildPick;
            scroll.Controls.Add(H("List order"));
            sortMode = new RadioGroup("missing");
            foreach (var (k, txt) in new[] { ("missing", "Missing first"), ("mkv", "MKV order (English, then A–Z)") })
                scroll.Controls.Add(sortMode.Add(k, txt, Th.Panel));
            sortMode.Changed += RebuildPick;

            Button B(string s, Action a) { var b = Th.Btn(s, (o, e) => a()); b.AutoSize = false; b.Width = w; b.Height = Th.Px(30); b.Margin = new Padding(0, Th.Px(2), 0, Th.Px(2)); return b; }
            scroll.Controls.Add(H("Selection"));
            scroll.Controls.Add(B("Auto-select by priority", AutoSelect));
            scroll.Controls.Add(B("Select none", SelectNone));
            var row = new TableLayoutPanel { Width = w, Height = Th.Px(34), ColumnCount = 2, Margin = new Padding(0), BackColor = Th.Panel };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var ex = Th.Btn("Expand", (s, e) => Expand(true)); ex.AutoSize = false; ex.Dock = DockStyle.Fill; ex.Margin = new Padding(0, Th.Px(2), Th.Px(2), Th.Px(2));
            var co = Th.Btn("Collapse", (s, e) => Expand(false)); co.AutoSize = false; co.Dock = DockStyle.Fill; co.Margin = new Padding(Th.Px(2), Th.Px(2), 0, Th.Px(2));
            row.Controls.Add(ex, 0, 0); row.Controls.Add(co, 1, 0);
            scroll.Controls.Add(row);
            scroll.Controls.Add(H("Add subtitles"));
            scroll.Controls.Add(B("Add subtitle files…", () => AddFiles(null)));
            scroll.Controls.Add(B("From MKV…", () => ExtractFromMkv(null)));
            var help = Th.Lbl("or drag & drop subtitle files, folders or an MKV onto the window.\n\n" +
                              "Click a file to tick it · click the box on a language row to tick all.\n" +
                              "Double-click a file to open it. Orange = missing from the primary.\n" +
                              "\"auto\" = detected from the subtitle text.", Th.Muted, Th.Small, Th.Panel);
            help.MaximumSize = new Size(w, 0);
            help.Margin = new Padding(0, Th.Px(6), 0, 0);
            scroll.Controls.Add(help);
            side.Controls.Add(scroll);

            var main = new Card { Dock = DockStyle.Fill };
            pickGrid = new Grid { Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, MultiSelect = true };
            Th.StyleGrid(pickGrid, Th.Panel);
            foreach (var (name, wd, fill) in new[] { ("LANGUAGE", 250, false), ("PROVIDER", 150, false), ("VARIANT", 180, false),
                                                     ("CUES", 70, false), ("SIZE", 80, false), ("FILE / AVAILABLE FROM", 300, true) })
                pickGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = name, Width = Th.Px(wd), SortMode = DataGridViewColumnSortMode.NotSortable,
                    AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                });
            pickGrid.CellPainting += PickPaint;
            pickGrid.CellMouseDown += PickMouseDown;
            pickGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= pickRows.Count || pickRows[e.RowIndex].Group) return;
                var sub = pickRows[e.RowIndex].S;
                sel[sub.Path] = !sel[sub.Path];               // undo the toggle from the second click
                RefreshMarks();
                Tools.OpenPath(sub.Path);
            };
            pickGrid.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Space) return;
                foreach (DataGridViewRow r in pickGrid.SelectedRows) TogglePickRow(r.Index);
                RefreshMarks();
                e.Handled = e.SuppressKeyPress = true;
            };
            main.Controls.Add(pickGrid);

            t.Controls.Add(main);
            t.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Th.Px(10) });
            t.Controls.Add(side);
        }

        void TogglePickRow(int i)
        {
            if (i < 0 || i >= pickRows.Count) return;
            var r = pickRows[i];
            if (!r.Group) { sel[r.S.Path] = !sel[r.S.Path]; return; }
            var kids = KidsOf(i);
            bool target = !kids.All(k => sel[k.Path]);
            foreach (var k in kids) sel[k.Path] = target;
        }

        List<Sub> KidsOf(int gi)
        {
            var g = pickRows[gi];
            var shown = Groups().TryGetValue(g.Base, out var items) ? items : new List<Sub>();
            return provFilter != null ? shown.Where(s => s.Provider == provFilter).ToList() : shown;
        }

        void PickMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= pickRows.Count || e.Button != MouseButtons.Left) return;
            var r = pickRows[e.RowIndex];
            if (!r.Group) { TogglePickRow(e.RowIndex); RefreshMarks(); return; }
            if (e.ColumnIndex != 0) return;
            int arrowEnd = Th.Px(24), boxEnd = arrowEnd + Th.Px(22);
            if (e.X < arrowEnd)
            {
                groupOpen[r.Base] = !IsOpen(r);
                RebuildPick();
            }
            else if (e.X < boxEnd) { TogglePickRow(e.RowIndex); RefreshMarks(); }
        }

        bool IsOpen(PickRow r) => provFilter != null || (groupOpen.TryGetValue(r.Base, out var o) ? o : !r.HasPrim);

        void PickPaint(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= pickRows.Count) return;
            var r = pickRows[e.RowIndex];
            var g = e.Graphics;
            var cb = e.CellBounds;
            bool selected = pickGrid.Rows[e.RowIndex].Selected;
            Color bg = selected ? Th.Sel : r.Group ? (r.HasPrim ? Th.HaveBg : Th.MissBg) : Th.Panel;
            using (var bb = new SolidBrush(bg)) g.FillRectangle(bb, cb);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            var tr = new Rectangle(cb.X + Th.Px(8), cb.Y, cb.Width - Th.Px(12), cb.Height);
            string prim = Primary();
            if (r.Group)
            {
                Color fg = r.HasPrim ? Th.HaveFg : Th.MissFg;
                var kids = KidsOf(e.RowIndex);
                int n = kids.Count(k => sel.TryGetValue(k.Path, out var on) && on);
                switch (e.ColumnIndex)
                {
                    case 0:
                        TextRenderer.DrawText(g, IsOpen(r) ? "▾" : "▸", Th.Base, new Rectangle(cb.X + Th.Px(4), cb.Y, Th.Px(20), cb.Height), Th.Muted,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                        var img = Th.Box(kids.Count > 0 && n == kids.Count ? "on" : n > 0 ? "part" : "off", bg);
                        g.DrawImage(img, cb.X + Th.Px(24), cb.Y + (cb.Height - img.Height) / 2);
                        TextRenderer.DrawText(g, $"{Core.LangName(r.Base)}  ·  {r.Base}", Th.Bold,
                            new Rectangle(cb.X + Th.Px(24) + img.Width + Th.Px(8), cb.Y, cb.Width, cb.Height), fg, flags);
                        break;
                    case 1: TextRenderer.DrawText(g, (r.HasPrim ? "●  In " : "●  Not in ") + prim, Th.Bold, tr, fg, flags); break;
                    case 2: TextRenderer.DrawText(g, n > 0 ? $"{n} selected" : "", Th.Bold, tr, fg, flags); break;
                    case 5: TextRenderer.DrawText(g, "Available from  " + string.Join(", ", r.Provs), Th.Bold, tr, fg, flags); break;
                }
            }
            else
            {
                bool on = sel.TryGetValue(r.S.Path, out var o) && o;
                Color fg = on ? Th.Fg : Th.OffFg;
                switch (e.ColumnIndex)
                {
                    case 0:
                        var img = Th.Box(on ? "on" : "off", bg);
                        g.DrawImage(img, cb.X + Th.Px(46), cb.Y + (cb.Height - img.Height) / 2);
                        break;
                    case 1: TextRenderer.DrawText(g, r.S.Provider, Th.Base, tr, fg, flags); break;
                    case 2: TextRenderer.DrawText(g, r.S.Variant, Th.Base, tr, fg, flags); break;
                    case 3: TextRenderer.DrawText(g, r.S.Cues > 0 ? r.S.Cues.ToString() : "—", Th.Base, tr, fg, flags | TextFormatFlags.HorizontalCenter); break;
                    case 4: TextRenderer.DrawText(g, Core.Kb(r.S.Size), Th.Base, tr, fg, flags | TextFormatFlags.HorizontalCenter); break;
                    case 5:
                        string rel = r.S.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? r.S.Path.Substring(folder.Length).TrimStart('\\') : r.S.Path;
                        TextRenderer.DrawText(g, rel, Th.Base, tr, fg, flags);
                        break;
                }
            }
            e.Handled = true;
        }

        // ---------------------------------------------------------------- page 2: track order
        static readonly (string key, string title, int w, string kind, bool edit, string ph)[] T2 =
        {
            ("n", "#", 38, "num", false, ""), ("sync", "SYNC", 54, "check", false, ""),
            ("lang", "LANG CODE", 100, "pill", true, ""), ("lname", "LANGUAGE", 150, "muted", false, ""),
            ("name", "TRACK NAME", 180, "field", true, "no name"), ("sdh", "SDH", 52, "check", false, ""),
            ("forced", "FORCED", 64, "check", false, ""), ("default", "DEFAULT", 68, "check", false, ""),
            ("delay", "DELAY", 90, "field", true, "0 ms"), ("tags", "SOURCE TAG  (text or .xml)", 250, "field", true, "+ add source"),
            ("provider", "PROVIDER", 92, "muted", false, ""), ("file", "FILE", 140, "muted", false, ""),
        };
        TextBox cellEditor;

        void BuildOrder(Panel t)
        {
            var tb = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Th.Px(42), WrapContents = false, Padding = new Padding(0, 0, 0, Th.Px(8)) };
            Button B(string s, Action a, bool acc = false, int left = 6) { var b = Th.Btn(s, (o, e) => a(), acc); b.Margin = new Padding(Th.Px(left), 0, 0, 0); tb.Controls.Add(b); return b; }
            B("Auto sort", AutoSort, false, 0);
            B("▲  Move up", () => MoveTrack(-1));
            B("▼  Move down", () => MoveTrack(1));
            B("Reset edits", ResetOverrides);
            B("Sync…", SyncDialog, true, 14);
            orderInfo = Th.Lbl("", Th.Muted); orderInfo.Margin = new Padding(Th.Px(14), Th.Px(8), 0, 0);
            tb.Controls.Add(orderInfo);
            var hint = Th.Lbl("SYNC = what Sync uses (header = all / none) · double-click to edit · right-click = bulk actions", Th.Muted);
            hint.Margin = new Padding(Th.Px(14), Th.Px(8), 0, 0);
            tb.Controls.Add(hint);

            var card = new Card { Dock = DockStyle.Fill };
            trackGrid = new Grid { Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, MultiSelect = true };
            Th.StyleGrid(trackGrid, Th.Panel);
            trackGrid.RowTemplate.Height = Th.Px(34);
            foreach (var c in T2)
                trackGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = c.title, Width = Th.Px(c.w), SortMode = DataGridViewColumnSortMode.NotSortable, Name = c.key,
                    AutoSizeMode = c.key == "file" ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
                    MinimumWidth = Th.Px(Math.Min(c.w, 60)),
                    HeaderCell = { Style = { Alignment = c.kind == "check" || c.kind == "num" || c.key == "delay" ? DataGridViewContentAlignment.MiddleCenter : DataGridViewContentAlignment.MiddleLeft } },
                });
            trackGrid.CellPainting += TrackPaint;
            trackGrid.PreMouseDown = TrackPreMouseDown;
            trackGrid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= trackRows.Count || e.ColumnIndex < 0) return;
                var c = T2[e.ColumnIndex];
                if (c.edit) EditCell(e.RowIndex, e.ColumnIndex);
                else if (c.key == "lname" || c.key == "provider" || c.key == "file") Tools.OpenPath(trackRows[e.RowIndex].Src);
            };
            trackGrid.ColumnHeaderMouseClick += (s, e) => { if (T2[e.ColumnIndex].key == "sync") OnSyncHeader(); };
            trackGrid.Scroll += (s, e) => EndEdit(true);
            var menu = new DarkMenu();
            menu.Add("Sync ticked tracks…", SyncDialog);
            menu.Add("Tick SYNC only for selected tracks", () => SetSync(SelectedTrackPaths()));
            menu.Add("Tick SYNC for all tracks", () => SetSync(null));
            menu.Add("Undo sync", () => UndoSync(SelectedTrackPaths()));
            menu.Sep();
            menu.Add("Type Source text for selected tracks…", () => AskSourceText(SelectedTrackPaths()));
            menu.Add("Choose existing tags XML for selected tracks…", () => ChooseTags(SelectedTrackPaths()));
            menu.Add("Use provider as Source  (e.g. \"iTunes WEB-DL\")", () => SourceFromProvider(SelectedTrackPaths()));
            menu.Add("Clear Source tag", () => ClearTags(SelectedTrackPaths()));
            menu.Sep();
            menu.Add("Set delay for selected tracks…", () => AskDelay(SelectedTrackPaths()));
            menu.Add("Reset edits of selected tracks", () => ResetRows(SelectedTrackPaths()));
            trackGrid.CellMouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
                if (!trackGrid.Rows[e.RowIndex].Selected) { trackGrid.ClearSelection(); trackGrid.Rows[e.RowIndex].Selected = true; }
            };
            trackGrid.CellMouseUp += (s, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) menu.Show(Cursor.Position); };
            card.Controls.Add(trackGrid);

            var foot = Th.Lbl("Everything here goes into the export · yellow = edited · ⟳ teal = synced copy is used · " +
                              "Source tag: type text (an XML is made for you) or a path to an existing .xml", Th.Muted);
            foot.Dock = DockStyle.Bottom; foot.AutoSize = false; foot.Height = Th.Px(26); foot.Padding = new Padding(Th.Px(2), Th.Px(8), 0, 0);
            t.Controls.Add(card);
            t.Controls.Add(foot);
            t.Controls.Add(tb);
        }

        List<string> SelectedTrackPaths() =>
            trackGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).Where(i => i < trackRows.Count)
                     .OrderBy(i => i).Select(i => trackRows[i].PathKey).ToList();

        bool TrackPreMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return false;
            var hit = trackGrid.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.Cell || hit.RowIndex >= trackRows.Count) return false;
            var c = T2[hit.ColumnIndex];
            if (c.kind == "check") { ToggleFlag(hit.RowIndex, c.key); return true; }
            if (c.key == "tags")
            {
                var rect = trackGrid.GetCellDisplayRectangle(hit.ColumnIndex, hit.RowIndex, false);
                if (e.X >= rect.Right - Th.Px(5) - Th.Px(28))
                {
                    var rows = trackGrid.Rows[hit.RowIndex].Selected ? SelectedTrackPaths() : new List<string> { trackRows[hit.RowIndex].PathKey };
                    ChooseTags(rows);
                    return true;
                }
            }
            return false;
        }

        void TrackPaint(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= trackRows.Count || e.ColumnIndex < 0) return;
            var t = trackRows[e.RowIndex];
            var c = T2[e.ColumnIndex];
            var g = e.Graphics;
            var cb = e.CellBounds;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool selected = trackGrid.Rows[e.RowIndex].Selected;
            Color bg = selected ? Th.Sel : (e.RowIndex % 2 == 1 ? Th.Stripe : Th.Panel);
            using (var bb = new SolidBrush(bg)) g.FillRectangle(bb, cb);
            var changed = overrides.TryGetValue(t.PathKey, out var ov) ? ov.Keys : (IEnumerable<string>)new string[0];
            bool ch = changed.Contains(c.key);
            int pad = Th.Px(10);
            float cy = cb.Y + cb.Height / 2f;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            string val = CellText(t, e.RowIndex, c.key);
            switch (c.kind)
            {
                case "num":
                    TextRenderer.DrawText(g, val, Th.Base, cb, Th.Faint, flags | TextFormatFlags.HorizontalCenter);
                    break;
                case "check":
                    bool on = c.key == "sync" ? !noSync.Contains(t.PathKey) && !t.Sub.Pgs : c.key == "sdh" ? t.Sdh : c.key == "forced" ? t.Forced : t.Default;
                    var img = Th.Box(on ? "on" : "off", bg);
                    g.DrawImage(img, cb.X + (cb.Width - img.Width) / 2, cb.Y + (cb.Height - img.Height) / 2);
                    break;
                case "pill":
                    {
                        string txt = Th.Fit(g, Th.SmallBold, val, cb.Width - 3 * pad);
                        int tw = TextRenderer.MeasureText(g, txt, Th.SmallBold, Size.Empty, TextFormatFlags.NoPadding).Width + Th.Px(16);
                        var pr = new RectangleF(cb.X + pad, cb.Y + Th.Px(7), tw, cb.Height - Th.Px(14));
                        using (var path = Th.Round(pr, Th.Px(9)))
                        {
                            using (var br = new SolidBrush(Th.PillBg)) g.FillPath(br, path);
                            if (ch) using (var p = new Pen(Th.Edit)) g.DrawPath(p, path);
                        }
                        TextRenderer.DrawText(g, txt, Th.SmallBold, Rectangle.Round(pr), ch ? Th.Edit : Th.PillFg,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        break;
                    }
                case "field":
                    {
                        int x1 = cb.X + Th.Px(5), x2 = cb.Right - Th.Px(5);
                        var fr = new RectangleF(x1, cb.Y + Th.Px(5), x2 - x1, cb.Height - Th.Px(10));
                        using (var path = Th.Round(fr, Th.Px(6)))
                        {
                            using (var br = new SolidBrush(Th.Field)) g.FillPath(br, path);
                            using (var p = new Pen(ch ? Th.Edit : Th.Border)) g.DrawPath(p, path);
                        }
                        if (c.key == "tags")
                        {
                            int bx1 = x2 - Th.Px(28), bx2 = x2 - Th.Px(3);
                            var brr = new RectangleF(bx1, cb.Y + Th.Px(8), bx2 - bx1, cb.Height - Th.Px(16));
                            using (var path = Th.Round(brr, Th.Px(4))) using (var br = new SolidBrush(Th.Hover)) g.FillPath(br, path);
                            if (Th.Icon != null) TextRenderer.DrawText(g, "", Th.Icon, Rectangle.Round(brr), Th.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                            else TextRenderer.DrawText(g, "…", Th.Base, Rectangle.Round(brr), Th.Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                            x2 = bx1;
                        }
                        bool empty = string.IsNullOrEmpty(val);
                        string txt = empty ? c.ph : val;
                        Color col = empty ? Th.Faint : (ch ? Th.Edit : Th.Fg);
                        var trc = new Rectangle(x1 + Th.Px(8), cb.Y, x2 - x1 - Th.Px(12), cb.Height);
                        TextRenderer.DrawText(g, txt, Th.Base, trc, col, flags | (c.key == "delay" ? TextFormatFlags.HorizontalCenter : 0));
                        break;
                    }
                default:
                    {
                        Color col = c.key == "file" && t.Synced != "" ? Th.Sync : (c.kind == "muted" ? Th.Muted : Th.Fg);
                        TextRenderer.DrawText(g, val, Th.Base, new Rectangle(cb.X + pad, cb.Y, cb.Width - 2 * pad, cb.Height), col, flags);
                        break;
                    }
            }
            e.Handled = true;
        }

        string CellText(Track t, int i, string key)
        {
            switch (key)
            {
                case "n": return (i + 1).ToString();
                case "lang": return t.Lang;
                case "lname": return Core.LangDisplay(t.Lang);
                case "name": return t.Name;
                case "delay": return t.Delay != 0 ? $"{t.Delay:+0;-0} ms" : "";
                case "tags": return Core.IsXmlPath(t.Tags) ? "XML · " + Path.GetFileName(t.Tags) : t.Tags;
                case "provider": return t.Sub.Provider;
                case "file": return (t.Synced != "" ? "⟳ synced · " : "") + t.Sub.Name;
            }
            return "";
        }

        void EditCell(int row, int col)
        {
            EndEdit(true);
            var t = trackRows[row];
            string key = T2[col].key;
            string value = key == "delay" ? (t.Delay != 0 ? t.Delay.ToString() : "") : key == "lang" ? t.Lang : key == "name" ? t.Name : t.Tags;
            var rect = trackGrid.GetCellDisplayRectangle(col, row, false);
            cellEditor = new TextBox
            {
                Text = value, BackColor = Th.Field, ForeColor = Th.Fg, BorderStyle = BorderStyle.FixedSingle, Font = Th.Base,
                Bounds = new Rectangle(rect.X + Th.Px(4), rect.Y + Th.Px(5), Math.Max(rect.Width - Th.Px(8), Th.Px(200)), rect.Height - Th.Px(10)),
                Tag = (t.PathKey, key),
            };
            trackGrid.Controls.Add(cellEditor);
            cellEditor.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; EndEdit(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndEdit(false); }
            };
            cellEditor.LostFocus += (s, e) => EndEdit(true);
            cellEditor.Focus();
            cellEditor.SelectAll();
        }

        void EndEdit(bool commit)
        {
            var ed = cellEditor;
            if (ed == null) return;
            cellEditor = null;
            var (path, key) = ((string, string))ed.Tag;
            string v = ed.Text;
            trackGrid.Controls.Remove(ed);
            ed.Dispose();
            trackGrid.Focus();
            if (commit) BeginInvoke(new Action(() => CommitEdit(path, key, v)));
        }

        // ================================================================ data
        void PickFolder()
        {
            var d = Ask.Folder(this, "Subtitle folder", folder != "" ? folder : Environment.CurrentDirectory);
            if (d != null) { LoadFolder(d); ShowPage("pick"); }
        }

        string DefaultOut(string fmt) => fmt == "srt" ? Path.Combine(folder, "Selected") : Path.Combine(folder, $"{title}.{fmt}");

        void OnFmt()
        {
            string fmt = fmtSeg.Value, cur = outPath.Text.Trim();
            var defaults = folder != "" ? new[] { "srt", "mks", "mkv" }.Select(DefaultOut).ToList() : new List<string>();
            if (cur == "" || defaults.Contains(cur)) outPath.Text = folder != "" ? DefaultOut(fmt) : "";
            else if (fmt != "srt")
            {
                if (Core.HasExt(cur, new[] { ".mks", ".mkv" })) outPath.Text = Path.ChangeExtension(cur, "." + fmt);
                else outPath.Text = Path.Combine(cur, $"{title}.{fmt}");
            }
            else if (Core.HasExt(cur, new[] { ".mks", ".mkv" })) outPath.Text = Path.GetDirectoryName(cur);
            numChk.Enabled = fmt == "srt";
            UpdateStatus();
        }

        void PickOut()
        {
            string fmt = fmtSeg.Value, cur = outPath.Text.Trim();
            if (fmt == "srt")
            {
                var d = Ask.Folder(this, "Export SRT files to", cur != "" ? cur : folder);
                if (d != null) outPath.Text = d;
            }
            else
                using (var f = new SaveFileDialog { Filter = $"Matroska {fmt.ToUpperInvariant()}|*.{fmt}", DefaultExt = fmt, FileName = Path.GetFileName(cur) })
                {
                    string dir = cur != "" ? Path.GetDirectoryName(cur) : folder;
                    if (Directory.Exists(dir)) f.InitialDirectory = dir;
                    if (f.ShowDialog(this) == DialogResult.OK) outPath.Text = f.FileName;
                }
        }

        public void LoadFolder(string dir, bool keep = false, ICollection<string> newly = null)
        {
            dir = Path.GetFullPath(dir).TrimEnd('\\');
            MergeTagsDir();
            var paths = Core.ListSubs(dir);
            if (paths.Count == 0) { Ask.Warn(this, $"No subtitle files found in:\n{dir}"); return; }
            var newSet = new HashSet<string>(newly ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var oldSel = keep ? new Dictionary<string, bool>(sel) : new Dictionary<string, bool>();
            Cursor = Cursors.WaitCursor;
            try
            {
                folder = dir;
                var segs = dir.Split('\\');
                folderLbl.Text = dir.Length <= 48 || segs.Length <= 3 ? dir : "…\\" + string.Join("\\", segs.Skip(segs.Length - 2));
                subs = paths.Select(p => new Sub(p)).ToList();
                var textCues = subs.Where(s => s.Cues > 0).Select(s => s.Cues).ToList();
                double med = Core.Median(textCues);
                foreach (var s in subs)
                    if (s.Cues > 0 && !s.IsForced && !s.IsSdh && s.Cues < Core.ForcedRatio * med) s.Auto.Add("forced");
                title = Core.CommonTitle(paths.Select(Path.GetFileName).ToList(), Path.GetFileName(dir));
                var present = subs.Select(s => s.Provider).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (keep)
                {
                    provOrder.AddRange(present.Where(p => !provOrder.Contains(p)));
                    foreach (var p in present) if (!provOn.ContainsKey(p)) provOn[p] = true;
                    sel = subs.ToDictionary(s => s.Path, s => oldSel.TryGetValue(s.Path, out var v) ? v : newSet.Contains(s.Path));
                    provList.Refresh2();
                    RebuildPick();
                    return;
                }
                overrides = new Dictionary<string, Dictionary<string, object>>();
                noSync = new HashSet<string>();
                manualOrder = null;
                provFilter = null;
                groupOpen.Clear();
                provOrder = Core.DefaultPriority.Where(present.Contains).Concat(present.Where(p => !Core.DefaultPriority.Contains(p))).ToList();
                provOn = provOrder.ToDictionary(p => p, p => true);
                sel = subs.ToDictionary(s => s.Path, s => false);
                outPath.Text = "";
                OnFmt();
                provList.Refresh2();
                AutoSelect();
            }
            finally { Cursor = Cursors.Default; }
        }

        List<string> EnabledProvs() => provOrder.Where(p => provOn.TryGetValue(p, out var on) && on).ToList();
        string Primary() => EnabledProvs().FirstOrDefault();

        Dictionary<string, List<Sub>> Groups()
        {
            var en = EnabledProvs();
            var rank = en.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
            var g = new Dictionary<string, List<Sub>>();
            foreach (var s in subs)
                if (rank.ContainsKey(s.Provider))
                {
                    if (!g.TryGetValue(s.Base, out var l)) g[s.Base] = l = new List<Sub>();
                    l.Add(s);
                }
            foreach (var items in g.Values)
                items.Sort((a, b) => Core.CmpKeys(
                    new object[] { rank[a.Provider], Core.RegionInfo(a).rank, a.IsSdh, a.IsForced, a.Name },
                    new object[] { rank[b.Provider], Core.RegionInfo(b).rank, b.IsSdh, b.IsForced, b.Name }));
            return g;
        }

        void AutoSelect()
        {
            foreach (var k in sel.Keys.ToList()) sel[k] = false;
            foreach (var items in Groups().Values)
            {
                string best = items[0].Provider;
                var seen = new HashSet<(string, bool, bool)>();
                foreach (var s in items)
                {
                    var key = (s.Tag, s.IsSdh, s.IsForced);
                    if (s.Provider == best && seen.Add(key)) sel[s.Path] = true;
                }
            }
            RebuildPick();
        }

        void SelectNone()
        {
            foreach (var k in sel.Keys.ToList()) sel[k] = false;
            RefreshMarks();
        }

        List<Sub> Chosen()
        {
            var en = new HashSet<string>(EnabledProvs());
            return subs.Where(s => sel.TryGetValue(s.Path, out var on) && on && en.Contains(s.Provider)).ToList();
        }

        public void OnProvChange()
        {
            RebuildPick();
        }

        public void SetProvFilter(string p)
        {
            provFilter = p;
            provList.Refresh2();
            RebuildPick();
        }

        void Expand(bool state)
        {
            foreach (var r in pickRows.Where(r => r.Group)) groupOpen[r.Base] = state;
            RebuildPick();
        }

        void RebuildPick()
        {
            if (pickGrid == null) return;
            int first = pickGrid.FirstDisplayedScrollingRowIndex;
            string prim = Primary();
            var groups = Groups();
            bool HasPrim(string b) => groups[b].Any(s => s.Provider == prim);
            object[] MkvKey(string b) => new object[] { b != "en", Core.Family(b).ToLowerInvariant(), b == "yue", b };
            var order = groups.Keys.ToList();
            if (sortMode.Value == "missing")
                order.Sort((a, b) => Core.CmpKeys(new object[] { HasPrim(a) }.Concat(MkvKey(a)).ToArray(), new object[] { HasPrim(b) }.Concat(MkvKey(b)).ToArray()));
            else order.Sort((a, b) => Core.CmpKeys(MkvKey(a), MkvKey(b)));
            pickRows.Clear();
            foreach (var b in order)
            {
                var items = groups[b];
                bool hp = HasPrim(b);
                if ((view.Value == "missing" && hp) || (view.Value == "primary" && !hp)) continue;
                var shown = provFilter != null ? items.Where(s => s.Provider == provFilter).ToList() : items;
                if (shown.Count == 0) continue;
                var gr = new PickRow { Group = true, Base = b, HasPrim = hp, Provs = items.Select(s => s.Provider).Distinct().ToList(), Shown = shown.Count };
                pickRows.Add(gr);
                if (IsOpen(gr)) foreach (var s in shown) pickRows.Add(new PickRow { Base = b, S = s });
            }
            pickGrid.RowCount = 0;
            pickGrid.RowCount = pickRows.Count;
            pickGrid.ClearSelection();
            if (first >= 0 && first < pickRows.Count) try { pickGrid.FirstDisplayedScrollingRowIndex = first; } catch { }
            RefreshMarks();
        }

        void RefreshMarks()
        {
            pickGrid.Invalidate();
            provList.Refresh2();
            UpdateStatus();
            RefreshTracks();
        }

        void UpdateStatus()
        {
            if (subs.Count == 0 || exportBtn == null) return;
            string prim = Primary();
            var g = Groups();
            var missing = g.Where(kv => !kv.Value.Any(s => s.Provider == prim)).Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var chosen = Chosen();
            int auto = subs.Count(s => s.Auto.Count > 0);
            status.Text = $"{subs.Count} files  ·  {g.Count} languages  ·  primary {prim}  ·  " +
                          $"{missing.Count} not in {prim}: {(missing.Count > 0 ? string.Join(", ", missing) : "–")}  ·  " +
                          $"{auto} flagged from content  ·  selected {chosen.Count} files / {chosen.Select(s => s.Base).Distinct().Count()} languages";
            string fmt = fmtSeg.Value;
            exportBtn.Text = fmt == "srt" ? $"Copy {chosen.Count} files" : $"Create {fmt.ToUpperInvariant()}  ·  {chosen.Count} tracks";
        }

        // ---------------------------------------------------------------- tracks
        List<Track> Tracks()
        {
            var prank = provOrder.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
            object[] Key(Sub s)
            {
                var (vr, _, rk) = Core.RegionInfo(s);
                return new object[] { s.Base != "en", Core.Family(s.Base).ToLowerInvariant(), (s.Base == "yue" ? 100 : 0) + vr, rk,
                                      (s.IsSdh ? 1 : 0) + (s.IsForced ? 2 : 0), prank.TryGetValue(s.Provider, out var pr) ? pr : 99,
                                      s.Name.ToLowerInvariant() };
            }
            var chosen = Chosen();
            chosen.Sort((a, b) => Core.CmpKeys(Key(a), Key(b)));
            var seen = new Dictionary<string, int>();
            var outp = new List<Track>();
            for (int i = 0; i < chosen.Count; i++)
            {
                var s = chosen[i];
                var (vr, rtitle, rk) = Core.RegionInfo(s);
                string gk = $"{s.Base}|{vr}|{rk}|{s.IsSdh}|{s.IsForced}";
                int n = seen.TryGetValue(gk, out var c) ? c : 0;
                seen[gk] = n + 1;
                var parts = new List<string>();
                if (s.IsForced) parts.Add("Forced");
                if (s.IsSdh) parts.Add("SDH");
                if (rtitle != "") parts.Add(rtitle);
                if (n > 0) parts.Add(n == 1 ? "Alternate" : $"Alternate {n}");
                var t = new Track
                {
                    PathKey = s.Path, Sub = s, Lang = s.Tag, Name = string.Join(" / ", parts), Sdh = s.IsSdh, Forced = s.IsForced,
                    Default = false, Delay = 0, Tags = AutoTag(s), Synced = "", I = i,
                };
                if (overrides.TryGetValue(s.Path, out var ov))
                    foreach (var kv in ov)
                        switch (kv.Key)
                        {
                            case "lang": t.Lang = (string)kv.Value; break;
                            case "name": t.Name = (string)kv.Value; break;
                            case "sdh": t.Sdh = (bool)kv.Value; break;
                            case "forced": t.Forced = (bool)kv.Value; break;
                            case "default": t.Default = (bool)kv.Value; break;
                            case "delay": t.Delay = (int)kv.Value; break;
                            case "tags": t.Tags = (string)kv.Value; break;
                            case "synced": t.Synced = (string)kv.Value; break;
                        }
                if (t.Synced != "" && !File.Exists(t.Synced)) t.Synced = "";
                t.Src = t.Synced != "" ? t.Synced : s.Path;
                outp.Add(t);
            }
            if (manualOrder != null)
            {
                var idx = manualOrder.Select((p, k) => (p, k)).ToDictionary(x => x.p, x => x.k);
                outp = outp.OrderBy(t => idx.TryGetValue(t.PathKey, out var k) ? k : idx.Count + t.I).ToList();
            }
            return outp;
        }

        Track TrackOf(string path) => Tracks().First(t => t.PathKey == path);

        string AutoTag(Sub s)
        {
            var table = cfg.Map("source_tags").GroupBy(kv => kv.Key.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Value);
            foreach (var k in Core.ProviderKeys(s))
                if (table.TryGetValue(k, out var p) && !string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            return "";
        }

        void MergeTagsDir()
        {
            string dir = cfg["tags_dir"];
            if (dir == "" || !Directory.Exists(dir)) return;
            var table = cfg.Map("source_tags");
            var have = new HashSet<string>(table.Keys.Select(k => k.ToLowerInvariant()));
            var (found, _) = Core.ScanTagsDir(dir);
            var add = found.Where(kv => !have.Contains(kv.Key.ToLowerInvariant())).ToList();
            if (add.Count == 0) return;
            foreach (var kv in add) table[kv.Key] = kv.Value;
            cfg.SetMap("source_tags", table);
            cfg.Save();
        }

        void RefreshTracks()
        {
            if (trackGrid == null) return;
            var selKeys = new HashSet<string>(SelectedTrackPaths());
            int first = trackGrid.FirstDisplayedScrollingRowIndex;
            trackRows = Tracks();
            trackGrid.RowCount = 0;
            trackGrid.RowCount = trackRows.Count;
            trackGrid.ClearSelection();
            for (int i = 0; i < trackRows.Count; i++) if (selKeys.Contains(trackRows[i].PathKey)) trackGrid.Rows[i].Selected = true;
            if (first >= 0 && first < trackRows.Count) try { trackGrid.FirstDisplayedScrollingRowIndex = first; } catch { }
            int edited = overrides.Count(kv => kv.Value.Count > 0);
            int synced = trackRows.Count(t => t.Synced != "");
            orderInfo.Text = $"{trackRows.Count} tracks  ·  " + (manualOrder != null ? "manual order" : "auto sorted") +
                             (edited > 0 ? $"  ·  {edited} edited" : "") + (synced > 0 ? $"  ·  {synced} synced" : "");
            tabs.SetLabel("order", $"Track order  ·  {trackRows.Count}");
        }

        void SetOverride(IEnumerable<string> paths, string key, object value)
        {
            foreach (var p in paths)
            {
                if (!overrides.TryGetValue(p, out var ov)) overrides[p] = ov = new Dictionary<string, object>();
                ov[key] = value;
            }
            RefreshTracks();
        }

        void ToggleFlag(int row, string key)
        {
            var t = trackRows[row];
            var targets = trackGrid.Rows[row].Selected ? SelectedTrackPaths() : new List<string> { t.PathKey };
            if (key == "sync")
            {
                if (t.Sub.Pgs) { status.Text = "PGS (.sup) tracks are never synced - use the DELAY column to shift them."; return; }
                if (noSync.Contains(t.PathKey)) foreach (var p in targets) noSync.Remove(p);
                else foreach (var p in targets) noSync.Add(p);
                RefreshTracks();
                return;
            }
            bool cur = key == "sdh" ? t.Sdh : key == "forced" ? t.Forced : t.Default;
            SetOverride(targets, key, !cur);
        }

        void SetSync(List<string> paths)
        {
            var all = Tracks().Select(t => t.PathKey);
            noSync = paths == null ? new HashSet<string>() : new HashSet<string>(all.Except(paths));
            RefreshTracks();
        }

        void OnSyncHeader()
        {
            var ts = Tracks().Where(t => !t.Sub.Pgs).ToList();
            bool allOn = ts.All(t => !noSync.Contains(t.PathKey));
            SetSync(allOn ? new List<string>() : null);
        }

        void AutoSort() { manualOrder = null; RefreshTracks(); }

        void ResetOverrides()
        {
            if (overrides.Values.Any(o => o.ContainsKey("synced")) &&
                !Ask.YesNo(this, "This also forgets the synced copies (the files stay on disk). Continue?")) return;
            overrides = new Dictionary<string, Dictionary<string, object>>();
            RefreshTracks();
        }

        void ResetRows(List<string> paths) { foreach (var p in paths) overrides.Remove(p); RefreshTracks(); }

        void MoveTrack(int d)
        {
            var selp = SelectedTrackPaths();
            if (selp.Count == 0) return;
            var order = Tracks().Select(t => t.PathKey).ToList();
            var idxs = selp.Select(p => order.IndexOf(p)).OrderBy(i => i).ToList();
            if (d < 0 && idxs[0] == 0) return;
            if (d > 0 && idxs[idxs.Count - 1] == order.Count - 1) return;
            foreach (int i in d < 0 ? (IEnumerable<int>)idxs : Enumerable.Reverse(idxs))
            {
                var tmp = order[i + d]; order[i + d] = order[i]; order[i] = tmp;
            }
            manualOrder = order;
            RefreshTracks();
            int firstRow = trackRows.FindIndex(t => selp.Contains(t.PathKey));
            if (firstRow >= 0) { try { trackGrid.CurrentCell = trackGrid.Rows[firstRow].Cells[0]; } catch { } }
            foreach (var r in trackRows.Select((t, i) => (t, i)).Where(x => selp.Contains(x.t.PathKey))) trackGrid.Rows[r.i].Selected = true;
        }

        void CommitEdit(string path, string key, string v)
        {
            v = v.Trim();
            object value = v;
            if (key == "delay")
            {
                string x = v.Replace("ms", "").Replace("+", "").Trim();
                if (x == "") x = "0";
                if (!int.TryParse(x, out int ms)) { Ask.Error(this, "Delay must be a whole number of milliseconds."); return; }
                value = ms;
            }
            else if (key == "lang")
            {
                if (!Core.LangTagRe.IsMatch(v)) { Ask.Error(this, $"\"{v}\" is not a valid language tag (e.g. en, en-GB, zh-Hans)."); return; }
            }
            else if (key == "tags")
            {
                v = v.Trim('"');
                if (v.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !File.Exists(v)) { Ask.Error(this, $"Tags file not found:\n{v}"); return; }
                value = v;
            }
            SetOverride(new[] { path }, key, value);            // '' = no tag (also hides the provider XML)
        }

        void ChooseTags(List<string> paths)
        {
            if (paths.Count == 0) return;
            var f = Ask.File(this, "Tags XML for the selected track(s)", "Matroska tags XML|*.xml|All files|*.*", folder);
            if (f != null) SetOverride(paths, "tags", f);
        }

        void ClearTags(List<string> paths) => SetOverride(paths, "tags", "");

        void AskSourceText(List<string> paths)
        {
            if (paths.Count == 0) return;
            string cur = TrackOf(paths[0]).Tags;
            var v = Ask.Text(this, "Source tag", $"Source text for {paths.Count} track(s):", Core.IsXmlPath(cur) ? "" : cur);
            if (v == null) return;
            if (v.Trim() != "") SetOverride(paths, "tags", v.Trim());
            else ClearTags(paths);
        }

        void AskDelay(List<string> paths)
        {
            if (paths.Count == 0) return;
            var v = Ask.Text(this, "Delay", $"Delay in ms for {paths.Count} track(s) (negative = earlier):", TrackOf(paths[0]).Delay.ToString());
            if (v == null) return;
            if (!int.TryParse(v.Trim(), out int ms)) { Ask.Error(this, "Delay must be a whole number of milliseconds."); return; }
            SetOverride(paths, "delay", ms);
        }

        void SourceFromProvider(List<string> paths)
        {
            foreach (var p in paths)
            {
                var prov = subs.FirstOrDefault(s => s.Path == p)?.Provider ?? "";
                if (prov == "") continue;
                if (!overrides.TryGetValue(p, out var ov)) overrides[p] = ov = new Dictionary<string, object>();
                ov["tags"] = prov == "Blu-ray" ? prov : $"{prov} WEB-DL";
            }
            RefreshTracks();
        }

        void UndoSync(List<string> paths)
        {
            foreach (var p in paths) if (overrides.TryGetValue(p, out var ov)) ov.Remove("synced");
            RefreshTracks();
        }

        // ---------------------------------------------------------------- drag & drop / add files
        void EnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files) BeginInvoke(new Action(() => OnDrop(files)));
            };
            foreach (Control k in c.Controls) EnableDrop(k);
        }

        void OnDrop(string[] paths)
        {
            var subsIn = new List<string>(); var mkvs = new List<string>(); var dirs = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) dirs.Add(p);
                else if (Core.HasExt(p, Core.SubExts)) subsIn.Add(p);
                else if (Core.HasExt(p, Core.MkvExts)) mkvs.Add(p);
            }
            if (folder == "")
            {
                if (dirs.Count > 0) { LoadFolder(dirs[0]); ShowPage("pick"); return; }
                if (subsIn.Count > 0) { LoadFolder(Path.GetDirectoryName(subsIn[0]), false, subsIn); ShowPage("pick"); return; }
            }
            foreach (var d in dirs)
                subsIn.AddRange(Directory.GetFiles(d, "*", SearchOption.AllDirectories).Where(f => Core.HasExt(f, Core.SubExts)));
            if (subsIn.Count > 0)
            {
                AddFiles(subsIn);
                if (tabs.Value == "download") ShowPage("pick");
            }
            foreach (var m in mkvs) ExtractFromMkv(m);
            if (subsIn.Count == 0 && mkvs.Count == 0) status.Text = "Nothing to add - drop .srt / .ass / .ssa / .vtt / .sup files, a folder or an MKV.";
        }

        void AddFiles(List<string> files)
        {
            if (folder == "") { Ask.Info(this, "Open or download a subtitle folder first."); return; }
            if (files == null)
                using (var f = new OpenFileDialog { Title = "Add subtitle files", Multiselect = true, Filter = "Subtitles|*.srt;*.ass;*.ssa;*.vtt;*.sup|All files|*.*" })
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    files = f.FileNames.ToList();
                }
            var added = new List<string>();
            foreach (var f in files)
            {
                string dst = Path.Combine(folder, Path.GetFileName(f));
                if (!string.Equals(Path.GetFullPath(f), dst, StringComparison.OrdinalIgnoreCase))
                {
                    dst = Core.UniquePath(dst);
                    File.Copy(f, dst);
                }
                added.Add(dst);
            }
            LoadFolder(folder, true, added);
        }
    }

    // ==================================================================== provider list
    /// Provider priority list: tick = enabled, press-and-hold then drag = reorder, double-click = filter
    class ProviderList : Control
    {
        readonly MainForm app;
        int rh => Th.Px(30);
        int pressIndex = -1, dropIndex = -1;
        bool armed;
        readonly Timer hold = new Timer { Interval = 250 };
        Point pressPt;

        public ProviderList(MainForm app)
        {
            this.app = app;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Th.Panel;
            Height = rh;
            hold.Tick += (s, e) => { hold.Stop(); if (pressIndex >= 0) { armed = true; Cursor = Cursors.SizeNS; Invalidate(); } };
        }

        List<string> Order => app.ProvOrder;

        public void Refresh2()
        {
            Height = rh * (Order.Count + 1) + Th.Px(4);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Th.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            // "All files" row
            bool allActive = app.ProvFilter == null;
            var r0 = new Rectangle(0, 0, Width, rh);
            if (allActive) using (var br = new SolidBrush(Th.Accent)) g.FillRectangle(br, 0, Th.Px(4), Th.Px(3), rh - Th.Px(8));
            TextRenderer.DrawText(g, "All files", allActive ? Th.Bold : Th.Base, new Rectangle(Th.Px(12), 0, Width, rh), allActive ? Th.PillFg : Th.Fg, flags);
            TextRenderer.DrawText(g, app.SubCount.ToString(), Th.Small, new Rectangle(Th.Px(80), 0, Th.Px(60), rh), Th.Muted, flags);
            string prim = app.ProvPrimary;
            for (int i = 0; i < Order.Count; i++)
            {
                string p = Order[i];
                int y = rh * (i + 1);
                bool active = app.ProvFilter == p;
                if (active) using (var br = new SolidBrush(Th.Accent)) g.FillRectangle(br, 0, y + Th.Px(4), Th.Px(3), rh - Th.Px(8));
                var img = Th.Box(app.ProvEnabled(p) ? "on" : "off", Th.Panel);
                g.DrawImage(img, Th.Px(12), y + (rh - img.Height) / 2);
                int x = Th.Px(12) + img.Width + Th.Px(10);
                string name = p;
                int nameW = TextRenderer.MeasureText(g, name, active ? Th.Bold : Th.Base).Width;
                TextRenderer.DrawText(g, name, active ? Th.Bold : Th.Base, new Rectangle(x, y, Width - x - Th.Px(110), rh), active ? Th.PillFg : Th.Fg, flags);
                x += Math.Min(nameW, Width - x - Th.Px(110)) + Th.Px(4);
                TextRenderer.DrawText(g, app.ProvCount(p).ToString(), Th.Small, new Rectangle(x, y, Th.Px(40), rh), Th.Muted, flags);
                if (p == prim)
                    TextRenderer.DrawText(g, "PRIMARY", Th.SmallBold, new Rectangle(Width - Th.Px(100), y, Th.Px(70), rh), Th.Accent, flags);
                bool grip = armed && i == pressIndex;
                if (Th.Icon != null) TextRenderer.DrawText(g, "", Th.Icon, new Rectangle(Width - Th.Px(26), y, Th.Px(24), rh), grip ? Th.Accent : Th.Muted, flags);
                else TextRenderer.DrawText(g, "≡", Th.Bold, new Rectangle(Width - Th.Px(24), y, Th.Px(24), rh), grip ? Th.Accent : Th.Muted, flags);
            }
            if (armed && dropIndex >= 0)
                using (var br = new SolidBrush(Th.Accent)) g.FillRectangle(br, Th.Px(8), rh * (dropIndex + 1) - 1, Width - Th.Px(16), Th.Px(2));
        }

        int RowAt(int y) => y / rh - 1;                                  // -1 = "All files"

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = RowAt(e.Y);
            if (e.Y < rh) { app.SetProvFilter(null); return; }
            if (i < 0 || i >= Order.Count) return;
            if (e.X < Th.Px(12) + Th.Px(16) + Th.Px(6))                   // the tick box
            {
                app.ToggleProv(Order[i]);
                return;
            }
            pressIndex = i;
            pressPt = e.Location;
            armed = false;
            hold.Start();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!armed || pressIndex < 0) return;
            int target = Math.Max(0, Math.Min(Order.Count, (int)Math.Round((e.Y - rh) / (double)rh)));
            if (target != dropIndex) { dropIndex = target; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            hold.Stop();
            bool wasArmed = armed;
            int from = pressIndex, to = dropIndex;
            armed = false; pressIndex = -1; dropIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
            if (!wasArmed || from < 0 || to < 0 || to == from || to == from + 1) return;
            string p = Order[from];
            Order.RemoveAt(from);
            Order.Insert(to > from ? to - 1 : to, p);
            app.OnProvChange();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = RowAt(e.Y);
            if (i >= 0 && i < Order.Count && e.X >= Th.Px(34)) app.SetProvFilter(Order[i]);
        }

        protected override void Dispose(bool disposing) { if (disposing) hold.Dispose(); base.Dispose(disposing); }
    }

    partial class MainForm
    {
        public List<string> ProvOrder => provOrder;
        public string ProvFilter => provFilter;
        public string ProvPrimary => Primary();
        public int SubCount => subs.Count;
        public bool ProvEnabled(string p) => provOn.TryGetValue(p, out var on) && on;
        public int ProvCount(string p) => subs.Count(s => s.Provider == p);
        public void ToggleProv(string p) { provOn[p] = !ProvEnabled(p); OnProvChange(); }
    }
}
