// Step-by-step "add a GitHub account" wizard. C# 5 - no interpolation, no ?.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace GhAccounts
{
    public class AccountWizard : Form
    {
        public Account Result;

        readonly Config cfg;
        readonly Panel[] pages = new Panel[4];
        readonly string[] titles = {
            "Which GitHub account?", "Choose an SSH key",
            "Give the key to GitHub", "Check it works" };
        readonly string[] blurbs = {
            "The username exactly as it appears in your profile URL.",
            "A key proves who you are. If you have never set one up, generate one.",
            "GitHub needs the public half of the key. Nothing secret leaves your machine.",
            "Asking GitHub who this key belongs to." };

        int step;
        Label lblStep, lblTitle, lblBlurb;
        Button bBack, bNext, bCancel;
        TextBox tOrg, tName, tEmail, tKey, tPub;
        RadioButton rExisting, rGenerate;
        Label lblResult, lblGenNote;
        string keyPath = "";
        bool verified;
        bool demoMode;

        public AccountWizard(Config config)
        {
            cfg = config;
            Text = "Add a GitHub account";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 430);
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;

            var head = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Color.White };
            lblStep = new Label { Left = 20, Top = 12, Width = 120, ForeColor = Color.FromArgb(37, 99, 235),
                                  Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
            lblTitle = new Label { Left = 20, Top = 30, Width = 520,
                                   Font = new Font("Segoe UI", 13f, FontStyle.Regular) };
            lblBlurb = new Label { Left = 22, Top = 54, Width = 520, ForeColor = Color.DimGray };
            head.Controls.AddRange(new Control[] { lblStep, lblTitle, lblBlurb });

            var foot = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.FromArgb(246, 246, 248) };
            bCancel = new Button { Text = "Cancel", Width = 90, Left = 20, Top = 12, DialogResult = DialogResult.Cancel };
            bBack = new Button { Text = "< Back", Width = 90, Left = 340, Top = 12 };
            bNext = new Button { Text = "Next >", Width = 90, Left = 440, Top = 12 };
            bBack.Click += delegate { Go(step - 1); };
            bNext.Click += OnNext;
            foot.Controls.AddRange(new Control[] { bCancel, bBack, bNext });

            var body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20, 8, 20, 0) };
            pages[0] = BuildAccountPage();
            pages[1] = BuildKeyPage();
            pages[2] = BuildGitHubPage();
            pages[3] = BuildVerifyPage();
            foreach (Panel p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; body.Controls.Add(p); }

            Controls.Add(body); Controls.Add(foot); Controls.Add(head);
            CancelButton = bCancel;
            Go(0);
        }

        Panel BuildAccountPage()
        {
            var p = new Panel();
            p.Controls.Add(new Label { Text = "GitHub username", Left = 0, Top = 14, Width = 120 });
            tOrg = new TextBox { Left = 130, Top = 11, Width = 330 };
            p.Controls.Add(tOrg);
            p.Controls.Add(new Label { Text = "e.g. octocat  -  github.com/octocat", Left = 132, Top = 36,
                                       Width = 330, ForeColor = Color.DimGray });

            p.Controls.Add(new Label { Text = "Commit name", Left = 0, Top = 74, Width = 120 });
            tName = new TextBox { Left = 130, Top = 71, Width = 330 };
            p.Controls.Add(tName);

            p.Controls.Add(new Label { Text = "Commit email", Left = 0, Top = 114, Width = 120 });
            tEmail = new TextBox { Left = 130, Top = 111, Width = 330 };
            p.Controls.Add(tEmail);
            p.Controls.Add(new Label
            {
                Left = 132, Top = 136, Width = 380, Height = 46, ForeColor = Color.DimGray,
                Text = "Leave both blank to use the GitHub noreply address, which\n" +
                       "always works even with \"Keep my email private\" turned on."
            });
            return p;
        }

        Panel BuildKeyPage()
        {
            var p = new Panel();
            rGenerate = new RadioButton { Text = "Generate a new key for this account", Left = 0, Top = 10,
                                          Width = 400, Checked = true };
            lblGenNote = new Label
            {
                Left = 20, Top = 34, Width = 460, Height = 34, ForeColor = Color.DimGray,
                Text = "Recommended. Creates an ed25519 key in your .ssh folder.\nPick this if you have never set up SSH for GitHub."
            };
            rExisting = new RadioButton { Text = "Use a key I already have", Left = 0, Top = 84, Width = 300 };
            tKey = new TextBox { Left = 20, Top = 110, Width = 350, Enabled = false };
            var bBrowse = new Button { Text = "Browse...", Left = 380, Top = 108, Width = 90, Enabled = false };
            bBrowse.Click += delegate
            {
                var d = new OpenFileDialog { InitialDirectory = Env.SshDir,
                                             Title = "Select the private key (not the .pub file)" };
                if (d.ShowDialog() == DialogResult.OK) tKey.Text = d.FileName;
            };
            rExisting.CheckedChanged += delegate
            {
                tKey.Enabled = rExisting.Checked; bBrowse.Enabled = rExisting.Checked;
            };
            p.Controls.AddRange(new Control[] { rGenerate, lblGenNote, rExisting, tKey, bBrowse });
            return p;
        }

        Panel BuildGitHubPage()
        {
            var p = new Panel();
            p.Controls.Add(new Label
            {
                Left = 0, Top = 4, Width = 500, Height = 34, ForeColor = Color.FromArgb(180, 83, 9),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Text = "Sign in to GitHub as this account first.\nFor a second account use a private window, or sign out."
            });
            p.Controls.Add(new Label { Text = "Public key (already copied to your clipboard):",
                                       Left = 0, Top = 46, Width = 400 });
            tPub = new TextBox { Left = 0, Top = 66, Width = 500, Height = 74, Multiline = true,
                                 ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                                 Font = new Font("Consolas", 8.5f), BackColor = Color.FromArgb(248, 248, 250) };
            p.Controls.Add(tPub);
            var bCopy = new Button { Text = "Copy again", Left = 0, Top = 150, Width = 110 };
            var bOpen = new Button { Text = "Open GitHub key page", Left = 120, Top = 150, Width = 170 };
            bCopy.Click += delegate
            {
                if (tPub.Text.Length > 0) { Clipboard.SetText(tPub.Text); bCopy.Text = "Copied"; }
            };
            bOpen.Click += delegate { Process.Start("https://github.com/settings/ssh/new"); };
            p.Controls.AddRange(new Control[] { bCopy, bOpen });
            p.Controls.Add(new Label
            {
                Left = 0, Top = 190, Width = 500, Height = 50, ForeColor = Color.DimGray,
                Text = "Paste it into the \"Key\" box on that page, give it any title,\n" +
                       "then press Add SSH key. Come back here and continue."
            });
            return p;
        }

        Panel BuildVerifyPage()
        {
            var p = new Panel();
            lblResult = new Label { Left = 0, Top = 20, Width = 500, Height = 150,
                                    Font = new Font("Segoe UI", 10f) };
            p.Controls.Add(lblResult);
            return p;
        }

        void Go(int n)
        {
            if (n < 0 || n > 3) return;
            step = n;
            for (int i = 0; i < pages.Length; i++) pages[i].Visible = (i == n);
            lblStep.Text = string.Format("STEP {0} OF 4", n + 1);
            lblTitle.Text = titles[n];
            lblBlurb.Text = blurbs[n];
            bBack.Enabled = n > 0;
            bNext.Text = n == 3 ? "Finish" : "Next >";
            if (n == 3 && !demoMode) RunVerify();
        }

        /// <summary>Drive the wizard for documentation screenshots - no keygen,
        /// no network.</summary>
        public void Demo(int n, string org, string pub, string result, Color colour)
        {
            demoMode = true;
            tOrg.Text = org;
            if (pub != null) tPub.Text = pub;
            Go(n);
            if (n == 3) { lblResult.ForeColor = colour; lblResult.Text = result; }
        }

        void OnNext(object sender, EventArgs e)
        {
            if (step == 0)
            {
                if (tOrg.Text.Trim().Length == 0)
                { MessageBox.Show(this, "Enter the GitHub username."); return; }
                Go(1); return;
            }
            if (step == 1)
            {
                string org = tOrg.Text.Trim();
                string id = new string(org.ToLowerInvariant()
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
                string email = tEmail.Text.Trim().Length > 0
                    ? tEmail.Text.Trim() : org + "@users.noreply.github.com";
                if (rGenerate.Checked)
                {
                    Cursor = Cursors.WaitCursor;
                    try { keyPath = Gh.MakeKey(id, email); }
                    catch (Exception ex)
                    { Cursor = Cursors.Default; MessageBox.Show(this, "ssh-keygen failed:\n" + ex.Message); return; }
                    Cursor = Cursors.Default;
                }
                else
                {
                    keyPath = tKey.Text.Trim();
                    if (keyPath.Length == 0 || !File.Exists(keyPath))
                    { MessageBox.Show(this, "Pick an existing private key file."); return; }
                }
                Build();
                string pub = Gh.PubKey(Result);
                tPub.Text = pub;
                if (pub.Length > 0) { try { Clipboard.SetText(pub); } catch { } }
                Go(2); return;
            }
            if (step == 2) { Go(3); return; }

            // step 3 - Finish
            if (!verified &&
                MessageBox.Show(this,
                    "GitHub has not accepted this key yet. Add it anyway?\n\n" +
                    "You can retry later with \"Test connection\".",
                    "Not verified", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        void Build()
        {
            string org = tOrg.Text.Trim();
            if (Result == null) Result = new Account();
            Result.Org = org;
            Result.Id = new string(org.ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            Result.Name = tName.Text.Trim().Length > 0 ? tName.Text.Trim() : org;
            Result.Email = tEmail.Text.Trim().Length > 0
                ? tEmail.Text.Trim() : org + "@users.noreply.github.com";
            Result.Key = keyPath;
            if (string.IsNullOrEmpty(Result.Colour))
                Result.Colour = Env.Palette[cfg.Accounts.Count % Env.Palette.Length];
        }

        void RunVerify()
        {
            Build();
            lblResult.ForeColor = Color.DimGray;
            lblResult.Text = "Contacting GitHub...";
            bNext.Enabled = false;
            Application.DoEvents();
            string who;
            bool ok = Gh.Verify(Result, out who);
            bNext.Enabled = true;
            if (!ok)
            {
                verified = false;
                lblResult.ForeColor = Color.Firebrick;
                lblResult.Text = "GitHub did not accept the key.\n\n  " + who +
                    "\n\nGo Back and make sure the public key was added to the\n" +
                    "right account, then try again.";
                return;
            }
            verified = true;
            if (!string.Equals(who, Result.Org, StringComparison.OrdinalIgnoreCase))
            {
                lblResult.ForeColor = Color.FromArgb(180, 83, 9);
                lblResult.Text = "Connected - but this key belongs to \"" + who + "\",\n" +
                    "not \"" + Result.Org + "\".\n\nGitHub is the authority here, so the account will be\n" +
                    "saved as \"" + who + "\". URL matching needs the exact name.";
                if (string.Equals(Result.Name, Result.Org, StringComparison.OrdinalIgnoreCase))
                    Result.Name = who;
                if (string.Equals(Result.Email, Result.Org + "@users.noreply.github.com",
                                  StringComparison.OrdinalIgnoreCase))
                    Result.Email = who + "@users.noreply.github.com";
                Result.Org = who;
                Result.Id = new string(who.ToLowerInvariant()
                    .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
                return;
            }
            lblResult.ForeColor = Color.FromArgb(0, 110, 40);
            lblResult.Text = "Connected. GitHub says: Hi " + who + "!\n\n" +
                "Commits will be authored as:\n  " + Result.Name + " <" + Result.Email + ">\n\n" +
                "Press Finish to add the account.";
        }
    }
}
