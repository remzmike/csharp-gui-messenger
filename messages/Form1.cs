// http://bytes.com/topic/c-sharp/answers/231911-blinking-systray-icon
// http://social.msdn.microsoft.com/Forums/en-US/vbgeneral/thread/013ee7e1-3f3f-423b-b35e-7bf2a419219c

// timers, threaded or message based: http://www.codeproject.com/Articles/1132/Multithreading-in-NET
// http://www.codeproject.com/Articles/4201/Proper-Threading-in-Winforms-NET
// http://www.codeproject.com/Articles/18702/Threading-in-NET-and-WinForms

// http://example.com/messages/?action=submit&message=foorelious2013.01.10&lognum=1&origin=itsamemario

// wish: log all text... to a file specific to the channel you are listening to
// wish: save old inputs, up/down arrow cycle

// clickonce is awesome
// Build | Publish | Finish, then sync example.com clickonce session in beyond compare

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Net;
using System.IO;
using System.Web;
using System.Runtime.InteropServices;

namespace messages
{
    public partial class Form1 : Form
    {
        int pollSeconds = 30;
        int fastPollSeconds = 4;
        bool isFlashing = false;
        public string me = "none";
        WebcallAsync asyncPop;
        public int popRetry = 0;
        public readonly int maxPopRetry = 2;
        Icon icon1;
        Icon icon2;
        DateTime lastActivity;
        Font mainFont;
        Font boldFont;
        string[] targets = { "rival", "ganymede", "europa" };
        bool[] tabhilight = { false, false, false };
        System.Media.SoundPlayer player;

        public Form1()
        {
            player = new System.Media.SoundPlayer();
            player.SoundLocation = "notify.wav";
            player.Load();
            
            InitializeComponent();
            // first param is me, otherwise uses machine name
            me = Config.MachineName();
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1) me = args[1];

            timerWebCall.Interval = pollSeconds * 1000; // ignore whatever crap we set in the design mode
            //timerWebCall.Interval = 12000; // debug
            asyncPop = new WebcallAsync();
            asyncPop.webclient.DownloadStringCompleted += OnAsyncPop;
            icon1 = new System.Drawing.Icon("1.ico");
            icon2 = new System.Drawing.Icon("2.ico");

            // hide debug buttons
            if (!System.Diagnostics.Debugger.IsAttached)
            {
                button3.Visible = false;
                button4.Visible = false;
            }

            mainFont = box1.Font;
            boldFont = new Font(mainFont, FontStyle.Bold);

            this.ActiveControl = inputBox;

            labelVersion.Text = "v." + Config.DeployedVersion();

            var i = 0;
            foreach (var target in targets)
            {
                tabControl.TabPages[i].Text = target;
                i++;
            }
        }

        // means this app is active / has focus / whatever
        bool IsActiveApp()
        {
            return Form.ActiveForm != null;
        }
        
        void StartFlashing(MessageParts parts)
        {
            var isEuropa = parts.Origin.ToLower() == "europa";

            // taskbar flashing
            var taskBarFlashCount = uint.MaxValue;
            if (isEuropa)
            {
                taskBarFlashCount = 1;
            }

            var doFlash = false;
            if (taskBarFlashCount > 0)
            {
                doFlash = true;
                // europa cannot reset flash
                if (isFlashing && isEuropa)
                {
                    doFlash = false;
                }
            }
            
            if (doFlash)
            {
                isFlashing = true; // controls the notify icon flashing

                // does the actual taskbar flashing
                this.Invoke(new MethodInvoker(delegate
                {
                    var continuous = taskBarFlashCount == uint.MaxValue;
                    FlashWindow.Flash(this, taskBarFlashCount, continuous);
                })); // run on main thread
            }

            // balloon and sound
            if (parts.IsValid())
            {
                if (!IsActiveApp())
                {
                    // no balloon, no sound, when message from europa
                    if (!isEuropa)
                    {
                        notifyIcon.ShowBalloonTip(5000, parts.Origin + " @ " + parts.TimeStamp, parts.Message, ToolTipIcon.Info);                    
                        player.Play(); // this is async, doesnt block
                    }
                }
            }
        }

