// Dark theme + small custom controls (no third-party libraries)
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SubPicker
{
    static class Th
    {
        static Color H(string s) => ColorTranslator.FromHtml(s);
        public static readonly Color Bg = H("#131518"), Panel = H("#1c1f24"), Field = H("#262a31"), Hover = H("#2f343c"),
            Border = H("#343a43"), BoxBorder = H("#5f6774"), Fg = H("#e6e8eb"), Muted = H("#8f97a2"), Faint = H("#5d6570"),
            Accent = H("#4b8bff"), AccentHi = H("#6a9fff"), AccentDn = H("#3a74e0"), Sel = H("#25406e"),
            MissBg = H("#33261a"), MissFg = H("#ffb46e"), HaveBg = H("#1b2b22"), HaveFg = H("#86e0a6"),
            OffFg = H("#7d8590"), Stripe = H("#1a1d22"), Edit = H("#ffd479"), PillBg = H("#22355a"),
            PillFg = H("#a9c7ff"), Ok = H("#86e0a6"), Err = H("#ff7b7b"), Sync = H("#7fd6c2");

        public static float S = 1f;
        public static int Px(double v) => (int)Math.Round(v * S);
        public static Font Base, Small, Bold, SmallBold, Title, Mono, Icon;

        public static void Init(float dpi)
        {
            S = dpi / 96f;
            Base = new Font("Segoe UI", 9f);
            Small = new Font("Segoe UI", 8f);
            var fams = FontFamily.Families.Select(f => f.Name).ToHashSet();
            Bold = fams.Contains("Segoe UI Semibold") ? new Font("Segoe UI Semibold", 9f) : new Font("Segoe UI", 9f, FontStyle.Bold);
            SmallBold = fams.Contains("Segoe UI Semibold") ? new Font("Segoe UI Semibold", 8f) : new Font("Segoe UI", 8f, FontStyle.Bold);
            Title = fams.Contains("Segoe UI Variable Display") ? new Font("Segoe UI Variable Display", 15f, FontStyle.Bold)
                                                                 : new Font("Segoe UI", 15f, FontStyle.Bold);
            Mono = new Font(fams.Contains("Cascadia Mono") ? "Cascadia Mono" : "Consolas", 9f);
            Icon = fams.Contains("Segoe Fluent Icons") ? new Font("Segoe Fluent Icons", 10f)
                 : fams.Contains("Segoe MDL2 Assets") ? new Font("Segoe MDL2 Assets", 10f) : null;
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);

        public static void DarkTitle(Form f)
        {
            int v = 1;
            try { if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4); } catch { }
        }

        /// Dark native scrollbars (Windows 10 1809+).
        public static void DarkScroll(Control c)
        {
            void apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) apply(); else c.HandleCreated += (s, e) => apply();
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static readonly Dictionary<(string, int), Bitmap> boxes = new Dictionary<(string, int), Bitmap>();

        /// Anti-aliased rounded tick box: state 'on' | 'off' | 'part'
        public static Bitmap Box(string state, Color bg)
        {
            var key = (state, bg.ToArgb());
            if (boxes.TryGetValue(key, out var b)) return b;
            int size = Px(16);
            b = new Bitmap(size, size);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(bg);
                var r = new RectangleF(size * 0.1f, size * 0.1f, size * 0.8f, size * 0.8f);
                using (var path = Round(r, size * 0.22f))
                {
                    if (state == "off")
                    {
                        using (var br = new SolidBrush(Field)) g.FillPath(br, path);
                        using (var pen = new Pen(BoxBorder, Math.Max(1.2f, size / 12f))) g.DrawPath(pen, path);
                    }
                    else
                    {
                        using (var br = new SolidBrush(Accent)) g.FillPath(br, path);
                        using (var pen = new Pen(Color.White, size * 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            if (state == "on")
                                g.DrawLines(pen, new[] { new PointF(.28f * size, .52f * size), new PointF(.44f * size, .68f * size), new PointF(.74f * size, .35f * size) });
                            else
                                g.DrawLine(pen, .30f * size, .5f * size, .70f * size, .5f * size);
                        }
                    }
                }
            }
            boxes[key] = b;
            return b;
        }

        public static Button Btn(string text, EventHandler click, bool accent = false)
        {
            var b = new Button
            {
                Text = text, FlatStyle = FlatStyle.Flat, ForeColor = accent ? Color.White : Fg,
                BackColor = accent ? Accent : Field, Font = accent ? Bold : Base, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Px(10), Px(3), Px(10), Px(3)),
                MinimumSize = new Size(Px(80), Px(32)), MaximumSize = new Size(0, Px(32)),     // same height as a Field
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false,
                UseMnemonic = false,
            };
            b.FlatAppearance.BorderColor = accent ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = accent ? AccentHi : Hover;
            b.FlatAppearance.MouseDownBackColor = accent ? AccentDn : Border;
            if (click != null) b.Click += click;
            b.EnabledChanged += (s, e) => { b.ForeColor = b.Enabled ? (accent ? Color.White : Fg) : Faint; b.BackColor = b.Enabled ? (accent ? Accent : Field) : Panel; };
            return b;
        }

        public static Label Lbl(string text, Color? fg = null, Font font = null, Color? bg = null) => new Label
        {
            Text = text, AutoSize = true, ForeColor = fg ?? Fg, Font = font ?? Base, BackColor = bg ?? Color.Transparent,
            Margin = new Padding(0), UseMnemonic = false,
        };

        public static Label Head(string text) => Lbl(text.ToUpperInvariant(), Muted, SmallBold);

        public static void StyleGrid(DataGridView g, Color bg)
        {
            g.BackgroundColor = bg;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Border;
            g.CellBorderStyle = DataGridViewCellBorderStyle.None;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Bg, ForeColor = Muted, Font = SmallBold, SelectionBackColor = Bg, SelectionForeColor = Muted,
                Padding = new Padding(Px(6), 0, 0, 0), WrapMode = DataGridViewTriState.False,
            };
            g.ColumnHeadersHeight = Px(30);
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = bg, ForeColor = Fg, Font = Base, SelectionBackColor = Sel, SelectionForeColor = Fg,
                Padding = new Padding(Px(6), 0, Px(4), 0), WrapMode = DataGridViewTriState.False,
            };
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = g.AllowUserToDeleteRows = g.AllowUserToResizeRows = false;
            g.AllowUserToOrderColumns = false;
            g.RowTemplate.Height = Px(30);
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.EditMode = DataGridViewEditMode.EditProgrammatically;
            typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(g, true, null);
            DarkScroll(g);
            foreach (Control c in g.Controls) if (c is ScrollBar) DarkScroll(c);      // the grid's own scrollbars
        }

        public static string Fit(Graphics g, Font f, string text, int w)
        {
            if (w <= 10) return "";
            if (TextRenderer.MeasureText(g, text, f, Size.Empty, TextFormatFlags.NoPadding).Width <= w) return text;
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (TextRenderer.MeasureText(g, text.Substring(0, mid) + "…", f, Size.Empty, TextFormatFlags.NoPadding).Width <= w) lo = mid;
                else hi = mid - 1;
            }
            return text.Substring(0, lo) + "…";
        }
    }

    // ---------------------------------------------------------------- base forms
    class DarkForm : Form
    {
        public DarkForm()
        {
            BackColor = Th.Bg;
            ForeColor = Th.Fg;
            Font = Th.Base;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Th.DarkTitle(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            AlignRows(this);
            base.OnLoad(e);
        }

        /// Every box / button / label sitting in a table row is centred vertically with the same
        /// margins, so a text box and its Browse button line up exactly.
        public static void AlignRows(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (root is TableLayoutPanel t && t.GetColumnSpan(c) == 1 && !(c is TableLayoutPanel) && !(c is Seg) && c.Dock == DockStyle.None)
                {
                    c.Anchor = c is Field ? AnchorStyles.Left | AnchorStyles.Right : AnchorStyles.Left;
                    c.Margin = new Padding(c.Margin.Left, Th.Px(3), c.Margin.Right, Th.Px(3));
                }
                AlignRows(c);
            }
        }
    }

    /// Dialog: title, scrollable card body, fixed footer (buttons never scroll away)
    class Dlg : DarkForm
    {
        public readonly Panel Body;
        public readonly FlowLayoutPanel Buttons, LeftButtons;
        public readonly Panel Footer;

        public Dlg(Form owner, string title, int w, int h)
        {
            Text = title;
            Owner = owner;
            if (owner != null) Icon = owner.Icon;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            var scr = Screen.FromControl(owner ?? (Control)this).WorkingArea;
            int W = Math.Min(Th.Px(w), scr.Width - 40), H = Math.Min(Th.Px(h), scr.Height - 40);
            Size = new Size(W, H);
            MinimumSize = new Size(Math.Min(Th.Px(420), W), Math.Min(Th.Px(280), H));
            if (owner != null)
                Location = new Point(Math.Max(scr.Left, Math.Min(owner.Left + Th.Px(80), scr.Right - W)),
                                     Math.Max(scr.Top, Math.Min(owner.Top + Th.Px(60), scr.Bottom - H)));
            Padding = new Padding(Th.Px(14));

            Footer = new Panel { Dock = DockStyle.Bottom, Height = Th.Px(46), Padding = new Padding(0, Th.Px(10), 0, 0) };
            Buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
            LeftButtons = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false };
            Footer.Controls.Add(Buttons);
            Footer.Controls.Add(LeftButtons);

            var card = new Card { Dock = DockStyle.Fill };
            Body = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Th.Panel, Padding = new Padding(Th.Px(14)) };
            Th.DarkScroll(Body);
            card.Controls.Add(Body);

            var titleLbl = new Label { Text = title, Font = Th.Title, ForeColor = Th.Fg, Dock = DockStyle.Top, Height = Th.Px(40), UseMnemonic = false };
            Controls.Add(card);
            Controls.Add(Footer);
            Controls.Add(titleLbl);
        }

        public Button AddButton(string text, EventHandler click, bool accent = false)
        {
            var b = Th.Btn(text, click, accent);
            b.Margin = new Padding(Th.Px(6), 0, 0, 0);
            Buttons.Controls.Add(b);
            return b;
        }

        public Button AddLeftButton(string text, EventHandler click)
        {
            var b = Th.Btn(text, click);
            b.Margin = new Padding(0, 0, Th.Px(6), 0);
            LeftButtons.Controls.Add(b);
            return b;
        }
    }

    /// Bordered, filled panel
    class Card : Panel
    {
        public Card()
        {
            BackColor = Th.Panel;
            Padding = new Padding(1);
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Th.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// Text entry with a dark border
    class Field : Panel
    {
        public readonly TextBox Box;
        bool focused;

        public Field(string text = "", bool password = false)
        {
            BackColor = Th.Field;
            Height = Th.Px(32);                                       // same height as a button
            Anchor = AnchorStyles.Left | AnchorStyles.Right;          // in table rows: full width, fixed height
            Padding = new Padding(Th.Px(8), Th.Px(8), Th.Px(6), Th.Px(4));
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Th.Field, ForeColor = Th.Fg, Font = Th.Base,
                Dock = DockStyle.Fill, Text = text, UseSystemPasswordChar = password,
            };
            Box.GotFocus += (s, e) => { focused = true; Invalidate(); };
            Box.LostFocus += (s, e) => { focused = false; Invalidate(); };
            Controls.Add(Box);
            ResizeRedraw = true;
            Cursor = Cursors.IBeam;
            Click += (s, e) => Box.Focus();
        }

        public override string Text { get => Box.Text; set => Box.Text = value; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(focused ? Th.Accent : Th.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// Segmented control (tabs / options)
    class Seg : Control
    {
        readonly List<(string key, string label)> items;
        readonly Dictionary<string, string> labels = new Dictionary<string, string>();
        string value;
        int hover = -1;
        public event Action Changed;
        public Color Back;

        public Seg(IEnumerable<(string, string)> items, string value, Color back)
        {
            this.items = items.ToList();
            this.value = value;
            Back = back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Th.Base;
            Height = Th.Px(32);
            Cursor = Cursors.Hand;
            Relayout();
        }

        public string Value
        {
            get => value;
            set { if (this.value != value) { this.value = value; Invalidate(); Changed?.Invoke(); } }
        }

        public void SetLabel(string key, string text) { labels[key] = text; Relayout(); Invalidate(); }
        string Label(int i) => labels.TryGetValue(items[i].key, out var l) ? l : items[i].label;

        List<int> widths = new List<int>();

        void Relayout()
        {
            widths.Clear();
            using (var g = CreateGraphics())
                for (int i = 0; i < items.Count; i++)
                    widths.Add(TextRenderer.MeasureText(g, Label(i), Th.Bold).Width + Th.Px(28));
            Width = widths.Sum() + Th.Px(8);
        }

        int HitIndex(int x)
        {
            int cx = Th.Px(4);
            for (int i = 0; i < widths.Count; i++) { if (x >= cx && x < cx + widths[i]) return i; cx += widths[i]; }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitIndex(e.X); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { int i = HitIndex(e.X); if (i >= 0) Value = items[i].key; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Back);
            using (var path = Th.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Th.Px(9)))
            using (var br = new SolidBrush(Th.Field)) { g.FillPath(br, path); using (var p = new Pen(Th.Border)) g.DrawPath(p, path); }
            int cx = Th.Px(4);
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rectangle(cx, Th.Px(4), widths[i], Height - Th.Px(8));
                bool on = items[i].key == value;
                if (on || i == hover)
                    using (var path = Th.Round(r, Th.Px(7)))
                    using (var br = new SolidBrush(on ? Th.Accent : Th.Hover)) g.FillPath(br, path);
                TextRenderer.DrawText(g, Label(i), on ? Th.Bold : Th.Base, r, on ? Color.White : Th.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                cx += widths[i];
            }
        }
    }

    /// Tick box + text
    class Check : Control
    {
        bool on;
        public event Action Changed;
        public Color Back;

        public Check(string text, bool value, Color back)
        {
            Text = text; on = value; Back = back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Font = Th.Base;
            Cursor = Cursors.Hand;
            using (var g = CreateGraphics())
                Size = new Size(TextRenderer.MeasureText(g, text, Font).Width + Th.Px(28), Th.Px(26));
        }

        public bool Checked { get => on; set { if (on != value) { on = value; Invalidate(); Changed?.Invoke(); } } }
        protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) Checked = !Checked; }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Back);
            var img = Th.Box(on && Enabled ? "on" : "off", Back);
            e.Graphics.DrawImage(img, 0, (Height - img.Height) / 2);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(img.Width + Th.Px(8), 0, Width, Height),
                Enabled ? Th.Fg : Th.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// Radio option (round)
    class Radio : Control
    {
        public string Key;
        public bool On;
        public event Action<string> Picked;
        public Color Back;

        public Radio(string key, string text, bool on, Color back)
        {
            Key = key; Text = text; On = on; Back = back;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Font = Th.Base;
            Cursor = Cursors.Hand;
            using (var g = CreateGraphics())
                Size = new Size(TextRenderer.MeasureText(g, text, Font).Width + Th.Px(30), Th.Px(24));
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Picked?.Invoke(Key); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Th.Px(15), y = (Height - d) / 2;
            using (var br = new SolidBrush(On ? Th.Accent : Th.Field)) g.FillEllipse(br, 1, y, d, d);
            if (!On) using (var p = new Pen(Th.BoxBorder, Th.Px(1.4))) g.DrawEllipse(p, 1, y, d, d);
            else using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, 1 + d * 0.32f, y + d * 0.32f, d * 0.36f, d * 0.36f);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(d + Th.Px(9), 0, Width, Height), Th.Fg,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// Group of Radio controls bound to one value
    class RadioGroup
    {
        readonly List<Radio> radios = new List<Radio>();
        public string Value { get; private set; }
        public event Action Changed;

        public RadioGroup(string value) { Value = value; }

        public Radio Add(string key, string text, Color back)
        {
            var r = new Radio(key, text, key == Value, back);
            r.Picked += k => Set(k);
            radios.Add(r);
            return r;
        }

        public void Set(string k)
        {
            if (k == Value) return;
            Value = k;
            foreach (var r in radios) { r.On = r.Key == k; r.Invalidate(); }
            Changed?.Invoke();
        }
    }

    class Bar : Control
    {
        double value;
        bool busy;
        readonly Timer t = new Timer { Interval = 30 };
        float phase;

        public Bar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Th.Px(6);
            t.Tick += (s, e) => { phase = (phase + 0.02f) % 1.4f; Invalidate(); };
        }

        public double Value { get => value; set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); } }
        public bool Busy { get => busy; set { busy = value; t.Enabled = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Th.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Th.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
            using (var br = new SolidBrush(Th.Field)) g.FillPath(br, p);
            float x0 = 0, w = (float)(Width * value / 100);
            if (busy) { w = Width * 0.3f; x0 = (phase - 0.3f) * Width; }
            if (w > 1)
            {
                var r = new RectangleF(Math.Max(0, x0), 0, Math.Min(w, Width - Math.Max(0, x0)) - 1, Height - 1);
                if (r.Width > 1)
                    using (var p = Th.Round(r, Height / 2f))
                    using (var br = new SolidBrush(Th.Accent)) g.FillPath(br, p);
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) t.Dispose(); base.Dispose(disposing); }
    }

    class LogBox : RichTextBox
    {
        public LogBox()
        {
            ReadOnly = true;
            BackColor = Th.Bg;
            ForeColor = Th.Muted;
            BorderStyle = BorderStyle.None;
            Font = Th.Mono;
            DetectUrls = false;
            Th.DarkScroll(this);
        }

        public void Write(string text, string tag = null)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => Write(text, tag))); return; }
            SelectionStart = TextLength;
            SelectionLength = 0;
            SelectionColor = tag == "ok" ? Th.Ok : tag == "err" ? Th.Err : tag == "head" ? Th.Fg : Th.Muted;
            AppendText(text + "\n");
            ScrollToCaret();
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Th.Sel;
        public override Color MenuItemBorder => Th.Sel;
        public override Color MenuBorder => Th.Border;
        public override Color ToolStripDropDownBackground => Th.Field;
        public override Color ImageMarginGradientBegin => Th.Field;
        public override Color ImageMarginGradientMiddle => Th.Field;
        public override Color ImageMarginGradientEnd => Th.Field;
        public override Color SeparatorDark => Th.Border;
        public override Color SeparatorLight => Th.Field;
        public override Color MenuItemSelectedGradientBegin => Th.Sel;
        public override Color MenuItemSelectedGradientEnd => Th.Sel;
    }

    class DarkMenu : ContextMenuStrip
    {
        public DarkMenu()
        {
            Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };
            BackColor = Th.Field;
            ForeColor = Th.Fg;
            Font = Th.Base;
            ShowImageMargin = false;
        }

        public void Add(string text, Action a) =>
            Items.Add(new ToolStripMenuItem(text, null, (s, e) => a()) { ForeColor = Th.Fg, Padding = new Padding(Th.Px(4), Th.Px(3), Th.Px(4), Th.Px(3)) });

        public void Sep() => Items.Add(new ToolStripSeparator());
    }

    static class Ask
    {
        /// Small text input dialog; null = cancelled
        public static string Text(Form owner, string title, string prompt, string initial)
        {
            using (var d = new Dlg(owner, title, 520, 230))
            {
                var lbl = Th.Lbl(prompt, Th.Fg, Th.Base, Th.Panel);
                lbl.Dock = DockStyle.Top;
                var f = new Field(initial) { Dock = DockStyle.Top };
                var gap = new Panel { Dock = DockStyle.Top, Height = Th.Px(8), BackColor = Th.Panel };
                d.Body.Controls.Add(f); d.Body.Controls.Add(gap); d.Body.Controls.Add(lbl);
                string result = null;
                var ok = d.AddButton("OK", (s, e) => { result = f.Text; d.Close(); }, true);
                d.AddButton("Cancel", (s, e) => d.Close());
                d.AcceptButton = ok;
                d.Shown += (s, e) => { f.Box.Focus(); f.Box.SelectAll(); };
                d.ShowDialog(owner);
                return result;
            }
        }

        public static bool YesNo(IWin32Window owner, string msg) =>
            MessageBox.Show(owner, msg, "SubPicker", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        public static void Info(IWin32Window owner, string msg) => MessageBox.Show(owner, msg, "SubPicker", MessageBoxButtons.OK, MessageBoxIcon.Information);
        public static void Warn(IWin32Window owner, string msg) => MessageBox.Show(owner, msg, "SubPicker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        public static void Error(IWin32Window owner, string msg) => MessageBox.Show(owner, msg, "SubPicker", MessageBoxButtons.OK, MessageBoxIcon.Error);

        /// Folder picker using the same modern Explorer window as the file picker (IFileOpenDialog + FOS_PICKFOLDERS)
        public static string Folder(IWin32Window owner, string title, string start)
        {
            try
            {
                var dlg = (FolderPicker.IFileOpenDialog)new FolderPicker.FileOpenDialogRCW();
                try
                {
                    dlg.GetOptions(out uint opts);
                    dlg.SetOptions(opts | 0x20 | 0x40 | 0x800);                 // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST
                    dlg.SetTitle(title);
                    string dir = start;
                    while (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) dir = System.IO.Path.GetDirectoryName(dir);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var iid = typeof(FolderPicker.IShellItem).GUID;
                        if (FolderPicker.SHCreateItemFromParsingName(dir, IntPtr.Zero, ref iid, out var item) == 0) dlg.SetFolder(item);
                    }
                    if (dlg.Show(owner?.Handle ?? IntPtr.Zero) != 0) return null;           // cancelled
                    dlg.GetResult(out var res);
                    res.GetDisplayName(0x80058000, out string path);                          // SIGDN_FILESYSPATH
                    return path;
                }
                finally { Marshal.ReleaseComObject(dlg); }
            }
            catch (COMException)
            {
                using (var f = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true })
                {
                    if (!string.IsNullOrEmpty(start) && System.IO.Directory.Exists(start)) f.SelectedPath = start;
                    return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
                }
            }
        }

        public static string File(IWin32Window owner, string title, string filter, string startDir = null)
        {
            using (var f = new OpenFileDialog { Title = title, Filter = filter })
            {
                if (!string.IsNullOrEmpty(startDir) && System.IO.Directory.Exists(startDir)) f.InitialDirectory = startDir;
                return f.ShowDialog(owner) == DialogResult.OK ? f.FileName : null;
            }
        }
    }
}
