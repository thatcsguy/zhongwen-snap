using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZhongWenSnap
{
    internal static class AppTheme
    {
        public static readonly Color Canvas = Color.FromArgb(23, 27, 32);
        public static readonly Color Surface = Color.FromArgb(34, 40, 47);
        public static readonly Color Border = Color.FromArgb(54, 63, 73);
        public static readonly Color Ink = Color.FromArgb(237, 241, 245);
        public static readonly Color Muted = Color.FromArgb(171, 181, 193);
        public static readonly Color Accent = Color.FromArgb(128, 207, 188);
        public static readonly Color AccentSurface = Color.FromArgb(38, 62, 56);
        public static readonly Color NoteSurface = Color.FromArgb(47, 43, 42);
        public static readonly Color Coral = Color.FromArgb(243, 155, 142);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string theme, string subId);

        public static void Apply(Form form)
        {
            form.BackColor = Canvas;
            form.ForeColor = Ink;
            form.HandleCreated += (s, e) => {
                int enabled = 1;
                DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
            };
            StyleControls(form);
        }

        private static void StyleControls(Control root)
        {
            foreach (Control control in root.Controls)
            {
                control.ForeColor = control.ForeColor == Color.DimGray ? Muted : Ink;
                control.BackColor = Canvas;
                var button = control as Button;
                if (button != null) StyleButton(button);
                if (control is TextBox || control is ListBox)
                {
                    control.BackColor = Surface;
                    DarkScrollbars(control);
                }
                var checkbox = control as CheckBox;
                if (checkbox != null) checkbox.FlatStyle = FlatStyle.Flat;
                StyleControls(control);
            }
        }

        public static void StyleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.BackColor = AccentSurface;
            button.ForeColor = Ink;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 79, 71);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(57, 92, 83);
        }

        public static void DarkScrollbars(Control control)
        {
            control.HandleCreated += (s, e) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }

        public static void Apply(ContextMenuStrip menu)
        {
            menu.BackColor = Surface;
            menu.ForeColor = Ink;
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public DarkMenuColors() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuItemSelected { get { return AccentSurface; } }
            public override Color MenuItemBorder { get { return Border; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
        }
    }

    internal static class AppDialog
    {
        public static DialogResult Show(string message, string title, IWin32Window owner = null, bool confirm = false)
        {
            using (var dialog = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent,
                Font = new Font("Segoe UI", 10) })
            {
                var label = new Label { Text = message, Location = new Point(16, 16),
                    AutoSize = true, MaximumSize = new Size(408, 0) };
                dialog.Controls.Add(label);
                int buttonTop = label.Bottom + 18;
                dialog.ClientSize = new Size(440, buttonTop + 46);
                var accept = new Button { Name = "Accept", Text = confirm ? "Delete" : "OK", Location = new Point(336, buttonTop),
                    Size = new Size(88, 30), DialogResult = confirm ? DialogResult.Yes : DialogResult.OK };
                dialog.Controls.Add(accept);
                dialog.AcceptButton = accept;
                if (confirm)
                {
                    var cancel = new Button { Name = "Cancel", Text = "Cancel", Location = new Point(240, buttonTop),
                        Size = new Size(88, 30), DialogResult = DialogResult.No };
                    dialog.Controls.Add(cancel);
                    dialog.CancelButton = cancel;
                    dialog.ActiveControl = cancel;
                }
                else dialog.CancelButton = accept;
                AppTheme.Apply(dialog);
                return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            }
        }
    }

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
            AppTheme.Apply(this);
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            var candidate = new AppSettings {
                Hotkey = hotkeyBox.Text.Trim(), Model = modelBox.Text.Trim(),
                Prompt = promptBox.Text.Trim(), StartWithWindows = startupBox.Checked
            };
            if (String.IsNullOrWhiteSpace(candidate.Model) || String.IsNullOrWhiteSpace(candidate.Prompt))
            {
                AppDialog.Show("Model and prompt cannot be empty.", "Settings", this);
                return;
            }
            var error = save(candidate, keyBox.Text.Trim());
            if (!String.IsNullOrEmpty(error))
                AppDialog.Show(error, "Settings", this);
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
                Multiline = multiline, BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
        }
    }

    internal sealed class ResultPopup : Form
    {
        private readonly Rectangle captureBounds;
        private readonly Panel content;
        private readonly List<Font> popupFonts = new List<Font>();
        private readonly FollowUpConversation conversation;
        private readonly Button askButton;
        private readonly Panel composer;
        private readonly AskTextBox questionBox;
        private readonly Button sendButton;
        private readonly Font chatFont;
        private readonly Font captionFont;
        private CancellationTokenSource requestCancellation;
        private Control errorCard;
        private bool chatOpen;
        private bool answering;
        private int contentY;
        private Point? dragOffset;
        private Point preferredLocation;
        internal bool HasBeenDragged { get; private set; }
        internal Point DraggedLocation { get { return preferredLocation; } }
        private const int Gap = 6;
        private const int Inset = 10;
        private const int PopupWidth = 440;
        private const int HeaderHeight = 44;

        public ResultPopup(Rectangle captureBounds, string title, string message,
            TranslationResult result, FollowUpConversation conversation = null)
        {
            this.captureBounds = captureBounds;
            this.conversation = conversation;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = AppTheme.Canvas;
            ForeColor = AppTheme.Ink;
            DoubleBuffered = true;
            Font = OwnFont("Segoe UI", 10);
            captionFont = OwnFont("Segoe UI", 8, FontStyle.Bold);
            ClientSize = new Size(PopupWidth, 120);

            var titlebar = new Panel { Name = "Header", Location = Point.Empty,
                Size = new Size(PopupWidth, HeaderHeight) };
            Controls.Add(titlebar);
            var badge = new PopupBadge { Location = new Point(12, 10), Size = new Size(24, 24) };
            titlebar.Controls.Add(badge);
            var header = new Label { Text = title == "ZHONGWEN SNAP" ? "Your translation" : title,
                Location = new Point(44, 10), Size = new Size(346, 24), TextAlign = ContentAlignment.MiddleLeft,
                Font = OwnFont("Segoe UI", 10, FontStyle.Bold), ForeColor = AppTheme.Ink };
            titlebar.Controls.Add(header);
            var close = new PopupCloseButton { Name = "Close", Location = new Point(PopupWidth - 38, 8),
                Size = new Size(28, 28), AccessibleName = "Close popup" };
            close.Click += (s, e) => Close();
            titlebar.Controls.Add(close);
            EnableDragging(this);
            EnableDragging(titlebar);
            EnableDragging(badge);
            EnableDragging(header);

            content = new Panel { Name = "Content", Location = new Point(Inset, HeaderHeight), Width = PopupWidth - 2 * Inset,
                AutoScroll = true, BackColor = AppTheme.Canvas, TabStop = false };
            AppTheme.DarkScrollbars(content);
            Controls.Add(content);
            if (result == null)
                AddField("STATUS", String.IsNullOrWhiteSpace(message) ? "Just a moment…" : message,
                    OwnFont("Segoe UI", 11), AppTheme.Ink, AppTheme.Surface);
            else
            {
                AddField("CHINESE", result.Text, OwnFont("Microsoft JhengHei UI", 16, FontStyle.Bold), AppTheme.Ink, AppTheme.Surface);
                AddField("PINYIN", result.Pinyin, OwnFont("Segoe UI", 11), AppTheme.Accent, AppTheme.Surface);
                AddField("ENGLISH", result.English, OwnFont("Segoe UI", 11), AppTheme.Ink, AppTheme.Surface);
                AddField("LEARNER NOTE", result.Note, OwnFont("Segoe UI", 10), AppTheme.Ink, AppTheme.NoteSurface);
            }

            chatFont = OwnFont("Microsoft JhengHei UI", 11);

            if (conversation != null)
            {
                askButton = ChatButton("Ask", "Ask", 64, 28);
                askButton.Click += (s, e) => OpenChat();
                Controls.Add(askButton);

                composer = new Panel { Name = "Composer", Size = new Size(content.Width, 58), Visible = false,
                    BackColor = AppTheme.Canvas };
                questionBox = new AskTextBox { Name = "Question", Location = Point.Empty,
                    Size = new Size(content.Width - 70, 58), Multiline = true, AcceptsReturn = true,
                    WordWrap = true, ScrollBars = ScrollBars.Vertical, Font = chatFont,
                    BackColor = AppTheme.Surface, ForeColor = AppTheme.Ink, BorderStyle = BorderStyle.FixedSingle,
                    MaxLength = FollowUpConversation.MaxQuestionLength, AccessibleName = "Question about this translation" };
                AppTheme.DarkScrollbars(questionBox);
                questionBox.SendRequested += SendQuestion;
                composer.Controls.Add(questionBox);
                sendButton = ChatButton("Send", "Send", 64, 28);
                sendButton.Location = new Point(content.Width - 64, 0);
                sendButton.Click += (s, e) => SendQuestion();
                composer.Controls.Add(sendButton);
                Controls.Add(composer);
            }
            LayoutPopup(false);
            Shown += (s, e) => Activate();
        }

        private Button ChatButton(string name, string text, int width, int height)
        {
            var button = new Button { Name = name, Text = text, Size = new Size(width, height),
                Font = OwnFont("Segoe UI", 9, FontStyle.Bold) };
            AppTheme.StyleButton(button);
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
            sendButton.Enabled = false;
            sendButton.Text = "…";
            sendButton.AccessibleName = "Answering";
            var cancellation = new CancellationTokenSource();
            requestCancellation = cancellation;
            try
            {
                var answer = await Task.Run(() => conversation.Ask(question, cancellation.Token));
                if (IsDisposed || Disposing || cancellation.IsCancellationRequested) return;
                content.AutoScrollPosition = Point.Empty;
                AddField("YOU", question, chatFont, AppTheme.Ink, AppTheme.AccentSurface);
                AddField("ANSWER", answer, chatFont, AppTheme.Ink, AppTheme.Surface);
                if (conversation.AtLimit)
                    AddField("CHAT LIMIT REACHED", "Capture again to start a new chat.", chatFont, AppTheme.Muted, AppTheme.Surface);
                questionBox.Clear();
                LayoutPopup(true);
            }
            catch (OperationCanceledException) { /* Closing the popup abandons its conversation. */ }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing || cancellation.IsCancellationRequested) return;
                content.AutoScrollPosition = Point.Empty;
                errorCard = AddField("COULD NOT ANSWER", ex.Message, chatFont, AppTheme.Ink, AppTheme.NoteSurface);
                LayoutPopup(true);
            }
            finally
            {
                if (!IsDisposed && !Disposing && !cancellation.IsCancellationRequested)
                {
                    answering = false;
                    questionBox.ReadOnly = conversation.AtLimit;
                    sendButton.Enabled = !conversation.AtLimit;
                    sendButton.Text = "Send";
                    sendButton.AccessibleName = "Send question";
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
            contentY -= errorCard.Height + Gap;
            content.Controls.Remove(errorCard);
            errorCard.Dispose();
            errorCard = null;
            LayoutPopup(false);
        }

        private void LayoutPopup(bool scrollToLatest)
        {
            int previousScroll = -content.AutoScrollPosition.Y;
            // Automatic scrolling to a focused answer during card placement shifts cards
            // already positioned. Batch the rebuild so repeated layouts cannot add gaps.
            content.SuspendLayout();
            try
            {
                content.AutoScrollPosition = Point.Empty;
                var availableHeight = Math.Max(1, WorkingArea().Height - 16);
                var footerHeight = chatOpen ? composer.Height + Gap : (askButton != null ? askButton.Height + Gap : 0);
                int maximumContentHeight = Math.Max(1, availableHeight - HeaderHeight - Inset - footerHeight);
                if (chatOpen) maximumContentHeight = Math.Min(400, maximumContentHeight);
                LayoutCards(content.Width - 2);
                if (contentY - Gap > maximumContentHeight)
                    LayoutCards(content.Width - 2 - SystemInformation.VerticalScrollBarWidth);
                var totalContentHeight = Math.Max(1, contentY - Gap);
                content.Height = Math.Min(totalContentHeight, maximumContentHeight);
                content.AutoScrollMinSize = new Size(0, totalContentHeight);
                if (composer != null) composer.Location = new Point(Inset, content.Bottom + Gap);
                if (askButton != null) askButton.Location = new Point(PopupWidth - Inset - askButton.Width, content.Bottom + Gap);
                ClientSize = new Size(PopupWidth, content.Bottom + footerHeight + Inset);
                Place();
            }
            finally { content.ResumeLayout(); }
            // ResumeLayout establishes the new scroll range before restoring the viewport.
            content.AutoScrollPosition = new Point(0, scrollToLatest ? contentY : previousScroll);
        }

        private void LayoutCards(int width)
        {
            contentY = 0;
            foreach (Control card in content.Controls)
            {
                var text = (TextBox)card.Controls[1];
                var measured = TextRenderer.MeasureText(text.Text, text.Font, new Size(width - 20, 10000),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                int textHeight = Math.Max(measured.Height + 4, text.Font.Height + 4);
                card.Bounds = new Rectangle(0, contentY, width, textHeight + 34);
                card.Controls[0].Width = width - 20;
                text.Size = new Size(width - 20, textHeight);
                contentY += card.Height + Gap;
            }
        }

        private Control AddField(string caption, string value, Font font, Color color, Color fill)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            // Native Windows textboxes need CRLF. LayoutPopup sizes cards to the available viewport.
            value = value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
            var cardWidth = content.Width - 2;
            var textHeight = font.Height + 4;
            var card = new PopupCard(fill) { Location = new Point(0, contentY),
                Size = new Size(cardWidth, textHeight + 34) };
            var label = new Label { Text = caption, Location = new Point(10, 6), Size = new Size(cardWidth - 20, 16),
                Font = captionFont, ForeColor = AppTheme.Coral, BackColor = fill };
            card.Controls.Add(label);
            var text = new TranscriptTextBox(content) { Text = value, Location = new Point(10, 24),
                Size = new Size(cardWidth - 20, textHeight), Multiline = true, ReadOnly = true,
                BorderStyle = BorderStyle.None, WordWrap = true, ScrollBars = ScrollBars.None,
                BackColor = fill, ForeColor = color, Font = font, Cursor = Cursors.IBeam };
            card.Controls.Add(text);
            content.Controls.Add(card);
            contentY += card.Height + Gap;
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
            using (var border = new Pen(AppTheme.Border))
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private void Place()
        {
            var working = WorkingArea();
            if (HasBeenDragged)
            {
                Location = Constrain(preferredLocation, working);
                return;
            }
            var x = captureBounds.Left + captureBounds.Width / 2 - Width / 2;
            var y = captureBounds.Top - Height - 12;
            if (y < working.Top) y = captureBounds.Bottom + 12;
            x = Math.Max(working.Left, Math.Min(x, working.Right - Width));
            y = Math.Max(working.Top, Math.Min(y, working.Bottom - Height));
            Location = new Point(x, y);
        }

        private Rectangle WorkingArea()
        {
            return HasBeenDragged ? Screen.FromPoint(preferredLocation).WorkingArea : Screen.FromRectangle(captureBounds).WorkingArea;
        }

        private Point Constrain(Point location, Rectangle working)
        {
            return new Point(Math.Max(working.Left, Math.Min(location.X, working.Right - Width)),
                Math.Max(working.Top, Math.Min(location.Y, working.Bottom - Height)));
        }

        internal void RestoreDraggedLocation(Point location)
        {
            preferredLocation = location;
            HasBeenDragged = true;
            LayoutPopup(false);
        }

        private void EnableDragging(Control surface)
        {
            surface.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                var screenPoint = surface.PointToScreen(e.Location);
                dragOffset = new Point(screenPoint.X - Left, screenPoint.Y - Top);
                surface.Capture = true;
            };
            surface.MouseMove += (s, e) => {
                if (!dragOffset.HasValue || !surface.Capture) return;
                var screenPoint = surface.PointToScreen(e.Location);
                var location = new Point(screenPoint.X - dragOffset.Value.X, screenPoint.Y - dragOffset.Value.Y);
                if (location == Location) return;
                preferredLocation = location;
                HasBeenDragged = true;
                Location = location;
            };
            surface.MouseUp += (s, e) => {
                if (e.Button != MouseButtons.Left || !dragOffset.HasValue) return;
                dragOffset = null;
                surface.Capture = false;
                if (HasBeenDragged) LayoutPopup(false);
            };
            surface.MouseCaptureChanged += (s, e) => { if (!surface.Capture) dragOffset = null; };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (questionBox != null && questionBox.Focused && questionBox.IsComposing)
                return base.ProcessCmdKey(ref msg, keyData);
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal sealed class TranscriptTextBox : TextBox
    {
        private readonly Panel viewport;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public TranscriptTextBox(Panel viewport)
        {
            this.viewport = viewport;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x020A) // WM_MOUSEWHEEL
            {
                // Native multiline textboxes consume wheel input even with ScrollBars.None.
                // The transcript owns scrolling; retain the original delta and key modifiers.
                message.Result = SendMessage(viewport.Handle, message.Msg, message.WParam, message.LParam);
                return;
            }
            base.WndProc(ref message);
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

    internal sealed class PopupCloseButton : Button
    {
        public PopupCloseButton()
        {
            AppTheme.StyleButton(this);
            BackColor = AppTheme.Canvas;
            FlatAppearance.MouseOverBackColor = AppTheme.Surface;
            FlatAppearance.MouseDownBackColor = AppTheme.Border;
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float x = (ClientSize.Width - 1) / 2f, y = (ClientSize.Height - 1) / 2f;
            using (var pen = new Pen(AppTheme.Muted, 1.5f))
            {
                e.Graphics.DrawLine(pen, x - 4, y - 4, x + 4, y + 4);
                e.Graphics.DrawLine(pen, x + 4, y - 4, x - 4, y + 4);
            }
        }
    }

    internal sealed class PopupBadge : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(AppTheme.AccentSurface))
                e.Graphics.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
            using (var font = new Font("Microsoft JhengHei UI", Height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics, "中", font, ClientRectangle, AppTheme.Accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class PopupCard : Panel
    {
        private readonly Color fill;

        public PopupCard(Color fill)
        {
            this.fill = fill;
            BackColor = AppTheme.Canvas;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath())
            {
                int right = Width - 1, bottom = Height - 1;
                path.AddArc(0, 0, 10, 10, 180, 90);
                path.AddArc(right - 10, 0, 10, 10, 270, 90);
                path.AddArc(right - 10, bottom - 10, 10, 10, 0, 90);
                path.AddArc(0, bottom - 10, 10, 10, 90, 90);
                path.CloseFigure();
                using (var brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                using (var border = new Pen(AppTheme.Border)) e.Graphics.DrawPath(border, path);
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
            list = new ListBox { Location = new Point(14, 14), Size = new Size(630, 210), BorderStyle = BorderStyle.FixedSingle,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            foreach (var item in entries)
                list.Items.Add(item.CreatedUtc.ToLocalTime().ToString("MMM d, HH:mm") + "   " + item.Text);
            list.SelectedIndexChanged += (s, e) => ShowSelected();
            Controls.Add(list);
            details = new TextBox { Location = new Point(14, 237), Size = new Size(630, 145), Multiline = true,
                ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            Controls.Add(details);
            var clear = new Button { Text = "Clear history", Location = new Point(523, 395), Size = new Size(120, 30),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            clear.Click += (s, e) => {
                if (AppDialog.Show("Delete all saved translations?", "Clear history", this, true) == DialogResult.Yes)
                { HistoryStore.Clear(); entries.Clear(); list.Items.Clear(); details.Clear(); }
            };
            Controls.Add(clear);
            if (entries.Count > 0) list.SelectedIndex = 0;
            AppTheme.Apply(this);
        }

        private void ShowSelected()
        {
            details.Text = list.SelectedIndex >= 0 ? entries[list.SelectedIndex].Combined() : "";
        }
    }
}
