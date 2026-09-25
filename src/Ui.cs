using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace GhAccounts
{
    public class AccountDialog : Form
    {
        TextBox tOrg, tName, tEmail, tKey;
        RadioButton rExisting, rGenerate;
        public Account Result;
        public bool GeneratedKey;
        Config cfg;
        bool editing;

        public AccountDialog(Config config, Account existing)
        {
            cfg = config;
            editing = existing != null;
            Text = editing ? "Edit account" : "Add GitHub account";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 356);
            Font = new Font("Segoe UI", 9f);

            int y = 12;
            Add(new Label
            {
                Left = 16, Top = y, Width = 484, Height = 44, ForeColor = Color.DimGray,
                Text = "Sign in to GitHub as this account in your browser first.\n" +
                       "For a second account use a private/incognito window, or sign out first."
            });
            y += 50;
            Add(new Label { Text = "GitHub username", Left = 16, Top = y + 3, Width = 130 });
            tOrg = new TextBox { Left = 150, Top = y, Width = 350 };
            Add(tOrg); y += 32;

            Add(new Label { Text = "Commit name", Left = 16, Top = y + 3, Width = 130 });
            tName = new TextBox { Left = 150, Top = y, Width = 350 };
            Add(tName); y += 32;

            Add(new Label { Text = "Commit email", Left = 16, Top = y + 3, Width = 130 });
            tEmail = new TextBox { Left = 150, Top = y, Width = 350 };
            Add(tEmail); y += 26;
            var hint = new Label
            {
                Left = 150, Top = y, Width = 350, Height = 30,
                ForeColor = Color.DimGray,
                Text = "Tip: use ID+username@users.noreply.github.com if the\naccount has \"Keep my email private\" turned on."
            };
            Add(hint); y += 40;

            var gb = new GroupBox { Text = "SSH key", Left = 16, Top = y, Width = 484, Height = 100 };
            rExisting = new RadioButton { Text = "Use existing key file", Left = 12, Top = 22, Width = 200, Checked = true };
            rGenerate = new RadioButton { Text = "Generate a new key (no keys yet? pick this)", Left = 12, Top = 68, Width = 320 };
            tKey = new TextBox { Left = 32, Top = 44, Width = 330 };
            var bBrowse = new Button { Text = "Browse...", Left = 372, Top = 42, Width = 90 };
            bBrowse.Click += delegate
            {
                var d = new OpenFileDialog();
                d.InitialDirectory = Env.SshDir;
                d.Title = "Select the private key (not the .pub file)";
                if (d.ShowDialog() == DialogResult.OK) { tKey.Text = d.FileName; rExisting.Checked = true; }
            };
            gb.Controls.AddRange(new Control[] { rExisting, tKey, bBrowse, rGenerate });
            Add(gb);

            var ok = new Button { Text = editing ? "Save" : "Add", Left = 320, Top = 314, Width = 90, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancel", Left = 416, Top = 314, Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Add(ok); Add(cancel);
            AcceptButton = ok; CancelButton = cancel;

            if (editing)
            {
                tOrg.Text = existing.Org; tName.Text = existing.Name;
                tEmail.Text = existing.Email; tKey.Text = existing.Key;
                Result = existing;
            }
            tOrg.TextChanged += delegate
            {
                if (!editing && tName.Text.Length == 0) { }
            };
        }

        void Add(Control c) { Controls.Add(c); }

        void OnOk(object sender, EventArgs e)
        {
            string org = tOrg.Text.Trim();
            if (org.Length == 0) { MessageBox.Show(this, "GitHub username is required."); return; }
            string id = new string(org.ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            string name = tName.Text.Trim().Length > 0 ? tName.Text.Trim() : org;
            string email = tEmail.Text.Trim().Length > 0
                ? tEmail.Text.Trim() : org + "@users.noreply.github.com";
            string key = tKey.Text.Trim();

            if (rGenerate.Checked)
            {
                try { key = Gh.MakeKey(id, email); GeneratedKey = true; }
                catch (Exception ex)
                { MessageBox.Show(this, "ssh-keygen failed:\n" + ex.Message); return; }
            }
            if (key.Length == 0 || !File.Exists(key))
            { MessageBox.Show(this, "Pick an existing private key file, or choose Generate."); return; }

            if (Result == null) Result = new Account();
            Result.Id = id; Result.Org = org; Result.Name = name;
            Result.Email = email; Result.Key = key;
            if (string.IsNullOrEmpty(Result.Colour))
                Result.Colour = Env.Palette[cfg.Accounts.Count % Env.Palette.Length];
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    public class MainForm : Form
    {
        Config cfg;
        bool firstRun;
        TabControl tabs;
        ListView lvRepos, lvAccounts;
        ListBox lbRoots;
        ComboBox cbTarget;
        NumericUpDown nDepth;
        Label statusLabel;
        List<RepoInfo> repos = new List<RepoInfo>();
        Dictionary<string, int> dotIndex = new Dictionary<string, int>();
        ImageList dots = new ImageList();

        public MainForm()
        {
            firstRun = !File.Exists(Env.ConfigPath);
            cfg = Store.Load();
            if (Store.LoadError != null)
            {
                MessageBox.Show(
                    "Your saved accounts at " + Env.ConfigPath + " could not be read:\n\n" +
                    Store.LoadError + "\n\n" +
                    "Starting with an empty account list. Nothing has been overwritten yet - " +
                    "if this is unexpected, close the app and check that file before changing " +
                    "anything here.",
                    "GitHub Accounts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            Text = "GitHub Accounts";
            ClientSize = new Size(1040, 580);
            MinimumSize = new Size(760, 420);
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;

            dots.ImageSize = new Size(12, 12);
            dots.ColorDepth = ColorDepth.Depth32Bit;

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(BuildRepoTab());
            tabs.TabPages.Add(BuildAccountTab());
            tabs.TabPages.Add(BuildFolderTab());

            // A plain Label rather than a StatusStrip: ToolStrip-derived
            // controls do not render through DrawToBitmap, which makes the
            // status line impossible to verify in a screenshot.
            statusLabel = new Label
            {
                Dock = DockStyle.Fill, Text = "",
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0),
                BackColor = Color.FromArgb(240, 240, 240), ForeColor = Color.FromArgb(50, 50, 50),
                BorderStyle = BorderStyle.None
            };
            var statusBar = new Panel
            {
                Dock = DockStyle.Bottom, Height = 24,
                BackColor = Color.FromArgb(240, 240, 240)
            };
            statusBar.Controls.Add(statusLabel);
            // Dock order matters: the LAST control added is docked FIRST, so
            // the status line must be added after the Fill control or the tabs
            // swallow the whole client area.
            Controls.Add(tabs);
            Controls.Add(statusBar);

            Load += delegate
            {
                RebuildDots(); RefreshAccounts();
                if (firstRun) FirstRun();
                Rescan();
            };
        }

        /// <summary>Guess where this machine keeps its repositories.</summary>
        static List<string> GuessRoots()
        {
            var guesses = new List<string>();
            string home = Env.Home;
            string[] rel = { "source\\repos", "Documents\\GitHub", "github", "Projects",
                             "projects", "repos", "src", "dev", "code" };
            foreach (string r in rel)
            {
                string p = Path.Combine(home, r);
                if (Directory.Exists(p)) guesses.Add(p);
            }
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                foreach (string r in new[] { "github", "repos", "projects", "src", "code", "dev" })
                {
                    string p = Path.Combine(d.RootDirectory.FullName, r);
                    if (Directory.Exists(p) && !guesses.Contains(p)) guesses.Add(p);
                }
            }
            return guesses;
        }

        void FirstRun()
        {
            List<string> guesses = GuessRoots();
            foreach (string g in guesses)
                if (!cfg.Roots.Contains(g)) cfg.Roots.Add(g);
            if (cfg.Roots.Count > 0) Store.Save(cfg);

            var sb = new StringBuilder();
            sb.Append("Welcome. Three steps to get going:\n\n");
            sb.Append("1. Accounts tab - \"Detect keys...\" finds SSH keys that already\n");
            sb.Append("   work with GitHub, or \"Add account...\" sets up a new one\n");
            sb.Append("   (it can generate the key and hand it to GitHub for you).\n\n");
            sb.Append("2. Folders tab - point it at the folders holding your repos.\n");
            if (guesses.Count > 0)
                sb.Append("   Already added for you: " + string.Join(", ", guesses.ToArray()) + "\n\n");
            else
                sb.Append("   Nothing found automatically - add one yourself.\n\n");
            sb.Append("3. Repositories tab - each repo then commits and pushes as the\n");
            sb.Append("   account that owns it, with no per-repo setup.");
            MessageBox.Show(this, sb.ToString(), "GitHub Accounts",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            tabs.SelectedIndex = 1;
        }

        static readonly Color statusNormal = Color.FromArgb(50, 50, 50);
        static readonly Color statusError = Color.FromArgb(178, 30, 30);
        void Say(string msg) { Say(msg, false); }
        void Say(string msg, bool isError)
        {
            statusLabel.Text = msg;
            statusLabel.ForeColor = isError ? statusError : statusNormal;
            statusLabel.Refresh();
        }

        void RebuildDots()
        {
            dots.Images.Clear(); dotIndex.Clear();
            foreach (Account a in cfg.Accounts)
            {
                var bmp = new Bitmap(12, 12);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var br = new SolidBrush(a.Dot)) g.FillEllipse(br, 1, 1, 10, 10);
                }
                dotIndex[a.Id] = dots.Images.Count;
                dots.Images.Add(bmp);
            }
            var grey = new Bitmap(12, 12);
            using (Graphics g = Graphics.FromImage(grey))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var br = new SolidBrush(Color.Silver)) g.FillEllipse(br, 1, 1, 10, 10);
            }
            dotIndex["?"] = dots.Images.Count;
            dots.Images.Add(grey);
        }

        // ------------------------------------------------------------ repos
        TabPage BuildRepoTab()
        {
            var page = new TabPage("Repositories");
            var bar = new Panel { Dock = DockStyle.Top, Height = 40 };

            var lbl = new Label { Text = "Switch selected to:", Left = 8, Top = 12, Width = 115, AutoSize = false };
            cbTarget = new ComboBox { Left = 125, Top = 8, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            var bApply = new Button { Text = "Apply", Left = 313, Top = 7, Width = 80 };
            var bFix = new Button { Text = "Fix all mismatches", Left = 401, Top = 7, Width = 140 };
            var bRescan = new Button { Text = "Rescan", Left = 549, Top = 7, Width = 80 };
            var bOpen = new Button { Text = "Open folder", Left = 637, Top = 7, Width = 100 };
            bApply.Click += delegate { ApplySelected(); };
            bFix.Click += delegate { FixAll(); };
            bRescan.Click += delegate { Rescan(); };
            bOpen.Click += delegate
            {
                RepoInfo r = SelectedRepo();
                if (r != null) Process.Start("explorer.exe", "\"" + r.Path + "\"");
            };
            bar.Controls.AddRange(new Control[] { lbl, cbTarget, bApply, bFix, bRescan, bOpen });

            lvRepos = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                GridLines = false, MultiSelect = true, HideSelection = false,
                SmallImageList = dots, OwnerDraw = false
            };
            lvRepos.Columns.Add("Repository", 220);
            lvRepos.Columns.Add("Commits as", 160);
            lvRepos.Columns.Add("Repo owner", 140);
            lvRepos.Columns.Add("Notes", 470);
            lvRepos.DoubleClick += delegate
            {
                RepoInfo r = SelectedRepo();
                if (r != null && r.Mismatch)
                {
                    string err = Repos.Switch(cfg, r.Path, r.RemoteAcct);
                    Rescan();
                    if (err != null) Say(r.Name + ": " + err, true);
                    else Say(string.Format("Switched {0} to {1}.", r.Name, r.RemoteAcct.Org));
                }
                else ApplySelected();
            };
            page.Controls.Add(lvRepos);
            page.Controls.Add(bar);
            return page;
        }

        RepoInfo SelectedRepo()
        {
            if (lvRepos.SelectedItems.Count == 0) return null;
            return lvRepos.SelectedItems[0].Tag as RepoInfo;
        }

        void ApplySelected()
        {
            if (cbTarget.SelectedIndex < 0 || cbTarget.SelectedIndex >= cfg.Accounts.Count)
            { Say("Pick an account first."); return; }
            Account a = cfg.Accounts[cbTarget.SelectedIndex];
            int n = 0;
            var failed = new List<string>();
            foreach (ListViewItem it in lvRepos.SelectedItems)
            {
                var r = it.Tag as RepoInfo;
                if (r == null) continue;
                string err = Repos.Switch(cfg, r.Path, a);
                if (err != null) failed.Add(r.Name + ": " + err);
                else n++;
            }
            if (n == 0 && failed.Count == 0) { Say("Select one or more repositories."); return; }
            Rescan();
            if (failed.Count == 0)
                Say(string.Format("Switched {0} repo(s) to {1}.", n, a.Org));
            else
                Say(string.Format("Switched {0} repo(s) to {1}; {2} failed - {3}",
                    n, a.Org, failed.Count, string.Join("; ", failed.ToArray())), true);
        }

        void FixAll()
        {
            int n = 0;
            var failed = new List<string>();
            foreach (RepoInfo r in repos)
            {
                if (!r.Mismatch) continue;
                string err = Repos.Switch(cfg, r.Path, r.RemoteAcct);
                if (err != null) failed.Add(r.Name + ": " + err);
                else n++;
            }
            Rescan();
            if (failed.Count > 0)
                Say(string.Format("Fixed {0}, {1} failed - {2}",
                    n, failed.Count, string.Join("; ", failed.ToArray())), true);
            else
                Say(n == 0 ? "Nothing to fix - every repo matches its remote."
                           : string.Format("Fixed {0} mismatched repo(s).", n));
        }

        void Rescan()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                repos.Clear();
                lvRepos.Items.Clear();
                if (cfg.Accounts.Count == 0)
                {
                    Say("No accounts yet - add one on the Accounts tab (\"Detect keys...\" is the quick way).");
                    return;
                }
                if (cfg.Roots.Count == 0)
                {
                    Say("No folders configured yet - add one on the Folders tab.");
                    return;
                }
                foreach (string p in Repos.Find(cfg)) repos.Add(Repos.Info(cfg, p));
                int mismatched = 0, unknown = 0;
                foreach (RepoInfo r in repos)
                {
                    var it = new ListViewItem(r.Name);
                    it.Tag = r;
                    it.ImageIndex = r.Acct != null ? dotIndex[r.Acct.Id] : dotIndex["?"];
                    it.SubItems.Add(r.Acct != null ? r.Acct.Org
                                    : (r.Email.Length > 0 ? r.Email : "(unset)"));
                    it.SubItems.Add(r.RemoteAcct != null ? r.RemoteAcct.Org : "-");
                    var notes = new List<string>();
                    if (r.Mismatch)
                    {
                        notes.Add("commits as the wrong account for this remote");
                        it.BackColor = Color.FromArgb(255, 249, 224);
                        mismatched++;
                    }
                    if (r.Acct == null) unknown++;
                    if (r.Url.Length == 0) notes.Add("no remote configured");
                    if (r.Pinned) notes.Add("pinned locally");
                    if (r.Last.Length > 0 && Repos.ByEmail(cfg, r.Last) == null)
                        notes.Add("last commit by " + r.Last);
                    it.SubItems.Add(string.Join("; ", notes.ToArray()));
                    lvRepos.Items.Add(it);
                }
                Say(string.Format("{0} repositories - {1} mismatched, {2} on an unknown identity.",
                                  repos.Count, mismatched, unknown));
            }
            finally { Cursor = Cursors.Default; }
        }

        // --------------------------------------------------------- accounts
        TabPage BuildAccountTab()
        {
            var page = new TabPage("Accounts");
            var bar = new Panel { Dock = DockStyle.Top, Height = 40 };
            var bAdd = new Button { Text = "Add account...", Left = 8, Top = 7, Width = 110 };
            var bEdit = new Button { Text = "Edit", Left = 126, Top = 7, Width = 70 };
            var bDel = new Button { Text = "Remove", Left = 204, Top = 7, Width = 80 };
            var bTest = new Button { Text = "Test connection", Left = 292, Top = 7, Width = 120 };
            var bCopy = new Button { Text = "Copy public key", Left = 420, Top = 7, Width = 120 };
            var bGh = new Button { Text = "Add key on GitHub", Left = 548, Top = 7, Width = 140 };
            var bDetect = new Button { Text = "Detect keys...", Left = 696, Top = 7, Width = 110 };
            bDetect.Click += delegate { DetectKeys(); };
            bAdd.Click += delegate { AddAccount(); };
            bEdit.Click += delegate { EditAccount(); };
            bDel.Click += delegate { RemoveAccount(); };
            bTest.Click += delegate { TestAccounts(); };
            bCopy.Click += delegate { CopyPub(); };
            bGh.Click += delegate { Process.Start("https://github.com/settings/ssh/new"); };
            bar.Controls.AddRange(new Control[] { bAdd, bEdit, bDel, bTest, bCopy, bGh, bDetect });

            lvAccounts = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
                HideSelection = false, SmallImageList = dots
            };
            lvAccounts.Columns.Add("GitHub user", 160);
            lvAccounts.Columns.Add("Commit identity", 300);
            lvAccounts.Columns.Add("SSH key", 280);
            lvAccounts.Columns.Add("Status", 160);
            lvAccounts.DoubleClick += delegate { EditAccount(); };
            page.Controls.Add(lvAccounts);
            page.Controls.Add(bar);
            return page;
        }

        void RefreshAccounts()
        {
            RebuildDots();
            lvAccounts.Items.Clear();
            cbTarget.Items.Clear();
            foreach (Account a in cfg.Accounts)
            {
                var it = new ListViewItem(a.Org);
                it.Tag = a;
                it.ImageIndex = dotIndex[a.Id];
                it.SubItems.Add(a.Name + "  <" + a.Email + ">");
                it.SubItems.Add(Path.GetFileName(a.Key));
                it.SubItems.Add("not tested");
                lvAccounts.Items.Add(it);
                cbTarget.Items.Add(a.Org);
            }
            if (cbTarget.Items.Count > 0) cbTarget.SelectedIndex = 0;
            lvRepos.SmallImageList = dots;
            lvAccounts.SmallImageList = dots;
        }

        void Persist()
        {
            Store.Save(cfg);
            try
            {
                Apply.All(cfg);
                List<string> bad = Apply.Check(cfg);
                if (bad.Count > 0)
                    MessageBox.Show(this,
                        "The config was written, but ssh cannot resolve:\n\n  " +
                        string.Join("\n  ", bad.ToArray()) +
                        "\n\nPushes to those accounts will fail until this is sorted. " +
                        "Check that ~/.ssh/config is the file ssh actually reads.",
                        "Alias check failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not write config:\n" + ex.Message); }
        }

        void AddAccount()
        {
            var d = new AccountWizard(cfg);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            foreach (Account a in cfg.Accounts)
                if (a.Id == d.Result.Id)
                { MessageBox.Show(this, "That account is already configured."); return; }
            cfg.Accounts.Add(d.Result);
            Persist(); RefreshAccounts(); Rescan();
            Say("Added " + d.Result.Org + " - commits as " + d.Result.Email + ".");
        }

        void EditAccount()
        {
            if (lvAccounts.SelectedItems.Count == 0) { Say("Select an account."); return; }
            var a = lvAccounts.SelectedItems[0].Tag as Account;
            var d = new AccountDialog(cfg, a);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Persist(); RefreshAccounts(); Rescan();
            Say("Saved " + a.Org + ".");
        }

        void RemoveAccount()
        {
            if (lvAccounts.SelectedItems.Count == 0) { Say("Select an account."); return; }
            var a = lvAccounts.SelectedItems[0].Tag as Account;
            if (MessageBox.Show(this,
                    "Remove " + a.Org + " from this app?\n\n" +
                    "Repositories keep working; they just stop being routed to\n" +
                    "this account automatically.",
                    "Remove account", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;

            cfg.Accounts.Remove(a);

            string idFile = Path.Combine(Env.Home, ".gitconfig-" + a.Id);
            try { if (File.Exists(idFile)) File.Delete(idFile); } catch { }

            // Only offer to delete a key that lives in the ssh folder - never one
            // the user pointed at from somewhere else.
            bool ours = false;
            try
            {
                ours = string.Equals(Path.GetDirectoryName(a.Key).TrimEnd('\\'),
                                     Env.SshDir.TrimEnd('\\'),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { }
            if (ours && File.Exists(a.Key) &&
                MessageBox.Show(this,
                    "Also delete the key file?\n\n  " + Path.GetFileName(a.Key) +
                    "\n\nOnly do this if no other machine or service uses it.\n" +
                    "GitHub keeps its copy until you remove it there too.",
                    "Delete key file", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    File.Delete(a.Key);
                    if (File.Exists(a.Key + ".pub")) File.Delete(a.Key + ".pub");
                    Say("Removed " + a.Org + " and deleted " + Path.GetFileName(a.Key) + ".");
                }
                catch (Exception ex) { MessageBox.Show(this, "Could not delete the key:\n" + ex.Message); }
            }
            else Say("Removed " + a.Org + ". Key file left in place.");

            Persist(); RefreshAccounts(); Rescan();
        }

        void TestAccounts()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (ListViewItem it in lvAccounts.Items)
                {
                    var a = it.Tag as Account;
                    Say("Testing " + a.Org + "...");
                    string who;
                    bool ok = Gh.Verify(a, out who);
                    it.SubItems[3].Text = ok ? "OK - Hi " + who + "!" : "FAILED - " + who;
                    it.ForeColor = ok ? Color.FromArgb(0, 110, 40) : Color.Firebrick;
                    if (ok && !string.Equals(who, a.Org, StringComparison.OrdinalIgnoreCase))
                    {
                        it.SubItems[3].Text = "key belongs to " + who + ", not " + a.Org;
                        if (ReconcileOrg(a, who)) { Say("Corrected to " + who + "."); return; }
                    }
                }
                Say("Connection test finished.");
            }
            finally { Cursor = Cursors.Default; }
        }

        /// <summary>GitHub is the authority on who a key belongs to - offer to
        /// correct a mistyped username rather than failing quietly later.</summary>
        bool ReconcileOrg(Account a, string who)
        {
            if (string.Equals(a.Org, who, StringComparison.OrdinalIgnoreCase)) return false;
            if (MessageBox.Show(this,
                    "This key authenticates as \"" + who + "\", but the account is set up as \"" +
                    a.Org + "\".\n\nURL matching uses the username, so it must be exact.\n\n" +
                    "Change this account to \"" + who + "\"?",
                    "Username mismatch", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            string oldEmail = a.Org + "@users.noreply.github.com";
            if (string.Equals(a.Email, oldEmail, StringComparison.OrdinalIgnoreCase))
                a.Email = who + "@users.noreply.github.com";
            if (string.Equals(a.Name, a.Org, StringComparison.OrdinalIgnoreCase)) a.Name = who;
            a.Org = who;
            a.Id = new string(who.ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            Persist(); RefreshAccounts(); Rescan();
            return true;
        }

        void DetectKeys()
        {
            // Try every private key under ~/.ssh (and one level down) against
            // GitHub, and offer whichever ones actually authenticate.
            var candidates = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(Env.SshDir))
                    if (!f.EndsWith(".pub") && Path.GetFileName(f).StartsWith("id_"))
                        candidates.Add(f);
                foreach (string d in Directory.GetDirectories(Env.SshDir))
                    foreach (string f in Directory.GetFiles(d))
                        if (!f.EndsWith(".pub") && Path.GetFileName(f).StartsWith("id_"))
                            candidates.Add(f);
            }
            catch { }
            if (candidates.Count == 0) { Say("No key files found in " + Env.SshDir); return; }

            Cursor = Cursors.WaitCursor;
            var added = new List<string>();
            var dead = new List<string>();
            try
            {
                foreach (string key in candidates)
                {
                    Say("Testing " + Path.GetFileName(key) + "...");
                    var probe = new Account { Id = "probe", Org = "", Name = "", Email = "", Key = key };
                    string who;
                    if (!Gh.Verify(probe, out who)) { dead.Add(Path.GetFileName(key)); continue; }
                    bool known = false;
                    foreach (Account a in cfg.Accounts)
                        if (string.Equals(a.Org, who, StringComparison.OrdinalIgnoreCase)) known = true;
                    if (known) continue;
                    string id = new string(who.ToLowerInvariant()
                        .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
                    cfg.Accounts.Add(new Account
                    {
                        Id = id, Org = who, Name = who,
                        Email = who + "@users.noreply.github.com",
                        Key = key,
                        Colour = Env.Palette[cfg.Accounts.Count % Env.Palette.Length]
                    });
                    added.Add(who);
                }
            }
            finally { Cursor = Cursors.Default; }

            if (added.Count > 0) { Persist(); RefreshAccounts(); Rescan(); }
            var msg = new StringBuilder();
            msg.Append(added.Count == 0 ? "No new accounts found."
                       : "Added: " + string.Join(", ", added.ToArray()) +
                         "\n\nCommit emails default to the GitHub noreply address - " +
                         "use Edit if you want a real one.");
            if (dead.Count > 0)
                msg.Append("\n\nKeys GitHub rejected (left alone): " +
                           string.Join(", ", dead.ToArray()));
            MessageBox.Show(this, msg.ToString(), "Detect keys");
            Say("Key detection finished.");
        }

        void CopyPub()
        {
            if (lvAccounts.SelectedItems.Count == 0) { Say("Select an account."); return; }
            var a = lvAccounts.SelectedItems[0].Tag as Account;
            string pub = Gh.PubKey(a);
            if (pub.Length == 0) { Say("No .pub file next to " + a.Key); return; }
            Clipboard.SetText(pub);
            Say("Public key copied. Paste it at github.com/settings/ssh/new while signed in as " + a.Org + ".");
        }

        // ---------------------------------------------------------- folders
        TabPage BuildFolderTab()
        {
            var page = new TabPage("Folders");
            var bar = new Panel { Dock = DockStyle.Top, Height = 40 };
            var bAdd = new Button { Text = "Add folder...", Left = 8, Top = 7, Width = 110 };
            var bDel = new Button { Text = "Remove", Left = 126, Top = 7, Width = 80 };
            var lblD = new Label { Text = "Search depth:", Left = 220, Top = 12, Width = 85 };
            nDepth = new NumericUpDown { Left = 306, Top = 8, Width = 55, Minimum = 1, Maximum = 8 };
            nDepth.ValueChanged += delegate { cfg.Depth = (int)nDepth.Value; Store.Save(cfg); };
            var bScan = new Button { Text = "Rescan now", Left = 375, Top = 7, Width = 100 };
            bAdd.Click += delegate
            {
                var d = new FolderBrowserDialog();
                d.Description = "Pick a folder that contains your repositories";
                if (d.ShowDialog() == DialogResult.OK && !cfg.Roots.Contains(d.SelectedPath))
                { cfg.Roots.Add(d.SelectedPath); Store.Save(cfg); RefreshRoots(); Rescan(); }
            };
            bDel.Click += delegate
            {
                if (lbRoots.SelectedIndex < 0) return;
                cfg.Roots.RemoveAt(lbRoots.SelectedIndex);
                Store.Save(cfg); RefreshRoots(); Rescan();
            };
            bScan.Click += delegate { Rescan(); tabs.SelectedIndex = 0; };
            bar.Controls.AddRange(new Control[] { bAdd, bDel, lblD, nDepth, bScan });

            lbRoots = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var note = new Label
            {
                Dock = DockStyle.Bottom, Height = 44, ForeColor = Color.DimGray,
                Text = "Every folder listed here is searched for git repositories, up to the depth above.\n" +
                       "Depth 1 = repos directly inside the folder; depth 3 also finds them nested two levels down."
            };
            page.Controls.Add(lbRoots);
            page.Controls.Add(note);
            page.Controls.Add(bar);
            return page;
        }

        void RefreshRoots()
        {
            lbRoots.Items.Clear();
            foreach (string r in cfg.Roots) lbRoots.Items.Add(r);
            nDepth.Value = Math.Max(1, Math.Min(8, cfg.Depth));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshRoots();
        }

        // ------------------------------------------------- headless helpers
        public void RenderTo(string path, int tab)
        {
            tabs.SelectedIndex = tab;
            Application.DoEvents();
            // DrawToBitmap paints the whole window, title bar included, so the
            // bitmap must be the full Size - using ClientSize crops the bottom.
            var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        public Config Cfg { get { return cfg; } }
        public void ForceScan() { RebuildDots(); RefreshAccounts(); RefreshRoots(); Rescan(); }
    }

    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] argv)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var args = new List<string>(argv);
            if (args.Contains("--dump"))
            {
                AttachConsole(-1);
                Config cfg = Store.Load();
                Console.WriteLine("config : " + Env.ConfigPath);
                Console.WriteLine("roots  : " + string.Join(", ", cfg.Roots.ToArray())
                                  + "  (depth " + cfg.Depth + ")");
                foreach (Account a in cfg.Accounts)
                    Console.WriteLine(string.Format("account: {0,-14} {1,-38} {2}",
                        a.Org, a.Email, Path.GetFileName(a.Key)));
                Console.WriteLine();
                foreach (string p in Repos.Find(cfg))
                {
                    RepoInfo r = Repos.Info(cfg, p);
                    Console.WriteLine(string.Format("{0,-26} {1,-14} {2,-14} {3}",
                        r.Name,
                        r.Acct != null ? r.Acct.Org : "?",
                        r.RemoteAcct != null ? r.RemoteAcct.Org : "-",
                        r.Mismatch ? "MISMATCH" : ""));
                }
                return 0;
            }

            if (args.Contains("--apply"))
            {
                AttachConsole(-1);
                Config cfg0 = Store.Load();
                if (cfg0.Accounts.Count == 0)
                { Console.WriteLine("no accounts configured - nothing to apply"); return 1; }
                try
                {
                    Apply.All(cfg0);
                    Store.Save(cfg0);   // also normalises the config file
                    Console.WriteLine("wrote managed blocks:");
                    Console.WriteLine("  " + Path.Combine(Env.SshDir, "config"));
                    Console.WriteLine("  " + Path.Combine(Env.Home, ".gitconfig"));
                    foreach (Account a in cfg0.Accounts)
                        Console.WriteLine("  " + Path.Combine(Env.Home, ".gitconfig-" + a.Id));
                    List<string> bad = Apply.Check(cfg0);
                    if (bad.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("WARNING - aliases are not resolving:");
                        foreach (string b in bad) Console.WriteLine("  " + b);
                        return 2;
                    }
                    Console.WriteLine("all aliases resolve.");
                    return 0;
                }
                catch (Exception ex)
                { Console.WriteLine("FAILED: " + ex.Message); return 1; }
            }

            if (args.Contains("--shotwiz"))
            {
                int k = args.IndexOf("--shotwiz");
                var wiz = new AccountWizard(Store.Load());
                int wstep = args.Count > k + 2 ? int.Parse(args[k + 2]) : 0;
                wiz.Show(); Application.DoEvents();
                if (wstep > 0)
                    wiz.Demo(wstep, "octocat",
                        "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIExampleKeyForTheDocsOnlyNotReal01234 " +
                        "octocat@users.noreply.github.com",
                        "Connected. GitHub says: Hi octocat!\n\n" +
                        "Commits will be authored as:\n  octocat <octocat@users.noreply.github.com>\n\n" +
                        "Press Finish to add the account.",
                        Color.FromArgb(0, 110, 40));
                Application.DoEvents();
                var wb = new Bitmap(wiz.Width, wiz.Height);
                wiz.DrawToBitmap(wb, new Rectangle(0, 0, wiz.Width, wiz.Height));
                wb.Save(args[k + 1], System.Drawing.Imaging.ImageFormat.Png);
                wiz.Close();
                return 0;
            }

            if (args.Contains("--shotdlg"))
            {
                int j = args.IndexOf("--shotdlg");
                var dlg = new AccountDialog(new Config(), null);
                dlg.Show(); Application.DoEvents();
                var b = new Bitmap(dlg.Width, dlg.Height);
                dlg.DrawToBitmap(b, new Rectangle(0, 0, dlg.Width, dlg.Height));
                b.Save(args[j + 1], System.Drawing.Imaging.ImageFormat.Png);
                dlg.Close();
                return 0;
            }

            if (args.Contains("--shot"))
            {
                int i = args.IndexOf("--shot");
                string outPath = args[i + 1];
                int tab = args.Count > i + 2 ? int.Parse(args[i + 2]) : 0;
                var f = new MainForm();
                f.Show();
                Application.DoEvents();
                f.ForceScan();
                Application.DoEvents();
                AttachConsole(-1);
                Console.WriteLine("form client " + f.ClientSize);
                foreach (Control c in f.Controls)
                    Console.WriteLine(string.Format("  {0,-14} dock={1,-6} bounds={2} visible={3}",
                        c.GetType().Name, c.Dock, c.Bounds, c.Visible));
                f.RenderTo(outPath, tab);
                f.Close();
                return 0;
            }

            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("Unexpected error:\n\n" + e.Exception.Message,
                    "GitHub Accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show("Unexpected error:\n\n" + (ex != null ? ex.Message : e.ExceptionObject),
                    "GitHub Accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
            return 0;
        }
    }
}
