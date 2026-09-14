#!/usr/bin/env bash
# Mirror the Windows GitHub Accounts setup into WSL.
#
# Windows and WSL have separate ~/.ssh and ~/.gitconfig, and WSL's ssh refuses
# keys living on /mnt/c because they always look world-readable. This copies the
# keys in with mode 600 and writes the same managed blocks on the Linux side.
#
#   bash wsl-sync.sh
set -euo pipefail

winhome=$(wslpath "$(cmd.exe /c 'echo %USERPROFILE%' 2>/dev/null | tr -d '\r')")
cfg="$winhome/.gh-accounts.json"
[ -f "$cfg" ] || { echo "No config at $cfg - run the Windows app first."; exit 1; }
echo "reading $cfg"

ts=$(date +%Y%m%d-%H%M%S)
for f in "$HOME/.gitconfig" "$HOME/.ssh/config"; do
  [ -f "$f" ] && cp "$f" "$f.bak-$ts"
done
mkdir -p "$HOME/.ssh"; chmod 700 "$HOME/.ssh"

python3 - "$cfg" "$HOME" "$winhome" <<'PY'
import json, os, re, shutil, sys

cfg_path, home, winhome = sys.argv[1], sys.argv[2], sys.argv[3]
BEGIN, END = "# >>> gh-account-switcher >>>", "# <<< gh-account-switcher <<<"
cfg = json.load(open(cfg_path, encoding="utf-8-sig"))
accounts = cfg.get("Accounts") or []
if not accounts:
    sys.exit("no accounts in the config")

def to_wsl(p):
    p = p.replace("\\", "/")
    if len(p) > 1 and p[1] == ":":
        return "/mnt/" + p[0].lower() + p[2:]
    return p

def splice(path, block):
    old = open(path, encoding="utf-8", errors="replace").read() if os.path.exists(path) else ""
    body = "%s\n%s\n%s\n" % (BEGIN, block.rstrip(), END)
    if BEGIN in old and END in old:
        new = re.sub(re.escape(BEGIN) + r".*?" + re.escape(END) + r"\n?",
                     lambda _: body, old, flags=re.S)
    else:
        new = (old.rstrip() + "\n\n" if old.strip() else "") + body
    open(path, "w", encoding="utf-8", newline="\n").write(new)

ssh_lines = ["# Managed by GitHub Account Switcher (wsl-sync.sh)."]
git_lines = list(ssh_lines)
for a in accounts:
    aid, org = a["Id"], a["Org"]
    src = to_wsl(a["Key"])
    dst = os.path.join(home, ".ssh", "id_ed25519_" + aid)
    if os.path.exists(src):
        shutil.copyfile(src, dst)
        os.chmod(dst, 0o600)
        if os.path.exists(src + ".pub"):
            shutil.copyfile(src + ".pub", dst + ".pub")
        print("  key -> %s (0600)" % dst)
    else:
        print("  !! key not found: %s" % src)
    ssh_lines += ["", "Host github-%s" % aid, "  HostName github.com", "  User git",
                  "  IdentityFile %s" % dst, "  IdentitiesOnly yes"]
    idf = os.path.join(home, ".gitconfig-%s" % aid)
    open(idf, "w", encoding="utf-8", newline="\n").write(
        "[user]\n\tname = %s\n\temail = %s\n" % (a["Name"], a["Email"]))
    git_lines += ['[url "git@github-%s:%s/"]' % (aid, org),
                  "\tinsteadOf = https://github.com/%s/" % org,
                  "\tinsteadOf = git@github.com:%s/" % org,
                  "\tinsteadOf = ssh://git@github.com/%s/" % org]

git_lines.append("")
for a in accounts:
    aid, org = a["Id"], a["Org"]
    idf = os.path.join(home, ".gitconfig-%s" % aid)
    # "**" degrades to "*" mid-token and cannot cross "//", so the https and
    # ssh:// forms are spelled out in full rather than prefixed with "**".
    for pat in ("**github-%s:%s/**" % (aid, org), "**github.com:%s/**" % org,
                "https://github.com/%s/**" % org, "ssh://git@github.com/%s/**" % org):
        git_lines += ['[includeIf "hasconfig:remote.*.url:%s"]' % pat, "\tpath = %s" % idf]

splice(os.path.join(home, ".ssh", "config"), "\n".join(ssh_lines))
os.chmod(os.path.join(home, ".ssh", "config"), 0o600)
splice(os.path.join(home, ".gitconfig"), "\n".join(git_lines))
print("  wrote ~/.ssh/config and ~/.gitconfig")
PY

# A stale core.sshCommand points git at a different ssh config where these
# aliases do not exist, and every push fails to resolve the host.
git config --global --unset core.sshCommand 2>/dev/null || true

echo
echo "checking aliases:"
python3 -c "
import json,sys
for a in json.load(open(sys.argv[1], encoding='utf-8-sig'))['Accounts']: print('github-'+a['Id'])
" "$cfg" | while read -r alias; do
  printf '  %-26s %s\n' "$alias" \
    "$(ssh -T -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 "git@$alias" 2>&1 | head -1)"
done
