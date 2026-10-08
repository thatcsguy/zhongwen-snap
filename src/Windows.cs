using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZhongWenSnap
{
    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap screenshot;
        private Point? start;
        private Rectangle selection;
        public Rectangle SelectedScreenBounds { get; private set; }

        public CaptureOverlay(Bitmap screenshot, Rectangle virtualBounds)
        {
            this.screenshot = screenshot;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Bounds = virtualBounds;
            Cursor = Cursors.Cross;
            DoubleBuffered = true;
            BackColor = Color.Black;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImageUnscaled(screenshot, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(50, 8, 18, 30)))
                e.Graphics.FillRectangle(shade, ClientRectangle);
            using (var bar = new SolidBrush(Color.FromArgb(235, 22, 32, 51)))
                e.Graphics.FillRectangle(bar, 0, 0, Math.Min(ClientSize.Width, 620), 49);
            using (var font = new Font("Segoe UI", 13, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "Drag around the text  •  Esc to cancel",
                    font, new Point(14, 10), Color.White);
            if (selection.Width > 0 && selection.Height > 0)
            {
                e.Graphics.DrawImage(screenshot, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(60, 226, 176), 3))
                    e.Graphics.DrawRectangle(pen, selection);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            start = e.Location;
            selection = Rectangle.Empty;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!start.HasValue) return;
            var a = start.Value;
            var next = Rectangle.FromLTRB(Math.Max(0, Math.Min(a.X, e.X)), Math.Max(0, Math.Min(a.Y, e.Y)),
                Math.Min(ClientSize.Width, Math.Max(a.X, e.X)), Math.Min(ClientSize.Height, Math.Max(a.Y, e.Y)));
            if (selection == next) return;
            var previous = selection;
            selection = next;
            InvalidateSelectionChange(previous, next);
        }

        private void InvalidateSelectionChange(Rectangle previous, Rectangle next)
        {
            // The screenshot and shade only change where the selection enters or leaves.
            // Repaint both outlines too, since their strokes can cross unchanged pixels.
            using (var dirty = new Region())
            {
                dirty.MakeEmpty();
                if (previous.Width > 0 && previous.Height > 0) dirty.Union(previous);
                if (next.Width > 0 && next.Height > 0) dirty.Xor(next);
                AddOutline(dirty, previous);
                AddOutline(dirty, next);
                Invalidate(dirty);
            }
        }

        private static void AddOutline(Region dirty, Rectangle box)
        {
            if (box.Width < 1 || box.Height < 1) return;
            const int margin = 2; // A 3-pixel pen extends beyond the selection bounds.
            dirty.Union(new Rectangle(box.Left - margin, box.Top - margin, box.Width + 2 * margin, 2 * margin + 1));
            dirty.Union(new Rectangle(box.Left - margin, box.Bottom - margin, box.Width + 2 * margin, 2 * margin + 1));
            dirty.Union(new Rectangle(box.Left - margin, box.Top - margin, 2 * margin + 1, box.Height + 2 * margin));
            dirty.Union(new Rectangle(box.Right - margin, box.Top - margin, 2 * margin + 1, box.Height + 2 * margin));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!start.HasValue || e.Button != MouseButtons.Left) return;
            Capture = false;
            start = null;
            if (selection.Width < 20 || selection.Height < 12)
            {
                var previous = selection;
                selection = Rectangle.Empty;
                InvalidateSelectionChange(previous, selection);
                return;
            }
            SelectedScreenBounds = new Rectangle(Left + selection.Left, Top + selection.Top,
                selection.Width, selection.Height);
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else base.OnKeyDown(e);
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly TextBox keyBox;
        private readonly TextBox hotkeyBox;
        private readonly TextBox modelBox;
        private readonly TextBox promptBox;
        private readonly CheckBox startupBox;
        private readonly Func<AppSettings, string, string> save;

        public SettingsForm(AppSettings current, string currentKey, Func<AppSettings, string, string> save)
        {
            this.save = save;
            Text = "ZhongWen Snap settings";
            ClientSize = new Size(600, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            Padding = new Padding(18);
            var title = new Label { Text = "Settings", Font = new Font("Segoe UI", 18, FontStyle.Bold),
                Location = new Point(20, 17), AutoSize = true };
            Controls.Add(title);

            Controls.Add(LabelAt("OpenAI API key", 20, 65));
            keyBox = BoxAt(20, 88, 550, false);
            keyBox.UseSystemPasswordChar = true;
            keyBox.Text = currentKey;
            Controls.Add(keyBox);
            var reveal = new CheckBox { Text = "Show key", Location = new Point(20, 119), AutoSize = true };
            reveal.CheckedChanged += (s, e) => keyBox.UseSystemPasswordChar = !reveal.Checked;
            Controls.Add(reveal);
            Controls.Add(new Label { Text = "Saved encrypted for this Windows account; each user enters a key once.",
                Location = new Point(121, 120), Size = new Size(450, 24), ForeColor = Color.DimGray });

            Controls.Add(LabelAt("Global capture shortcut", 20, 152));
            hotkeyBox = BoxAt(20, 176, 230, false);
            hotkeyBox.Text = current.Hotkey;
            Controls.Add(hotkeyBox);
            Controls.Add(new Label { Text = "Example: Ctrl+Alt+T", Location = new Point(264, 180),
                Size = new Size(250, 24), ForeColor = Color.DimGray });

            Controls.Add(LabelAt("Model", 20, 213));
            modelBox = BoxAt(20, 237, 230, false);
            modelBox.Text = current.Model;
            Controls.Add(modelBox);

            Controls.Add(LabelAt("What should the app tell you?", 20, 276));
            Controls.Add(new Label { Text = "Customize translation style, vocabulary, grammar notes, and length.",
                Location = new Point(20, 300), Size = new Size(550, 24), ForeColor = Color.DimGray });
            promptBox = BoxAt(20, 329, 550, true);
            promptBox.Height = 128;
            promptBox.AcceptsReturn = true;
            promptBox.Text = current.Prompt;
            Controls.Add(promptBox);

            startupBox = new CheckBox { Text = "Start when I sign in to Windows", Location = new Point(20, 472),
                AutoSize = true, Checked = current.StartWithWindows };
            Controls.Add(startupBox);

            var cancelButton = new Button { Text = "Cancel", Location = new Point(379, 511), Size = new Size(88, 32) };
            cancelButton.Click += (s, e) => Close();
            Controls.Add(cancelButton);
            var saveButton = new Button { Text = "Save", Location = new Point(482, 511), Size = new Size(88, 32) };
            saveButton.Click += SaveClicked;
            Controls.Add(saveButton);
            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            var candidate = new AppSettings {
                Hotkey = hotkeyBox.Text.Trim(), Model = modelBox.Text.Trim(),
                Prompt = promptBox.Text.Trim(), StartWithWindows = startupBox.Checked
            };
            if (String.IsNullOrWhiteSpace(candidate.Model) || String.IsNullOrWhiteSpace(candidate.Prompt))
            {
                MessageBox.Show(this, "Model and prompt cannot be empty.", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var error = save(candidate, keyBox.Text.Trim());
            if (!String.IsNullOrEmpty(error))
                MessageBox.Show(this, error, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else Close();
        }

        private static Label LabelAt(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        }

        private static TextBox BoxAt(int x, int y, int width, bool multiline)
        {
            return new TextBox { Location = new Point(x, y), Width = width, Height = multiline ? 120 : 27,
                Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
        }
    }

    internal sealed class ResultPopup : Form
    {
        private readonly Rectangle captureBounds;
        private readonly Panel content;
        private readonly List<Font> popupFonts = new List<Font>();
        private int contentY;
        private static readonly Color Canvas = Color.FromArgb(255, 249, 245);
        private static readonly Color Ink = Color.FromArgb(48, 55, 65);
        private static readonly Color Muted = Color.FromArgb(112, 120, 126);
        private static readonly Color Coral = Color.FromArgb(232, 112, 106);

        public ResultPopup(Rectangle captureBounds, string title, string message,
            TranslationResult result, Action openHistory)
        {
            this.captureBounds = captureBounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Canvas;
            ForeColor = Ink;
            Font = OwnFont("Segoe UI", 10);
            ClientSize = new Size(480, 160);

            var badge = new PopupBadge { Location = new Point(18, 13), Size = new Size(36, 36) };
            Controls.Add(badge);
            var header = new Label { Text = title == "ZHONGWEN SNAP" ? "Your translation" : title,
                Location = new Point(64, 15), Size = new Size(365, 25),
                Font = OwnFont("Segoe UI", 11, FontStyle.Bold), ForeColor = Ink };
            Controls.Add(header);
            var close = new Button { Text = "×", Location = new Point(435, 12), Size = new Size(30, 30),
                FlatStyle = FlatStyle.Flat, BackColor = Canvas, ForeColor = Muted, TabStop = false,
                Font = OwnFont("Segoe UI", 13) };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (s, e) => Close();
            Controls.Add(close);

            content = new Panel { Location = new Point(18, 61), Width = 444,
                AutoScroll = true, BackColor = Canvas, TabStop = false };
            Controls.Add(content);
            if (result == null)
                AddField("STATUS", String.IsNullOrWhiteSpace(message) ? "Just a moment…" : message,
                    OwnFont("Segoe UI", 11), Ink, Color.White);
            else
            {
                AddField("CHINESE", result.Text, OwnFont("Microsoft JhengHei UI", 17, FontStyle.Bold), Ink, Color.White);
                AddField("PINYIN", result.Pinyin, OwnFont("Segoe UI", 11), Color.FromArgb(48, 120, 111), Color.White);
                AddField("ENGLISH", result.English, OwnFont("Segoe UI", 12), Ink, Color.White);
                AddField("LEARNER NOTE", result.Note, OwnFont("Segoe UI", 10), Ink,
                    Color.FromArgb(255, 238, 231));
            }

            var availableHeight = Math.Max(190, Screen.FromRectangle(captureBounds).WorkingArea.Height - 24);
            content.Height = Math.Min(contentY, Math.Max(75, availableHeight - 118));
            ClientSize = new Size(480, content.Bottom + 57);
            content.AutoScrollMinSize = new Size(0, contentY);

            var hint = new Label { Text = "Select text to copy  ·  Esc to close",
                Location = new Point(20, content.Bottom + 17), Size = new Size(330, 24),
                ForeColor = Muted, Font = OwnFont("Segoe UI", 8) };
            Controls.Add(hint);
            if (result != null)
            {
                var history = new Button { Text = "History", Location = new Point(372, content.Bottom + 10),
                    Size = new Size(90, 32), FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(225, 241, 235), ForeColor = Color.FromArgb(43, 104, 93),
                    Font = OwnFont("Segoe UI", 9, FontStyle.Bold), TabStop = false };
                history.FlatAppearance.BorderSize = 0;
                history.Click += (s, e) => { Close(); openHistory(); };
                Controls.Add(history);
            }
            Place();
            Shown += (s, e) => Activate();
        }

        private void AddField(string caption, string value, Font font, Color color, Color fill)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            var measured = TextRenderer.MeasureText(value, font, new Size(394, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var textHeight = Math.Max(measured.Height + 8, font.Height + 8);
            var card = new PopupCard(fill) { Location = new Point(9, contentY),
                Size = new Size(426, textHeight + 48) };
            var label = new Label { Text = caption, Location = new Point(14, 10), Size = new Size(394, 18),
                Font = OwnFont("Segoe UI", 8, FontStyle.Bold), ForeColor = Coral, BackColor = fill };
            card.Controls.Add(label);
            var text = new TextBox { Text = value, Location = new Point(14, 32),
                Size = new Size(398, textHeight), Multiline = true, ReadOnly = true,
                BorderStyle = BorderStyle.None, WordWrap = true, ScrollBars = ScrollBars.None,
                BackColor = fill, ForeColor = color, Font = font, Cursor = Cursors.IBeam };
            card.Controls.Add(text);
            content.Controls.Add(card);
            contentY += card.Height + 8;
        }

        private Font OwnFont(string family, float size, FontStyle style = FontStyle.Regular)
        {
            var font = new Font(family, size, style);
            popupFonts.Add(font);
            return font;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                foreach (var font in popupFonts) font.Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var border = new Pen(Color.FromArgb(232, 219, 212)))
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private void Place()
        {
            var working = Screen.FromRectangle(captureBounds).WorkingArea;
            var x = captureBounds.Left + captureBounds.Width / 2 - Width / 2;
            var y = captureBounds.Top - Height - 12;
            if (y < working.Top) y = captureBounds.Bottom + 12;
            x = Math.Max(working.Left, Math.Min(x, working.Right - Width));
            y = Math.Max(working.Top, Math.Min(y, working.Bottom - Height));
            Location = new Point(x, y);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal sealed class PopupBadge : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(Color.FromArgb(232, 112, 106)))
                e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            using (var font = new Font("Microsoft JhengHei UI", 14, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "中", font, ClientRectangle, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class PopupCard : Panel
    {
        private readonly Color fill;

        public PopupCard(Color fill)
        {
            this.fill = fill;
            BackColor = Color.FromArgb(255, 249, 245);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                int right = Width - 1, bottom = Height - 1;
                path.AddArc(0, 0, 18, 18, 180, 90);
                path.AddArc(right - 18, 0, 18, 18, 270, 90);
                path.AddArc(right - 18, bottom - 18, 18, 18, 0, 90);
                path.AddArc(0, bottom - 18, 18, 18, 90, 90);
                path.CloseFigure();
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                using (var border = new Pen(Color.FromArgb(241, 227, 220))) e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly List<TranslationResult> entries;
        private readonly ListBox list;
        private readonly TextBox details;

        public HistoryForm()
        {
            Text = "ZhongWen Snap history";
            ClientSize = new Size(660, 440);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            entries = HistoryStore.Recent();
            list = new ListBox { Location = new Point(14, 14), Size = new Size(630, 210), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            foreach (var item in entries)
                list.Items.Add(item.CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm") + "   " + item.Text);
            list.SelectedIndexChanged += (s, e) => ShowSelected();
            Controls.Add(list);
            details = new TextBox { Location = new Point(14, 237), Size = new Size(630, 145), Multiline = true,
                ReadOnly = true, ScrollBars = ScrollBars.Vertical, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(details);
            var clear = new Button { Text = "Clear history", Location = new Point(523, 395), Size = new Size(120, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            clear.Click += (s, e) => {
                if (MessageBox.Show(this, "Delete all saved translations?", "Clear history",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { HistoryStore.Clear(); entries.Clear(); list.Items.Clear(); details.Clear(); }
            };
            Controls.Add(clear);
            if (entries.Count > 0) list.SelectedIndex = 0;
        }

        private void ShowSelected()
        {
            details.Text = list.SelectedIndex >= 0 ? entries[list.SelectedIndex].Combined() : "";
        }
    }
}
