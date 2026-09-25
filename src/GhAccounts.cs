// GitHub Account Switcher - native WinForms app.
// Compiled with the in-box .NET Framework csc.exe, so it targets C# 5:
// no string interpolation, no ?. operator, no expression-bodied members.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GhAccounts
{
    public class Account
    {
        public string Id { get; set; }
        public string Org { get; set; }      // GitHub username
        public string Name { get; set; }     // commit author name
        public string Email { get; set; }    // commit author email
        public string Key { get; set; }      // path to private key
        public string Colour { get; set; }
        // Derived - not persisted, or the JSON fills up with computed junk.
        [ScriptIgnore]
        public string Alias { get { return "github-" + Id; } }
        [ScriptIgnore]
        public Color Dot
        {
            get
            {
                try { return ColorTranslator.FromHtml(Colour); }
                catch { return Color.Gray; }
            }
        }
    }

    public class Config
    {
        public List<Account> Accounts { get; set; }
        public List<string> Roots { get; set; }
        public int Depth { get; set; }
        public Config()
        {
            Accounts = new List<Account>();
            Roots = new List<string>();
            Depth = 3;
        }
    }

    public static class Env
    {
        public const string Begin = "# >>> gh-account-switcher >>>";
        public const string End = "# <<< gh-account-switcher <<<";
        public static readonly string[] Palette =
            { "#2563eb", "#9333ea", "#059669", "#d97706", "#dc2626", "#0891b2" };

        public static string Home
        {
            get
            {
                // GHACCT_HOME lets the setup script and tests point the whole
                // app at a scratch directory instead of the real profile.
                string over = Environment.GetEnvironmentVariable("GHACCT_HOME");
                if (!string.IsNullOrEmpty(over)) return over;
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
        public static string AppDir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }
        public static string ConfigPath
        {
            get { return Path.Combine(Home, ".gh-accounts.json"); }
        }
        public static string SshDir
        {
            get
            {
                string d = Path.Combine(Home, ".ssh");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }
        public static string Fwd(string p) { return p == null ? "" : p.Replace("\\", "/"); }
    }

    public static class Shell
    {
        public static int Run(string exe, string args, out string stdout, out string stderr,
                              int timeoutMs, string cwd)
        {
            stdout = ""; stderr = "";
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                if (!string.IsNullOrEmpty(cwd)) psi.WorkingDirectory = cwd;
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                    stdout = o; stderr = e;
                    return p.HasExited ? p.ExitCode : 1;
                }
            }
            catch (Exception ex) { stderr = ex.Message; return 1; }
        }

        public static string Git(string repo, string args)
        {
            string o, e;
            int rc = Run("git.exe", "-C \"" + repo + "\" " + args, out o, out e, 20000, null);
            return rc == 0 ? o.Trim() : "";
        }

        // For writes (remote set-url, config set/unset) where a failure must
        // reach the user instead of vanishing. `git config --unset` on a key
        // that was never set exits 5 with no meaningful stderr - that case is
        // reported as success since there was nothing to change.
        public static bool GitChecked(string repo, string args, out string error)
        {
            string o, e;
            int rc = Run("git.exe", "-C \"" + repo + "\" " + args, out o, out e, 20000, null);
            if (rc == 0 || (rc == 5 && args.StartsWith("config --unset")))
            {
                error = null;
                return true;
            }
            string first = e.Trim();
            if (first.Length > 0)
            {
                int nl = first.IndexOfAny(new char[] { '\r', '\n' });
                if (nl >= 0) first = first.Substring(0, nl);
            }
            error = first.Length > 0 ? first : ("git exited with code " + rc);
            return false;
        }
    }

    public static class Store
    {
        // Set when Load() had to fall back to an empty Config because the
        // file on disk exists but couldn't be read - lets the caller warn
        // instead of silently saving over the user's real accounts later.
        public static string LoadError;

        public static Config Load()
        {
            LoadError = null;
            try
            {
                if (File.Exists(Env.ConfigPath))
                {
                    var ser = new JavaScriptSerializer();
                    Config c = ser.Deserialize<Config>(File.ReadAllText(Env.ConfigPath));
                    if (c != null)
                    {
                        if (c.Accounts == null) c.Accounts = new List<Account>();
                        if (c.Roots == null) c.Roots = new List<string>();
                        if (c.Depth <= 0) c.Depth = 3;
                        for (int i = 0; i < c.Accounts.Count; i++)
                            if (string.IsNullOrEmpty(c.Accounts[i].Colour))
                                c.Accounts[i].Colour = Env.Palette[i % Env.Palette.Length];
                        return c;
                    }
                    LoadError = "The config file exists but was empty or not valid JSON.";
                }
            }
            catch (Exception ex) { LoadError = ex.Message; }
            return new Config();
        }

        public static void Save(Config c)
        {
            var ser = new JavaScriptSerializer();
            File.WriteAllText(Env.ConfigPath, Pretty(ser.Serialize(c)));
        }

        // JavaScriptSerializer emits one long line; make it diffable by hand.
        static string Pretty(string json)
        {
            var sb = new StringBuilder();
            int indent = 0; bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (inStr) { sb.Append(ch); continue; }
                if (ch == '{' || ch == '[')
                { sb.Append(ch); sb.Append('\n'); sb.Append(new string(' ', ++indent * 2)); }
                else if (ch == '}' || ch == ']')
                { sb.Append('\n'); sb.Append(new string(' ', --indent * 2)); sb.Append(ch); }
                else if (ch == ',')
                { sb.Append(ch); sb.Append('\n'); sb.Append(new string(' ', indent * 2)); }
                else if (ch == ':') sb.Append(": ");
                else sb.Append(ch);
            }
            return sb.ToString();
        }
    }

    public static class Apply
    {
        static void Splice(string path, string block)
        {
            string old = File.Exists(path) ? File.ReadAllText(path) : "";
            string body = Env.Begin + "\n" + block.TrimEnd() + "\n" + Env.End + "\n";
            string result;
            if (old.Contains(Env.Begin) && old.Contains(Env.End))
            {
                result = Regex.Replace(old,
                    Regex.Escape(Env.Begin) + ".*?" + Regex.Escape(Env.End) + "\r?\n?",
                    body.Replace("$", "$$"), RegexOptions.Singleline);
            }
            else
            {
                result = (old.TrimEnd().Length > 0 ? old.TrimEnd() + "\n\n" : "") + body;
            }
            File.WriteAllText(path, result.Replace("\r\n", "\n"));
        }

        public static void All(Config cfg)
        {
            var ssh = new StringBuilder();
            ssh.Append("# Managed by GitHub Account Switcher - edits inside this block are overwritten.\n");
            foreach (Account a in cfg.Accounts)
            {
                ssh.Append("\nHost " + a.Alias + "\n");
                ssh.Append("  HostName github.com\n  User git\n");
                ssh.Append("  IdentityFile " + Env.Fwd(a.Key) + "\n  IdentitiesOnly yes\n");
            }
            Splice(Path.Combine(Env.SshDir, "config"), ssh.ToString());

            var gc = new StringBuilder();
            gc.Append("# Managed by GitHub Account Switcher - edits inside this block are overwritten.\n");
            foreach (Account a in cfg.Accounts)
            {
                string idFile = Path.Combine(Env.Home, ".gitconfig-" + a.Id);
                File.WriteAllText(idFile,
                    "[user]\n\tname = " + a.Name + "\n\temail = " + a.Email + "\n");
                gc.Append("[url \"git@" + a.Alias + ":" + a.Org + "/\"]\n");
                gc.Append("\tinsteadOf = https://github.com/" + a.Org + "/\n");
                gc.Append("\tinsteadOf = git@github.com:" + a.Org + "/\n");
                gc.Append("\tinsteadOf = ssh://git@github.com/" + a.Org + "/\n");
            }
            gc.Append("\n");
            foreach (Account a in cfg.Accounts)
            {
                string idFile = Env.Fwd(Path.Combine(Env.Home, ".gitconfig-" + a.Id));
                // "**" degrades to "*" mid-token and cannot cross "//", so the
                // https and ssh:// forms are spelled out in full.
                string[] pats = {
                    "**" + a.Alias + ":" + a.Org + "/**",
                    "**github.com:" + a.Org + "/**",
                    "https://github.com/" + a.Org + "/**",
                    "ssh://git@github.com/" + a.Org + "/**" };
                foreach (string p in pats)
                {
                    gc.Append("[includeIf \"hasconfig:remote.*.url:" + p + "\"]\n");
                    gc.Append("\tpath = " + idFile + "\n");
                }
            }
            Splice(Path.Combine(Env.Home, ".gitconfig"), gc.ToString());

            // A leftover core.sshCommand points git at some other ssh config,
            // where these aliases do not exist - every rewritten URL would then
            // fail with "Could not resolve hostname". Our aliases live in the
            // default ~/.ssh/config, so this must go.
            string o, e;
            // --file, not --global: --global ignores the Home override and
            // would edit the real profile during a dry run.
            Shell.Run("git.exe", "config --file \"" +
                      Path.Combine(Env.Home, ".gitconfig") + "\" --unset core.sshCommand",
                      out o, out e, 15000, null);
        }

        /// <summary>Confirm each alias actually resolves before trusting it.</summary>
        public static List<string> Check(Config cfg)
        {
            var problems = new List<string>();
            foreach (Account a in cfg.Accounts)
            {
                string o, e;
                string over = Environment.GetEnvironmentVariable("GHACCT_HOME");
                string fArg = string.IsNullOrEmpty(over) ? ""
                    : "-F \"" + Path.Combine(over, ".ssh", "config") + "\" ";
                Shell.Run("ssh.exe", fArg + "-G " + a.Alias, out o, out e, 15000, null);
                if (o.IndexOf("hostname github.com", StringComparison.OrdinalIgnoreCase) < 0)
                    problems.Add(a.Alias + " does not resolve to github.com");
            }
            return problems;
        }
    }

    public static class Gh
    {
        public static bool Verify(Account a, out string who)
        {
            string o, e;
            Shell.Run("ssh.exe",
                "-T -o StrictHostKeyChecking=accept-new -o IdentitiesOnly=yes " +
                "-o IdentityAgent=none -o ConnectTimeout=15 -i \"" + a.Key + "\" git@github.com",
                out o, out e, 40000, null);
            string msg = (o + e).Trim();
            Match m = Regex.Match(msg, "Hi ([^!]+)!");
            if (m.Success) { who = m.Groups[1].Value; return true; }
            string[] lines = msg.Split('\n');
            who = lines.Length > 0 ? lines[0].Trim() : "no response";
            return false;
        }

        public static string MakeKey(string id, string comment)
        {
            string path = Path.Combine(Env.SshDir, "id_ed25519_" + id);
            if (File.Exists(path)) return path;
            string o, e;
            int rc = Shell.Run("ssh-keygen.exe",
                "-t ed25519 -N \"\" -C \"" + comment + "\" -f \"" + path + "\"",
                out o, out e, 60000, null);
            if (rc != 0) throw new Exception(string.IsNullOrEmpty(e) ? o : e);
            return path;
        }

        public static string PubKey(Account a)
        {
            try { return File.ReadAllText(a.Key + ".pub").Trim(); }
            catch { return ""; }
        }
    }

    public class RepoInfo
    {
        public string Name, Path, Url, Email, Last;
        public Account Acct, RemoteAcct;
        public bool Pinned;
        public bool Mismatch
        {
            get { return RemoteAcct != null && Acct != RemoteAcct; }
        }
    }

    public static class Repos
    {
        public static List<string> Find(Config cfg)
        {
            var found = new List<string>();
            foreach (string root in cfg.Roots)
            {
                if (!Directory.Exists(root)) continue;
                Walk(root, 0, cfg.Depth, found);
            }
            found.Sort(delegate(string a, string b)
            {
                return string.Compare(Path.GetFileName(a), Path.GetFileName(b),
                                      StringComparison.OrdinalIgnoreCase);
            });
            return found;
        }

        static void Walk(string dir, int level, int maxDepth, List<string> found)
        {
            try
            {
                bool isRepo = Directory.Exists(Path.Combine(dir, ".git"));
                if (isRepo)
                {
                    found.Add(dir);
                    // Keep descending only from a configured root: a stray
                    // "git init" at the top of a projects folder must not hide
                    // every repo underneath it.
                    if (level > 0) return;
                }
                if (level >= maxDepth) return;
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (name.StartsWith(".") || name == "node_modules" ||
                        name == "venv" || name == "__pycache__") continue;
                    Walk(sub, level + 1, maxDepth, found);
                }
            }
            catch { }
        }

        public static Account ByEmail(Config cfg, string email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            string e = email.ToLowerInvariant();
            foreach (Account a in cfg.Accounts)
            {
                if (e == (a.Email ?? "").ToLowerInvariant()) return a;
                if (e.EndsWith("@users.noreply.github.com"))
                {
                    string local = e.Split('@')[0];
                    int plus = local.LastIndexOf('+');
                    if (plus >= 0) local = local.Substring(plus + 1);
                    if (local == (a.Org ?? "").ToLowerInvariant()) return a;
                }
            }
            return null;
        }

        public static Account ByRemote(Config cfg, string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            string u = url.ToLowerInvariant();
            foreach (Account a in cfg.Accounts)
            {
                string org = (a.Org ?? "").ToLowerInvariant();
                if (org.Length > 0 && (u.Contains("/" + org + "/") || u.Contains(":" + org + "/")))
                    return a;
            }
            return null;
        }

        public static RepoInfo Info(Config cfg, string path)
        {
            var r = new RepoInfo();
            r.Path = path;
            r.Name = Path.GetFileName(path);
            r.Url = Shell.Git(path, "config remote.origin.url");
            r.Email = Shell.Git(path, "config user.email");
            r.Acct = ByEmail(cfg, r.Email);
            r.RemoteAcct = ByRemote(cfg, r.Url);
            r.Pinned = Shell.Git(path, "config --local user.email").Length > 0;
            r.Last = Shell.Git(path, "log -1 --format=%ae");
            return r;
        }

        // Returns null on success, or a short message describing what failed.
        // Partial failure (e.g. remote updated but identity write failed) is
        // still reported rather than left for the caller to discover later.
        public static string Switch(Config cfg, string path, Account acct)
        {
            string url = Shell.Git(path, "config remote.origin.url");
            var prefixes = new List<string> {
                "https://github.com/", "ssh://git@github.com/", "git@github.com:" };
            foreach (Account a in cfg.Accounts) prefixes.Add("git@" + a.Alias + ":");
            string err;
            foreach (string pre in prefixes)
            {
                if (url.StartsWith(pre))
                {
                    string tail = url.Substring(pre.Length);
                    string now = "git@" + acct.Alias + ":" + tail;
                    if (now != url && !Shell.GitChecked(path, "remote set-url origin \"" + now + "\"", out err))
                        return "Could not update the remote URL: " + err;
                    break;
                }
            }
            if (!Shell.GitChecked(path, "config --unset user.name", out err))
                return "Could not clear the local user.name: " + err;
            if (!Shell.GitChecked(path, "config --unset user.email", out err))
                return "Could not clear the local user.email: " + err;
            string eff = Shell.Git(path, "config user.email");
            if (!string.Equals(eff, acct.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (!Shell.GitChecked(path, "config user.name \"" + acct.Name + "\"", out err))
                    return "Could not set user.name: " + err;
                if (!Shell.GitChecked(path, "config user.email \"" + acct.Email + "\"", out err))
                    return "Could not set user.email: " + err;
            }
            return null;
        }
    }
}
