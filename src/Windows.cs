using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly FollowUpConversation conversation;
        private readonly Label hint;
        private readonly Button historyButton;
        private readonly Button askButton;
        private readonly Panel composer;
        private readonly AskTextBox questionBox;
        private readonly Button sendButton;
        private readonly Button resetButton;
        private readonly Label chatHint;
        private readonly Font chatFont;
        private readonly Font captionFont;
        private readonly int translationHeight;
        private readonly int translationCards;
        private CancellationTokenSource requestCancellation;
        private Control errorCard;
        private bool chatOpen;
        private bool answering;
        private int contentY;
        private static readonly Color Canvas = Color.FromArgb(255, 249, 245);
        private static readonly Color Ink = Color.FromArgb(48, 55, 65);
        private static readonly Color Muted = Color.FromArgb(112, 120, 126);
        private static readonly Color Coral = Color.FromArgb(232, 112, 106);

        public ResultPopup(Rectangle captureBounds, string title, string message,
            TranslationResult result, Action openHistory, FollowUpConversation conversation = null)
        {
            this.captureBounds = captureBounds;
            this.conversation = conversation;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Canvas;
            ForeColor = Ink;
            Font = OwnFont("Segoe UI", 10);
            captionFont = OwnFont("Segoe UI", 8, FontStyle.Bold);
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

            translationHeight = contentY;
            translationCards = content.Controls.Count;
            chatFont = OwnFont("Microsoft JhengHei UI", 11);

            hint = new Label { Text = "Select text to copy  ·  Esc to close",
                Size = new Size(252, 24),
                ForeColor = Muted, Font = OwnFont("Segoe UI", 8) };
            Controls.Add(hint);
            if (result != null)
            {
                historyButton = new Button { Text = "History",
                    Size = new Size(90, 32), FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(225, 241, 235), ForeColor = Color.FromArgb(43, 104, 93),
                    Font = OwnFont("Segoe UI", 9, FontStyle.Bold), TabStop = false };
                historyButton.FlatAppearance.BorderSize = 0;
                historyButton.Click += (s, e) => { Close(); openHistory(); };
                Controls.Add(historyButton);
            }
            if (conversation != null)
            {
                askButton = ChatButton("Ask", "Ask", 82, 32);
                askButton.Click += (s, e) => OpenChat();
                Controls.Add(askButton);

                composer = new Panel { Name = "Composer", Size = new Size(444, 118), Visible = false, BackColor = Canvas };
                composer.Controls.Add(new Label { Text = "ASK ABOUT THIS TRANSLATION",
                    Location = Point.Empty, Size = new Size(340, 18), ForeColor = Coral,
                    Font = captionFont });
                questionBox = new AskTextBox { Name = "Question", Location = new Point(0, 24),
                    Size = new Size(348, 62), Multiline = true, AcceptsReturn = true,
                    WordWrap = true, ScrollBars = ScrollBars.Vertical, Font = chatFont,
                    MaxLength = FollowUpConversation.MaxQuestionLength, AccessibleName = "Question about this translation" };
                questionBox.SendRequested += SendQuestion;
                composer.Controls.Add(questionBox);
                sendButton = ChatButton("Send", "Send", 86, 32);
                sendButton.Location = new Point(358, 24);
                sendButton.Click += (s, e) => SendQuestion();
                composer.Controls.Add(sendButton);
                resetButton = ChatButton("ResetChat", "Reset chat", 86, 26);
                resetButton.Location = new Point(358, 61);
                resetButton.Font = OwnFont("Segoe UI", 8);
                resetButton.Click += (s, e) => ResetChat();
                composer.Controls.Add(resetButton);
                chatHint = new Label { Text = "Enter to send  ·  Shift+Enter for newline",
                    Location = new Point(0, 94), Size = new Size(444, 22), ForeColor = Muted,
                    Font = OwnFont("Segoe UI", 8) };
                composer.Controls.Add(chatHint);
                Controls.Add(composer);
            }
            LayoutPopup(false);
            Shown += (s, e) => Activate();
        }

        private Button ChatButton(string name, string text, int width, int height)
        {
            var button = new Button { Name = name, Text = text, Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(255, 224, 216), ForeColor = Ink,
                Font = OwnFont("Segoe UI", 9, FontStyle.Bold) };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void OpenChat()
        {
            chatOpen = true;
            askButton.Visible = false;
            composer.Visible = true;
            LayoutPopup(false);
            questionBox.Focus();
        }

        private async void SendQuestion()
        {
            if (answering || conversation == null || conversation.AtLimit) return;
            var question = questionBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(question)) return;
            ClearError();
            answering = true;
            questionBox.ReadOnly = true;
            sendButton.Enabled = resetButton.Enabled = false;
            chatHint.Text = "Answering…";
            var cancellation = new CancellationTokenSource();
            requestCancellation = cancellation;
            try
            {
                var answer = await Task.Run(() => conversation.Ask(question, cancellation.Token));
                if (IsDisposed || Disposing || cancellation.IsCancellationRequested) return;
                content.AutoScrollPosition = Point.Empty;
                AddField("YOU", question, chatFont, Ink, Color.FromArgb(225, 241, 235));
                AddField("ANSWER", answer, chatFont, Ink, Color.White);
                questionBox.Clear();
                LayoutPopup(true);
            }
            catch (OperationCanceledException) { /* Closing the popup abandons its conversation. */ }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing || cancellation.IsCancellationRequested) return;
                content.AutoScrollPosition = Point.Empty;
                errorCard = AddField("COULD NOT ANSWER", ex.Message, chatFont, Ink, Color.FromArgb(255, 238, 231));
                LayoutPopup(true);
            }
            finally
            {
                if (!IsDisposed && !Disposing && !cancellation.IsCancellationRequested)
                {
                    answering = false;
                    questionBox.ReadOnly = conversation.AtLimit;
                    sendButton.Enabled = !conversation.AtLimit;
                    resetButton.Enabled = true;
                    chatHint.Text = conversation.AtLimit ? "Chat limit reached  ·  Reset chat to continue" :
                        "Enter to send  ·  Shift+Enter for newline";
                    questionBox.Focus();
                }
                requestCancellation = null;
                cancellation.Dispose();
            }
        }

        private void ClearError()
        {
            if (errorCard == null) return;
            content.AutoScrollPosition = Point.Empty;
            contentY -= errorCard.Height + 8;
            content.Controls.Remove(errorCard);
            errorCard.Dispose();
            errorCard = null;
            LayoutPopup(false);
        }

        private void ResetChat()
        {
            if (answering) return;
            conversation.Reset();
            content.AutoScrollPosition = Point.Empty;
            while (content.Controls.Count > translationCards)
            {
                var card = content.Controls[content.Controls.Count - 1];
                content.Controls.Remove(card);
                card.Dispose();
            }
            errorCard = null;
            contentY = translationHeight;
            questionBox.Clear();
            questionBox.ReadOnly = false;
            sendButton.Enabled = true;
            chatHint.Text = "Enter to send  ·  Shift+Enter for newline";
            LayoutPopup(false);
            questionBox.Focus();
        }

        private void LayoutPopup(bool scrollToLatest)
        {
            var availableHeight = Math.Max(1, Screen.FromRectangle(captureBounds).WorkingArea.Height - 24);
            var composerHeight = chatOpen ? 128 : 0;
            var desiredHeight = chatOpen ? Math.Min(480, Math.Max(280, contentY)) : contentY;
            content.Height = Math.Min(desiredHeight, Math.Max(1, availableHeight - 118 - composerHeight));
            content.AutoScrollMinSize = new Size(0, contentY);
            if (composer != null) composer.Location = new Point(18, content.Bottom + 10);
            var footerTop = content.Bottom + composerHeight;
            hint.Location = new Point(20, footerTop + 17);
            if (historyButton != null) historyButton.Location = new Point(372, footerTop + 10);
            if (askButton != null) askButton.Location = new Point(280, footerTop + 10);
            ClientSize = new Size(480, footerTop + 57);
            Place();
            if (scrollToLatest) content.AutoScrollPosition = new Point(0, contentY);
        }

        private Control AddField(string caption, string value, Font font, Color color, Color fill)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            // Native Windows textboxes need CRLF, and cards must leave room for a vertical scrollbar.
            value = value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            var cardWidth = content.Width - 18 - SystemInformation.VerticalScrollBarWidth;
            var measured = TextRenderer.MeasureText(value, font, new Size(cardWidth - 32, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            var textHeight = Math.Max(measured.Height + 8, font.Height + 8);
            var card = new PopupCard(fill) { Location = new Point(9, contentY),
                Size = new Size(cardWidth, textHeight + 48) };
            var label = new Label { Text = caption, Location = new Point(14, 10), Size = new Size(cardWidth - 28, 18),
                Font = captionFont, ForeColor = Coral, BackColor = fill };
            card.Controls.Add(label);
            var text = new TextBox { Text = value, Location = new Point(14, 32),
                Size = new Size(cardWidth - 28, textHeight), Multiline = true, ReadOnly = true,
                BorderStyle = BorderStyle.None, WordWrap = true, ScrollBars = ScrollBars.None,
                BackColor = fill, ForeColor = color, Font = font, Cursor = Cursors.IBeam };
            card.Controls.Add(text);
            content.Controls.Add(card);
            contentY += card.Height + 8;
            return card;
        }

        private Font OwnFont(string family, float size, FontStyle style = FontStyle.Regular)
        {
            var font = new Font(family, size, style);
            popupFonts.Add(font);
            return font;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && requestCancellation != null) requestCancellation.Cancel();
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
            if (questionBox != null && questionBox.Focused && questionBox.IsComposing)
                return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal sealed class AskTextBox : TextBox
    {
        public event Action SendRequested;
        internal bool IsComposing { get; private set; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x010D) IsComposing = true; // WM_IME_STARTCOMPOSITION
            if (message.Msg == 0x010E) IsComposing = false; // WM_IME_ENDCOMPOSITION
            base.WndProc(ref message);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyData == Keys.Enter && !IsComposing)
            {
                e.SuppressKeyPress = true;
                if (SendRequested != null) SendRequested();
                return;
            }
            base.OnKeyDown(e);
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
