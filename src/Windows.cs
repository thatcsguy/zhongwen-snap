using System;
using System.Collections.Generic;
using System.Drawing;
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
            selection = Rectangle.FromLTRB(Math.Max(0, Math.Min(a.X, e.X)), Math.Max(0, Math.Min(a.Y, e.Y)),
                Math.Min(ClientSize.Width, Math.Max(a.X, e.X)), Math.Min(ClientSize.Height, Math.Max(a.Y, e.Y)));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!start.HasValue || e.Button != MouseButtons.Left) return;
            Capture = false;
            start = null;
            if (selection.Width < 20 || selection.Height < 12)
            {
                selection = Rectangle.Empty;
                Invalidate();
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
        private readonly Timer dismissTimer = new Timer();
        private readonly Rectangle captureBounds;
        private readonly TranslationResult result;
        private readonly Action openHistory;
        private int nextY;

        public ResultPopup(Rectangle captureBounds, string title, string message,
            TranslationResult result, Action openHistory)
        {
            this.captureBounds = captureBounds;
            this.result = result;
            this.openHistory = openHistory;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(32, 44, 65);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(460, 100);
            var header = new Label { Text = title, Location = new Point(15, 11), Size = new Size(390, 24),
                Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(88, 224, 178) };
            Controls.Add(header);
            var close = new Button { Text = "×", Location = new Point(420, 5), Size = new Size(32, 31),
                FlatStyle = FlatStyle.Flat, BackColor = BackColor, ForeColor = Color.White, TabStop = false };
            close.FlatAppearance.BorderSize = 0;
            close.Click += (s, e) => Close();
            Controls.Add(close);
            nextY = 39;
            if (result == null) AddText(message, 11, Color.White, 10);
            else
            {
                AddText(result.Text, 17, Color.White, 8);
                AddText(result.Pinyin, 11, Color.FromArgb(88, 224, 178), 6);
                AddText(result.English, 12, Color.White, 8);
                AddText(result.Note, 10, Color.FromArgb(190, 206, 222), 9);
                var copy = new Button { Text = "Copy", Location = new Point(15, nextY + 6), Size = new Size(65, 28) };
                copy.Click += (s, e) => { Clipboard.SetText(result.Combined()); };
                Controls.Add(copy);
                var history = new Button { Text = "History", Location = new Point(88, nextY + 6), Size = new Size(72, 28) };
                history.Click += (s, e) => { Close(); openHistory(); };
                Controls.Add(history);
                nextY += 38;
            }
            var desiredHeight = Math.Max(72, nextY + 13);
            var availableHeight = Math.Max(180, Screen.FromRectangle(captureBounds).WorkingArea.Height - 24);
            ClientSize = new Size(460, Math.Min(desiredHeight, availableHeight));
            AutoScrollMinSize = new Size(0, desiredHeight);
            Place();
            dismissTimer.Interval = 25000;
            dismissTimer.Tick += (s, e) => Close();
            Shown += (s, e) => { Activate(); dismissTimer.Start(); };
            FormClosed += (s, e) => dismissTimer.Dispose();
        }

        private void AddText(string text, int size, Color color, int gap)
        {
            if (String.IsNullOrWhiteSpace(text)) return;
            var font = new Font("Segoe UI", size);
            var measured = TextRenderer.MeasureText(text, font, new Size(425, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var label = new Label { Text = text, Location = new Point(15, nextY + gap),
                Size = new Size(430, Math.Max(measured.Height + 3, size + 10)),
                Font = font, ForeColor = color };
            Controls.Add(label);
            nextY = label.Bottom;
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
