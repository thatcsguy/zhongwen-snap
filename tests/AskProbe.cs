using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZhongWenSnap
{
    internal static class AskProbe
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                TestRequestsAndResponses();
                TestConversation();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                int exitCode = 0;
                using (var host = new Form { Opacity = 0, ShowInTaskbar = false })
                {
                    // Exercise async UI handlers inside the same message loop as the tray app.
                    host.Shown += (s, e) => host.BeginInvoke((Action)(() => {
                        try { TestPopup(); TestClosedPopup(); }
                        catch (Exception ex) { Console.WriteLine(ex); exitCode = 1; }
                        finally { host.Close(); }
                    }));
                    Application.Run(host);
                }
                if (exitCode != 0) return exitCode;
                Console.WriteLine("Ask tests passed: context, full conversation replay, failures, cancellation, limits, keyboard, reset, and popup layout.");
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
            chat.Reset();
            for (int i = 0; i < 30; i++) chat.Ask("question " + i, CancellationToken.None);
            Assert(chat.AtLimit, "The turn limit must be enforced.");
            Expect<InvalidOperationException>(() => chat.Ask("one more", CancellationToken.None));
            chat.Reset();
            Assert(!chat.AtLimit, "Reset must clear the limit.");
            chat.Ask("after reset", CancellationToken.None);
            Assert(fake.LastHistory.Length == 0 && fake.LastContext.Text == Context().Text, "Reset must preserve only the original popup.");
            fake.Next = token => Reply(new string('a', 60000));
            chat.Ask("long answer", CancellationToken.None);
            Assert(chat.AtLimit, "The character limit must be enforced.");

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
                    Expect<InvalidOperationException>(() => concurrent.Reset());
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
                    var reset = Find<Button>(popup, "ResetChat");
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
                        Assert(!send.Enabled && !reset.Enabled && question.ReadOnly, "Only one request may run at a time.");
                        Key(question, Keys.Enter);
                        Assert(fake.Calls == 1, "Repeated Enter must not send duplicates.");
                    }
                    finally { release.Set(); }
                    PumpUntil(() => send.Enabled);
                    Assert(question.Text == "" && AllText(popup).Contains("不應該") && AllText(popup).Contains(Context().Text),
                        "Successful answers must appear while the original translation remains accessible.");
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

                    reset.PerformClick();
                    Assert(!AllText(popup).Contains("I should not be late.") && AllText(popup).Contains(Context().Text), "Reset must remove the transcript but retain the translation.");
                    question.Text = "A long explanation";
                    fake.Next = token => Reply(new string('文', 12000));
                    Key(question, Keys.Enter);
                    PumpUntil(() => send.Enabled);
                    Assert(fake.LastHistory.Length == 0, "Reset must send an empty history on the next request.");
                    Assert(Screen.FromControl(popup).WorkingArea.Contains(popup.Bounds) && composer.Bottom < popup.ClientSize.Height,
                        "Long answers must scroll without pushing the composer offscreen.");
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

        private static ResultPopup Popup(FollowUpConversation chat)
        {
            var working = Screen.PrimaryScreen.WorkingArea;
            return new ResultPopup(new Rectangle(working.Left + working.Width / 2, working.Top + working.Height / 2, 120, 30),
                "ZHONGWEN SNAP", "", Context(), () => { }, chat) { Opacity = 0 };
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