        void StopFlashing()
        {
            isFlashing = false;
            ChangeNotifyIcon(icon1);
        }

        void ChangeNotifyIcon(Icon icon)
        {
            notifyIcon.Icon = icon;
        }

        // http://stackoverflow.com/questions/3563889/how-to-let-windows-form-exit-to-system-tray
        // http://stackoverflow.com/questions/3571477/why-is-my-notifyicon-application-not-exiting-when-i-use-application-exit-or-fo
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // false when we call Application.Exit
            // (true if we call this.Close or user closes)
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (Config.ExitToTrayIcon())
                {
                    e.Cancel = true;
                    this.Hide();
                }
                if (Config.ExitToTaskBar())
                {
                    e.Cancel = true;
                    this.WindowState = FormWindowState.Minimized;
                }
            }
            else
            {
                SaveSettings();
            }
        }

        void RestoreWindow()
        {
            // wish: if window is in front, and active, then this causes a flash
            if (Config.ExitToTrayIcon())
            {
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
                this.Show();
                this.Activate();
            }
            if (Config.ExitToTaskBar())
            {
                this.WindowState = FormWindowState.Normal;
                this.BringToFront();
                this.Activate();
            }
            // ya, all this is required -- test case: open, switch to any other app, then double click notify icon
        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            RestoreWindow();
            Trace.WriteLine("notifyIcon_DoubleClick");
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RestoreWindow();
            Trace.WriteLine("openToolStripMenuItem_Click");
        }

        private void timerWebCall_Tick(object sender, EventArgs e)
        {            
            Trace.WriteLine("mk: messages: tick");
            GetMessages();
            ConsiderEndingFastPolling();
        }

        private int getMessageBusyTicks = 0;
        private void GetMessages()
        {
            if (asyncPop.webclient.IsBusy)
            {
                Trace.WriteLine("asyncPop webclient IsBusy");
                // 2013-04-04: saw a bug on ganymede where it stayed busy forever, i think this fixes that
                getMessageBusyTicks++;
                if (getMessageBusyTicks > 10)
                {
                    asyncPop.webclient.CancelAsync();
                    getMessageBusyTicks = 0;
                }
            }
            else
            {
                asyncPop.Pop(me);
                //asyncPop.PopAndKeep(me); // debug
                getMessageBusyTicks = 0;
            }
        }

        private void scrollToEnd(RichTextBox box)
        {            
            // TODO: make it so i dont need this hack...
            // this happens in load when i try to scrollToEnd many boxes
            if (!backgroundWorker2.IsBusy)
            {
                backgroundWorker2.RunWorkerAsync(box);
            }
        }

        private void addText(RichTextBox box, string data)
        {
            box.AppendText(data);
        }

        // http://stackoverflow.com/questions/1926264/color-different-parts-of-a-richtextbox-string
        // http://codebetter.com/patricksmacchia/2008/07/07/some-richtextbox-tricks/
        // http://www.codeproject.com/Articles/15038/C-Formatting-Text-in-a-RichTextBox-by-Parsing-the
        public void addText(RichTextBox box, string data, Color color, bool bold = false)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;

            box.SelectionColor = color;
            if (bold) box.SelectionFont = boldFont;
            addText(box, data);
            if (bold) box.SelectionFont = mainFont;
            box.SelectionColor = box.ForeColor;
        }

        private string GetTimestamp()
        {
            var now = DateTime.Now;
            return now.ToString("MMM d @ h:mm:ss tt");
        }

        private void processResults(string data)
        {            
            var isEmpty = data.Trim() == "";
            var balloon = new MessageParts("");
            if (!isEmpty)
            {
                data = data.Replace("\r\n", "\n");
                var lines = data.Split('\n');
                var boxes = new HashSet<RichTextBox>();
                foreach (var line in lines)
                {
                    // try to parse and color the line
                    try
                    {
                        // todo: somehow combine with MessageParts?
                        var sections = line.Split('-');
                        var validLine = sections.Length >= 3;
                        if (validLine)
                        {
                            var ts = sections[0].Trim();
                            var from = sections[1].Trim();
                            var message = String.Join("-", sections.Skip(2));
                            // message stored urldecoded on server
                            message = HttpUtility.UrlDecode(message);
                            message = message.Trim();
                            var box = ShowMessage(ts, from, message);
                            boxes.Add(box);
                            if (!balloon.IsValid())
                            {
                                balloon.TimeStamp = ts;
                                balloon.Origin = from;
                                balloon.Message = decryptMessage(message);
                            }
                        }
                        else
                        {
                            if (!String.IsNullOrEmpty(line))
                            {
                                var box = getActiveBox();
                                addText(box, line);
                                addText(box, "\n");
                                boxes.Add(box);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        string error = String.Format("Error parsing line:\n{0}\n{1}", line, ex);
                        var box = ShowError(error);
                        boxes.Add(box);
                    }                    
                }
                StartFlashing(balloon);
                StartFastPolling();
                SaveSettings();
                // wish: dont start fast polling unless it's a real message, not an error message
                foreach (var box in boxes)
                {
                    scrollToEnd(box);
                }
            }            
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            StopFlashing();
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                //if (!isFlashing) StartFlashing(); // debug
                if (notifyIcon.Icon == icon2)
                {
                    ChangeNotifyIcon(icon1);
                }
                else // normal icon
                {
                    if (isFlashing)
                    {
                        ChangeNotifyIcon(icon2);
                    }
                }
                Thread.Sleep(1200);
            }
        }

        private RichTextBox getActiveBox()
        {
            return (RichTextBox)tabControl.SelectedTab.Controls[0];
        }
        
        private string getCurrentTarget()
        {
            return targets[tabControl.SelectedIndex];
        }

        private RichTextBox tabindex2box(int index)
        {
            switch (index)
            {
                case 0: return box1;
                case 1: return box2;
                case 2: return box3;
                default: return box1;
            }
        }

        private RichTextBox tabname2box(string tabname)
        {            
            var index = tabname2index(tabname);
            var box = tabindex2box(index);
            return box;
        }

        private int tabname2index(string tabname)
        {
            tabname = tabname.Trim().ToLower();
            var index = Array.IndexOf<string>(targets, tabname);
            if (index == -1) index = 0;
            return index;
        }

        // returns the box it was shown in
        private RichTextBox ShowMessage(string timestamp, string origin, string message, string dest = null)
        {
            string tabname;
            if (dest == null)
            {
                tabname = origin;
                var tabindex = tabname2index(tabname);
                if (!isActiveTab(tabindex))
                {
                    tabhilight[tabindex] = true;
                    // wish: smaller invalidate?
                    tabControl.Invalidate();
                }
            }
            else
            {
                // self echo
                tabname = dest;
            }
            var box = tabname2box(tabname);
            timestamp = timestamp.Trim();
            origin = origin.Trim();
            message = message.Trim();
            addText(box, timestamp, Color.DarkGray);
            addText(box, " - ");
            addText(box, origin, Color.DarkBlue);
            addText(box, " - ");
            message = decryptMessage(message);
            // formatting is lost each line, so if message is multiline, then color each line
            message = message.Replace("\r\n", "\n");
            var lines = message.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                addText(box, line, Color.Black);
                var lastLine = i == lines.Length - 1;
                if (!lastLine) addText(box, "\n");
            }
            /* dont need this anymore
            if (!String.IsNullOrEmpty(dest))
            {
                dest = dest.Trim();
                addText(box, String.Format(" (to {0})", dest), Color.DarkGray);
            }
            */
            addText(box, "\n");
            return box;
        }

        private RichTextBox ShowError(string msg)
        {
            var box = getActiveBox();
            addText(box, String.Format("{0} - {1}\n", GetTimestamp(), msg), Color.Red);
            return box;
        }

        private void SendMessage()
        {
            var message = inputBox.Text.Replace("\r\n", "\n");
            var dest = getCurrentTarget();
            
            var box = ShowMessage(GetTimestamp(), me, message, dest);

            var asyncSubmit = new WebcallAsync();
            asyncSubmit.webclient.DownloadStringCompleted += OnAsyncSubmit;
            // 2013-12-07: encryption support
            var aes = new SimpleAES();
            var encrypted = "aes://" + Convert.ToBase64String(aes.Encrypt(message));
            asyncSubmit.Submit(me, encrypted, dest);

            StartFastPolling();
            SaveSettings();
            inputBox.Text = "";
            scrollToEnd(box);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            SendMessage();
        }

        private string decryptMessage(string message)
        {
            message = message.Trim();
            if (message.StartsWith("aes://"))
            {
                var aes = new SimpleAES();

                try
                {
                    var b64 = Convert.FromBase64String(message.Substring(6));
                    message = aes.Decrypt(b64);
                }
                catch (FormatException)
                {
                    // leave message as is
                }                
            }
            return message;
        }

        private void ResetTimer(int seconds)
        {
            Trace.WriteLine(String.Format("ResetTimer({0})", seconds));
            timerWebCall.Interval = seconds * 1000;
            timerWebCall.Stop();
            timerWebCall.Start();
        }

        private void ConsiderEndingFastPolling()
        {
            Trace.WriteLine("ConsiderEndingFastPolling");
            var delta = (DateTime.Now - lastActivity).TotalSeconds;
            Trace.WriteLine(String.Format("ConsiderEndingFastPolling, delta = {0}",delta));
            if (delta > 120)
            {
                ResetTimer(pollSeconds);
            }
        }

        private void StartFastPolling()
        {
            Trace.WriteLine("StartFastPolling");
            lastActivity = DateTime.Now;
            ResetTimer(fastPollSeconds);
        }

        private void stopFlashingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopFlashing();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            processResults(inputBox.Text);
        }

        private void LoadSettings()
        {
            addText(box1, Properties.Settings.Default.box1Text, Color.DarkGray);
            addText(box2, Properties.Settings.Default.box2Text, Color.DarkGray);
            addText(box3, Properties.Settings.Default.box3Text, Color.DarkGray);
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.box1Text = box1.Text;
            Properties.Settings.Default.box2Text = box2.Text;
            Properties.Settings.Default.box3Text = box3.Text;
            Properties.Settings.Default.Save();
        }

        // ignore return key now that input accepts multiline pastes
        private void inputBox_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Return) e.Handled = true;
        }

        private void inputBox_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return) e.Handled = true;
        }

        private void inputBox_KeyDown(object sender, KeyEventArgs e)
        {
            ConsiderInputBoxResize();            
            if (e.KeyCode == Keys.Return) 
            {
                // weird, press space+m+n, at the same time on ganymede and it sends a return, even happens in metapad
                //addText("return key hit...\n", Color.Green);
                SendMessage();
                e.Handled = true;
            }
        }

        private void ConsiderInputBoxResize()
        {
            // this is weird, changes to line 2 after 150th character, pointless, broken, useless
            /*int logicalLine = inputBox.GetLineFromCharIndex(inputBox.TextLength);
            var lineheight = inputBox.Font.Height;
            if (logicalLine > 1)
            {
                addText(logicalLine.ToString() + ", ", Color.Green);
            }*/
        }

        private void Form1_Load_1(object sender, EventArgs e)
        {
            LoadSettings();
            label1.Text = me;
            for (int i = 0; i < targets.Length; i++)
            {
                var box = tabindex2box(i);
                scrollToEnd(box);
            }
            timerWebCall.Start();
            backgroundWorker1.RunWorkerAsync();
            GetMessages();            
        }

        // test massive colored text
        private void button4_Click(object sender, EventArgs e)
        {
            string msg = "none";
            try
            {
                var ex = new System.Net.WebException("test exception");
                throw ex;
            }
            catch (WebException ex)
            {
                msg = ex.ToString() + "---" + ex.StackTrace;
            }
            var box = getActiveBox();
            var eventMask = Win32.StopRepaint(box);
            for (int i = 0; i < 2000; i++)
            {
                ShowMessage(GetTimestamp(), "test", msg, "dest");
            }
            scrollToEnd(box);
            Win32.ResumeRepaint(this, box, eventMask);
        }

        private void box_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(e.LinkText);
            }
            catch (Exception ex)
            {
                if (Config.ShowErrorsFromLinkClick())
                {
                    var box = ShowError(ex.ToString());
                    scrollToEnd(box);
                }
            }
            inputBox.Focus();
        }

        private void OnAsyncPop(Object sender, DownloadStringCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                processResults(e.Result);
                popRetry = 0;
            }
            else
            {
                // 2013-01-08: ignore random http errors
                popRetry += 1;
                if (popRetry != maxPopRetry) // ignore before AND after... so only one error is shown
                {
                    Trace.WriteLine(String.Format("ignoring fail, retry count = {0}", popRetry));
                }
                else
                {
                    if (Config.ShowErrorsFromPop())
                    {
                        string error = String.Format("Error getting messages:\n{0}", e.Error);
                        var box = ShowError(error);
                        scrollToEnd(box);
                    }
                    else
                    {
                        Trace.WriteLine("asyncPop_OnCompleted error, not configured to show errors");
                    }
                }
            }
        }

        private void OnAsyncSubmit(Object sender, DownloadStringCompletedEventArgs e)            
        {
            if (e.Error == null)
            {
                // reading e.Result when e.Error is not null will re-throw the exception already stored in e.Error
                // disabling this for now since it's hard to tell what tab this was associated with, since they might switch tabs after sending message
                //addText(box, e.Result, Color.Gray); // shows the raw server response, current blank, used to be "- message submit successful -"
            }
            else
            {
                if (Config.ShowErrorsFromSubmit())
                {
                    string error = String.Format("Error submitting message:\n{0}", e.Error);
                    var box = ShowError(error);
                    scrollToEnd(box);
                }
                else
                {
                    Trace.WriteLine("asyncSubmit_OnCompleted error, not configured to show errors");
                }
            }
        }

        private void backgroundWorker2_DoWork(object sender, DoWorkEventArgs e)
        {
            // huge derpin problems... when you click a bad link while inputbox is focused, only when you try to output error text on bad link
            // caused by scrolling/focus/output weirdness, can reproduce by moving this back to scrollToEnd         
            this.Invoke(new MethodInvoker(delegate
            {
                var box = (RichTextBox)e.Argument;
                ///var prev = ActiveControl;
                //ActiveControl = box;
                //box.Focus();
                box.SelectionStart = box.Text.Length;
                box.ScrollToCaret();
                //ActiveControl = prev;
                //prev.Focus();
            }));
        }

        private bool isActiveTab(int index)
        {
            var result = tabControl.SelectedIndex == index;
            return result;
        }

        private void tabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabPage page = tabControl.TabPages[e.Index];
            var textColor = page.ForeColor;
            if (tabhilight[e.Index] && !isActiveTab(e.Index))
            {
                // http://msdn.microsoft.com/en-us/library/system.drawing.systemcolors.aspx
                var bg = new SolidBrush(System.Drawing.SystemColors.Highlight);
                e.Graphics.FillRectangle(bg, e.Bounds);
                //var pen = new Pen(System.Drawing.SystemColors.Highlight);
                //e.Graphics.DrawRectangle(pen, e.Bounds);
                textColor = System.Drawing.SystemColors.HighlightText;
            }
            Rectangle paddedBounds = e.Bounds;
            int yOffset = (e.State == DrawItemState.Selected) ? -2 : 1;
            paddedBounds.Offset(1, yOffset);
            TextRenderer.DrawText(e.Graphics, page.Text, this.Font, paddedBounds, textColor);
        }

        private void tabControl_Selected(object sender, TabControlEventArgs e)
        {            
            tabhilight[e.TabPageIndex] = false;
        }

    } // class

} // namespace

