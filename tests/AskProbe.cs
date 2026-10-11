using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZhongWenSnap
{
    internal static class AskProbe
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                bool scrollOnly = args.Length > 0 && args[0] == "--scroll-test";
                bool dragOnly = args.Length > 0 && args[0] == "--drag-scroll-test";
                if (!scrollOnly && !dragOnly) { TestRequestsAndResponses(); TestConversation(); }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int exitCode = 0;
                using (var host = new Form { Opacity = 0, ShowInTaskbar = false })
                {
                    // Exercise async UI handlers inside the same message loop as the tray app.
                    host.Shown += (s, e) => host.BeginInvoke((Action)(() => {
                        try {
                            if (!scrollOnly) TestDraggingScroll();
                            if (!dragOnly) TestPopupScrolling();
                            if (!scrollOnly && !dragOnly) { TestPopup(); TestClosedPopup(); TestPopupMovement(); TestDialogs(); SaveThemePreviews(); }
                        }
                        catch (Exception ex) { Console.WriteLine(ex); exitCode = 1; }
                        finally { host.Close(); }
                    }));
                    Application.Run(host);
                }
                if (exitCode != 0) return exitCode;
                Console.WriteLine(dragOnly ? "Drag and scroll tests passed." : scrollOnly ? "Popup scrolling tests passed." :
                    "Ask tests passed: context, full conversation replay, failures, cancellation, limits, keyboard, popup dragging, scrolling, and layout.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static TranslationResult Context()
        {
            return new TranslationResult { Text = "我應該去嗎？", Pinyin = "wǒ yīnggāi qù ma?",
                English = "Should I go?", Note = "應該 expresses what should happen or what is likely." };
        }

        private static TutorReply Reply(string text)
        {
            var payload = new Dictionary<string, object> {
                { "status", "completed" }, { "output", new object[] {
                    new Dictionary<string, object> { { "type", "reasoning" }, { "id", "rs_test" },
                        { "summary", new object[0] }, { "encrypted_content", "opaque-reasoning" } },
                    new Dictionary<string, object> { { "type", "message" }, { "id", "msg_test" },
                        { "role", "assistant" }, { "phase", "final_answer" }, { "status", "completed" },
                        { "content", new object[] { new Dictionary<string, object> {
                            { "type", "output_text" }, { "text", text }
                        } } } }
                } }
            };
            return OpenAiTutor.ParseResponse(new JavaScriptSerializer().Serialize(payload));
        }

        private static void TestRequestsAndResponses()
        {
            var request = Object(OpenAiTutor.BuildRequest(Context(), new object[0], "How do I negate 應該?", "gpt-6-luna"));
            Assert(!(bool)request["store"] && Convert.ToString(request["model"]) == "gpt-6-luna", "Ask must use the selected model and store=false.");
            Assert(!request.ContainsKey("text") && !request.ContainsKey("previous_response_id") && !request.ContainsKey("conversation"),
                "Ask must use local context and a free-text response.");
            Assert(Convert.ToString(((Dictionary<string, object>)request["reasoning"])["effort"]) == "low", "Ask reasoning setting changed.");
            Assert(((object[])request["include"])[0].ToString() == "reasoning.encrypted_content", "Encrypted reasoning must be available for replay.");
            var input = (object[])request["input"];
            var seed = Convert.ToString(((Dictionary<string, object>)input[0])["content"]);
            foreach (var field in new[] { Context().Text, Context().Pinyin, Context().English, Context().Note })
                Assert(seed.Contains(field), "Missing popup context: " + field);
            Assert(input.Length == 2 && Convert.ToString(((Dictionary<string, object>)input[1])["content"]) == "How do I negate 應該?", "The question must follow the popup context.");
            Assert(!OpenAiTutor.BuildRequest(Context(), new object[0], "question", "custom-model").Contains("input_image"), "Follow-ups must be text only.");
            var custom = Object(OpenAiTutor.BuildRequest(Context(), new object[0], "question", "custom-model"));
            Assert(!custom.ContainsKey("reasoning") && Convert.ToString(custom["model"]) == "custom-model", "Custom models must remain usable.");
            Assert(Reply("不應該\n\nbù yīnggāi").Text == "不應該\n\nbù yīnggāi", "Answer parsing must preserve paragraphs and Unicode.");
            Expect<InvalidOperationException>(() => OpenAiTutor.ParseResponse("{\"status\":\"incomplete\",\"output\":[]}"));
            Expect<InvalidOperationException>(() => OpenAiTutor.ParseResponse("{\"status\":\"completed\",\"output\":[]}"));
            Expect<InvalidOperationException>(() => OpenAiTutor.ParseResponse("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"declined\"}]}]}"));
        }

        private static void TestConversation()
        {
            var source = Context();
            var fake = new RecordingAnswerer();
            var chat = new FollowUpConversation(source, fake);
            source.Text = "changed after capture";
            Expect<InvalidOperationException>(() => chat.Ask("  ", CancellationToken.None));
            Expect<InvalidOperationException>(() => chat.Ask(new string('x', 4001), CancellationToken.None));
            Assert(fake.Calls == 0, "Invalid questions must not make requests.");
            chat.Ask("How do I negate it?", CancellationToken.None);
            Assert(fake.LastHistory.Length == 0 && fake.LastContext.Text == Context().Text, "Each chat must freeze its popup context.");
            chat.Ask("Can you give another example?", CancellationToken.None);
            var request = Object(OpenAiTutor.BuildRequest(fake.LastContext, fake.LastHistory, fake.LastQuestion, "gpt-6-luna"));
            var input = (object[])request["input"];
            Assert(input.Length == 5, "The second question must replay the first user turn and all output items.");
            Assert(Convert.ToString(((Dictionary<string, object>)input[1])["content"]) == "How do I negate it?", "The first question was lost.");
            Assert(Convert.ToString(((Dictionary<string, object>)input[2])["encrypted_content"]) == "opaque-reasoning", "Reasoning was lost.");
            Assert(Convert.ToString(((Dictionary<string, object>)input[3])["phase"]) == "final_answer", "Assistant phase was lost.");
            fake.Next = token => { throw new InvalidOperationException("Network error"); };
            Expect<InvalidOperationException>(() => chat.Ask("Failed question", CancellationToken.None));
            fake.Next = null;
            chat.Ask("Retry question", CancellationToken.None);
            Assert(fake.LastHistory.Length == 6 && !new JavaScriptSerializer().Serialize(fake.LastHistory).Contains("Failed question"),
                "A failed request must not be committed or duplicated on retry.");
            using (var cancellation = new CancellationTokenSource())
            {
                fake.Next = token => { cancellation.Cancel(); return Reply("Late answer"); };
                Expect<OperationCanceledException>(() => chat.Ask("Cancelled question", cancellation.Token));
            }
            fake.Next = null;
            chat.Ask("After cancellation", CancellationToken.None);
            Assert(fake.LastHistory.Length == 9, "Cancelled requests must not advance the conversation.");
            for (int i = 4; i < 30; i++) chat.Ask("question " + i, CancellationToken.None);
            Assert(chat.AtLimit, "The turn limit must be enforced.");
            Expect<InvalidOperationException>(() => chat.Ask("one more", CancellationToken.None));
            var fresh = new FollowUpConversation(Context(), fake);
            fresh.Ask("new capture", CancellationToken.None);
            Assert(fake.LastHistory.Length == 0 && fake.LastContext.Text == Context().Text, "A new popup must start its own chat.");
            fake.Next = token => Reply(new string('a', 60000));
            fresh.Ask("long answer", CancellationToken.None);
            Assert(fresh.AtLimit, "The character limit must be enforced.");

            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var blocking = new RecordingAnswerer { Next = token => { started.Set(); release.Wait(); return Reply("answer"); } };
                var concurrent = new FollowUpConversation(Context(), blocking);
                var pending = Task.Run(() => concurrent.Ask("first", CancellationToken.None));
                try
                {
                    Assert(started.Wait(5000), "The pending request never started.");
                    Expect<InvalidOperationException>(() => concurrent.Ask("duplicate", CancellationToken.None));
                }
                finally { release.Set(); }
                Assert(pending.Wait(5000), "The pending request never completed.");
            }
        }

        private static void TestPopup()
        {
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var fake = new RecordingAnswerer { Next = token => {
                    started.Set(); release.Wait(); return Reply("Use 不應該 (bù yīnggāi) for ‘should not.’\n\n我不應該去。\nWǒ bù yīnggāi qù.\nI should not go.");
                } };
                var chat = new FollowUpConversation(Context(), fake);
                using (var popup = Popup(chat))
                {
                    popup.Show();
                    Application.DoEvents();
                    SavePreview(popup, "translation-popup.png");
                    var ask = Find<Button>(popup, "Ask");
                    ask.PerformClick();
                    var question = Find<AskTextBox>(popup, "Question");
                    var send = Find<Button>(popup, "Send");
                    var composer = Find<Panel>(popup, "Composer");
                    Assert(composer.Visible && question.Focused, "Ask must reveal and focus the question textbox.");
                    Assert(question.Multiline && question.AcceptsReturn, "The question textbox must support newlines.");
                    Key(question, Keys.Enter);
                    Assert(fake.Calls == 0, "Empty questions must not send.");
                    question.Text = "How would I negate 應該?";
                    var newline = Key(question, Keys.Shift | Keys.Enter);
                    Assert(!newline.SuppressKeyPress && fake.Calls == 0, "Shift+Enter must be allowed as a newline.");
                    Ime(question, 0x010D);
                    var composition = Key(question, Keys.Enter);
                    Assert(!composition.SuppressKeyPress && fake.Calls == 0, "Enter must confirm Chinese composition without sending.");
                    Ime(question, 0x010E);
                    Assert(Key(question, Keys.Enter).SuppressKeyPress, "Enter must send and suppress the newline.");
                    try
                    {
                        PumpUntil(() => started.IsSet);
                        Assert(!send.Enabled && question.ReadOnly, "Only one request may run at a time.");
                        Key(question, Keys.Enter);
                        Assert(fake.Calls == 1, "Repeated Enter must not send duplicates.");
                    }
                    finally { release.Set(); }
                    PumpUntil(() => send.Enabled);
                    Assert(question.Text == "" && AllText(popup).Contains("不應該") && AllText(popup).Contains(Context().Text),
                        "Successful answers must appear while the original translation remains accessible. Popup content:\n" + AllText(popup));
                    Assert(AllText(popup).Contains("should not.’\r\n\r\n我不應該去。"), "Windows textboxes must preserve answer paragraphs.");
                    foreach (Control child in popup.Controls)
                    {
                        var panel = child as Panel;
                        if (panel != null && panel.AutoScroll)
                            Assert(!panel.HorizontalScroll.Visible, "Translation and answer cards must fit beside the vertical scrollbar.");
                    }
                    Assert(Screen.FromControl(popup).WorkingArea.Contains(popup.Bounds), "An expanded popup must stay inside the screen.");
                    SavePreview(popup, "ask-popup.png");

                    fake.Next = token => { throw new InvalidOperationException("Network unavailable. Try again."); };
                    question.Text = "Another example?";
                    Key(question, Keys.Enter);
                    PumpUntil(() => send.Enabled);
                    Assert(question.Text == "Another example?" && AllText(popup).Contains("COULD NOT ANSWER"), "Errors must retain a retryable question.");
                    fake.Next = token => Reply("我不應該遲到。\nWǒ bù yīnggāi chídào.\nI should not be late.");
                    send.PerformClick();
                    PumpUntil(() => send.Enabled);
                    Assert(fake.LastHistory.Length == 3 && !AllText(popup).Contains("COULD NOT ANSWER"), "Retry must preserve context and remove the old error.");

                    question.Text = "A long explanation";
                    fake.Next = token => Reply(new string('文', 12000));
                    Key(question, Keys.Enter);
                    PumpUntil(() => send.Enabled);
                    Assert(fake.LastHistory.Length == 6, "Later questions must retain all successful turns.");
                    Assert(Screen.FromControl(popup).WorkingArea.Contains(popup.Bounds) && composer.Bottom < popup.ClientSize.Height,
                        "Long answers must scroll without pushing the composer offscreen.");
                    fake.Next = token => Reply(new string('a', 60000));
                    question.Text = "More detail";
                    Key(question, Keys.Enter);
                    PumpUntil(() => AllText(popup).Contains("CHAT LIMIT REACHED"));
                    Assert(!send.Enabled && question.ReadOnly && AllText(popup).Contains("Capture again"),
                        "A completed chat must show an actionable limit without offering a reset.");
                    int callsAtLimit = fake.Calls;
                    Key(question, Keys.Enter);
                    Assert(fake.Calls == callsAtLimit, "A chat at its limit must not make another request.");
                }
            }
        }

        private static void TestClosedPopup()
        {
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var fake = new RecordingAnswerer { Next = token => { started.Set(); release.Wait(); return Reply("Late answer after close"); } };
                using (var popup = Popup(new FollowUpConversation(Context(), fake)))
                {
                    popup.Show();
                    Find<Button>(popup, "Ask").PerformClick();
                    var question = Find<AskTextBox>(popup, "Question");
                    question.Text = "pending question";
                    Key(question, Keys.Enter);
                    try
                    {
                        PumpUntil(() => started.IsSet);
                        popup.Close();
                        Assert(fake.LastCancellation.IsCancellationRequested && popup.IsDisposed, "Closing must cancel the active request.");
                    }
                    finally { release.Set(); }
                    var cancellationField = typeof(ResultPopup).GetField("requestCancellation", BindingFlags.Instance | BindingFlags.NonPublic);
                    PumpUntil(() => cancellationField.GetValue(popup) == null);
                    using (var replacement = Popup(new FollowUpConversation(Context(), new RecordingAnswerer())))
                    {
                        replacement.Show();
                        Application.DoEvents();
                        Assert(!AllText(replacement).Contains("Late answer after close"), "A late response must never appear in another popup.");
                    }
                }
            }
        }

        private static void TestPopupScrolling()
        {
            var fake = new RecordingAnswerer { Next = token => Reply("First line.\nSecond line.") };
            using (var popup = Popup(new FollowUpConversation(Context(), fake)))
            {
                popup.Show();
                Find<Button>(popup, "Ask").PerformClick();
                var question = Find<AskTextBox>(popup, "Question");
                question.Text = "Explain this";
                Key(question, Keys.Enter);
                PumpUntil(() => Find<Button>(popup, "Send").Enabled);
                var content = Find<Panel>(popup, "Content");
                Assert(content.VerticalScroll.Visible && -content.AutoScrollPosition.Y > 0,
                    "The reply must create a scrollable transcript starting at the newest answer.");
                var answer = (TextBox)content.Controls[content.Controls.Count - 1].Controls[1];
                int before = -content.AutoScrollPosition.Y;
                Wheel(answer, content, 120);
                int after = -content.AutoScrollPosition.Y;
                Console.WriteLine("Wheel over answer: before={0}, after={1}", before, after);
                Assert(after < before, "Mouse wheel over the answer is stuck: the transcript did not scroll up.");
                answer.Focus();
                answer.Select(0, 5);
                content.AutoScrollPosition = new Point(0, before);
                Wheel(answer, content, 120);
                Assert(-content.AutoScrollPosition.Y < before && answer.SelectedText == "First",
                    "Wheel scrolling must work with answer text focused and preserve the selection.");
                foreach (var target in new Control[] { answer.Parent, answer.Parent.Controls[0], content })
                {
                    content.AutoScrollPosition = new Point(0, before);
                    Wheel(target, content, 120);
                    Assert(-content.AutoScrollPosition.Y < before, "Wheel scrolling must also work over captions, card padding, and the viewport.");
                }

                var lines = new List<string>();
                for (int line = 0; line < 40; line++) lines.Add("我不應該去。I should not go.");
                var longAnswer = String.Join("\n", lines);
                fake.Next = token => Reply(longAnswer);
                for (int turn = 0; turn < 3; turn++)
                {
                    question.Text = "Another example?";
                    Key(question, Keys.Enter);
                    PumpUntil(() => Find<Button>(popup, "Send").Enabled);
                    answer = (TextBox)content.Controls[content.Controls.Count - 1].Controls[1];
                    int bottom = -content.AutoScrollPosition.Y;
                    Assert(bottom > content.Height, "Long replies must create a transcript larger than its viewport.");
                    for (int step = 0; step < 3; step++)
                    {
                        before = -content.AutoScrollPosition.Y;
                        Wheel(answer, content, 120);
                        Assert(-content.AutoScrollPosition.Y < before, "Scrolling up a long answer must not get stuck after successive replies.");
                    }
                    for (int step = 0; step < 3; step++) Wheel(answer, content, -120);
                    Assert(-content.AutoScrollPosition.Y == bottom, "Scrolling down must return to the newest answer.");
                }
                question.Text = "Line one\r\nLine two\r\nLine three\r\nLine four\r\nLine five\r\nLine six";
                question.Focus();
                question.SelectionStart = question.TextLength;
                question.ScrollToCaret();
                int transcriptPosition = -content.AutoScrollPosition.Y;
                int editorLine = SendMessage(question.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                Wheel(question, question, 120);
                Assert(-content.AutoScrollPosition.Y == transcriptPosition &&
                    SendMessage(question.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32() < editorLine,
                    "Wheel input inside the question editor must scroll the editor without moving the transcript.");
            }
        }

        private static void Wheel(Control recipient, Control viewport, int delta)
        {
            var visible = Rectangle.Intersect(recipient.RectangleToScreen(recipient.ClientRectangle),
                viewport.RectangleToScreen(viewport.ClientRectangle));
            Assert(!visible.IsEmpty, "The wheel target must be visible inside the transcript.");
            int x = visible.Left + visible.Width / 2, y = visible.Top + visible.Height / 2;
            SendMessage(recipient.Handle, 0x020A, new IntPtr(delta << 16), new IntPtr((x & 0xffff) | (y << 16)));
            Application.DoEvents();
        }

        private static void TestDraggingScroll()
        {
            TestDraggingScroll(2);
            TestDraggingScroll(40);
        }

        private static void TestDraggingScroll(int lineCount)
        {
            var lines = new List<string>();
            for (int line = 0; line < lineCount; line++) lines.Add("我不應該去。I should not go.");
            var fake = new RecordingAnswerer { Next = token => Reply(String.Join("\n", lines)) };
            using (var popup = Popup(new FollowUpConversation(Context(), fake)))
            {
                popup.Show();
                Find<Button>(popup, "Ask").PerformClick();
                var question = Find<AskTextBox>(popup, "Question");
                question.Text = "Explain this";
                Key(question, Keys.Enter);
                PumpUntil(() => Find<Button>(popup, "Send").Enabled);
                var content = Find<Panel>(popup, "Content");
                var header = Find<Panel>(popup, "Header");
                var working = Screen.PrimaryScreen.WorkingArea;
                var answer = (TextBox)content.Controls[content.Controls.Count - 1].Controls[1];
                int height = content.AutoScrollMinSize.Height;
                var original = (TextBox)content.Controls[0].Controls[1];
                var surfaces = new Control[] { header, header.Controls[0], header.Controls[1] };
                Assert(content.VerticalScroll.Visible, "The transcript must be scrollable before dragging.");
                foreach (var focus in new Control[] { question, answer, original })
                {
                    focus.Focus();
                    foreach (int offset in new int[] { 0, height / 2, height })
                    {
                        content.AutoScrollPosition = new Point(0, offset);
                        int scroll = content.AutoScrollPosition.Y;
                        var tops = new List<int>();
                        foreach (Control card in content.Controls) tops.Add(card.Top - scroll);
                        for (int drag = 0; drag < 12; drag++)
                        {
                            DragTo(popup, surfaces[drag % surfaces.Length],
                                new Point(working.Left + 24 + (drag % 2) * 10, working.Top + 24));
                            Application.DoEvents();
                            Assert(content.Controls[0].Top - content.AutoScrollPosition.Y == 0,
                                "Dragging adds blank space above the transcript.");
                            Assert(content.AutoScrollPosition.Y == scroll, "Dragging changes the transcript scroll position.");
                            Assert(content.AutoScrollMinSize.Height == height, "Dragging grows the transcript without adding messages.");
                            for (int card = 0; card < content.Controls.Count; card++)
                                Assert(content.Controls[card].Top - content.AutoScrollPosition.Y == tops[card],
                                    "Dragging shifts a card inside the transcript.");
                        }
                    }
                }
                Console.WriteLine("Repeated drags preserved the transcript: {0}-line answer, 3 focused controls, 3 scroll positions.", lineCount);
            }
        }

        private static void TestPopupMovement()
        {
            var working = Screen.PrimaryScreen.WorkingArea;
            var target = new Point(working.Left + 24, working.Top + 24);
            using (var popup = Popup(new FollowUpConversation(Context(), new RecordingAnswerer())))
            {
                popup.Show();
                Application.DoEvents();
                var header = Find<Panel>(popup, "Header");
                DragTo(popup, header, target);
                Assert(popup.Location == target && popup.HasBeenDragged, "Dragging the header must move the popup.");
                var originalSize = popup.Size;
                Find<Button>(popup, "Ask").PerformClick();
                Assert(popup.Location == target && popup.Height > originalSize.Height, "Opening Ask must retain the dragged position.");
                var question = Find<AskTextBox>(popup, "Question");
                question.Text = "Explain this";
                Key(question, Keys.Enter);
                PumpUntil(() => Find<Button>(popup, "Send").Enabled);
                Assert(popup.Location == target, "Receiving an answer must retain the dragged position.");
                var text = (TextBox)Find<Panel>(popup, "Content").Controls[0].Controls[1];
                text.Select(0, 2);
                Mouse(text, "OnMouseDown", MouseButtons.Left, new Point(4, 4));
                Mouse(text, "OnMouseMove", MouseButtons.Left, new Point(44, 24));
                Mouse(text, "OnMouseUp", MouseButtons.Left, new Point(44, 24));
                Assert(popup.Location == target && text.SelectedText.Length > 0, "Translation text must remain selectable without dragging the popup.");
                DragTo(popup, header, new Point(working.Right - 30, working.Bottom - 30));
                Assert(working.Contains(popup.Bounds), "Finishing a drag must keep the popup reachable onscreen.");
                using (var replacement = Popup(null))
                {
                    replacement.RestoreDraggedLocation(target);
                    replacement.Show();
                    Assert(replacement.Location == target, "Replacing the loading popup must preserve its dragged position.");
                }
                Find<PopupCloseButton>(popup, "Close").PerformClick();
                Assert(popup.IsDisposed, "The close icon must close the popup.");
            }
            using (var popup = Popup(null))
            {
                popup.Show();
                var args = new object[] { Message.Create(popup.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Escape };
                var handled = typeof(ResultPopup).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(popup, args);
                Assert((bool)handled && popup.IsDisposed, "Escape must still close the popup without a visible hint.");
            }
        }

        private static void DragTo(ResultPopup popup, Control surface, Point target)
        {
            var origin = new Point(6, 6);
            var delta = new Point(target.X - popup.Left, target.Y - popup.Top);
            Mouse(surface, "OnMouseDown", MouseButtons.Left, origin);
            Mouse(surface, "OnMouseMove", MouseButtons.Left, new Point(origin.X + delta.X, origin.Y + delta.Y));
            Mouse(surface, "OnMouseUp", MouseButtons.Left, origin);
        }

        private static void Mouse(Control control, string method, MouseButtons button, Point point)
        {
            typeof(Control).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(control,
                new object[] { new MouseEventArgs(button, 1, point.X, point.Y, 0) });
        }

        private static void SaveThemePreviews()
        {
            using (var settings = new SettingsForm(new AppSettings(), "sample-key", (candidate, key) => null) { Opacity = 0 })
            {
                settings.Show();
                Application.DoEvents();
                SavePreview(settings, "settings-dark.png");
            }
            using (var history = new HistoryForm { Opacity = 0 })
            {
                // Preview sample data without changing the user's saved history.
                foreach (Control control in history.Controls)
                {
                    var list = control as ListBox;
                    if (list != null) { list.Items.Clear(); list.Items.Add("Oct 10, 12:00   " + Context().Text); }
                    var details = control as TextBox;
                    if (details != null) details.Text = Context().Combined();
                }
                history.Show();
                Application.DoEvents();
                SavePreview(history, "history-dark.png");
            }
        }

        private static void TestDialogs()
        {
            Assert(DialogReply("Cancel", true) == DialogResult.No, "Cancelling a confirmation must preserve history.");
            Assert(DialogReply("Accept", true) == DialogResult.Yes, "Confirming deletion must return Yes.");
            Assert(DialogReply("Accept", false) == DialogResult.OK, "An informational dialog must close with OK.");
        }

        private static DialogResult DialogReply(string button, bool confirm)
        {
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += (s, e) => {
                    var dialog = Application.OpenForms[Application.OpenForms.Count - 1];
                    timer.Stop();
                    Assert(dialog.Text == "Dialog probe", "The themed dialog did not open.");
                    if (confirm && button == "Cancel") SavePreview(dialog, "dialog-dark.png");
                    Find<Button>(dialog, button).PerformClick();
                };
                timer.Start();
                return AppDialog.Show("Delete all saved translations?", "Dialog probe", null, confirm);
            }
        }

        private static ResultPopup Popup(FollowUpConversation chat)
        {
            var working = Screen.PrimaryScreen.WorkingArea;
            return new ResultPopup(new Rectangle(working.Left + working.Width / 2, working.Top + working.Height / 2, 120, 30),
                "ZHONGWEN SNAP", "", Context(), chat) { Opacity = 0 };
        }

        private static KeyEventArgs Key(AskTextBox textbox, Keys key)
        {
            var args = new KeyEventArgs(key);
            typeof(AskTextBox).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(textbox, new object[] { args });
            return args;
        }

        private static void Ime(AskTextBox textbox, int messageId)
        {
            var message = Message.Create(textbox.Handle, messageId, IntPtr.Zero, IntPtr.Zero);
            typeof(AskTextBox).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(textbox, new object[] { message });
        }

        private static T Find<T>(Control root, string name) where T : Control
        {
            return (T)root.Controls.Find(name, true)[0];
        }

        private static string AllText(Control control)
        {
            var result = control.Text;
            foreach (Control child in control.Controls) result += "\n" + AllText(child);
            return result;
        }

        private static void SavePreview(Form popup, string name)
        {
            var directory = Path.Combine(Environment.CurrentDirectory, "work");
            Directory.CreateDirectory(directory);
            using (var bitmap = new Bitmap(popup.Width, popup.Height))
            {
                popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(directory, name), ImageFormat.Png);
            }
        }

        private static void PumpUntil(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.ElapsedMilliseconds > 5000) throw new Exception("Timed out waiting for the popup request.");
                Application.DoEvents();
                Thread.Sleep(10);
            }
            Application.DoEvents();
        }

        private static Dictionary<string, object> Object(string json)
        {
            return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        private sealed class RecordingAnswerer : IQuestionAnswerer
        {
            public int Calls;
            public TranslationResult LastContext;
            public object[] LastHistory;
            public string LastQuestion;
            public CancellationToken LastCancellation;
            public Func<CancellationToken, TutorReply> Next;

            public TutorReply Answer(TranslationResult context, object[] history, string question, CancellationToken cancellation)
            {
                Calls++;
                LastContext = context;
                LastHistory = history;
                LastQuestion = question;
                LastCancellation = cancellation;
                return Next == null ? Reply("不應該 means ‘should not.’") : Next(cancellation);
            }
        }
    }
}
