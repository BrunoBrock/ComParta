"""Release notes for a version tag: one line per pull request merged since the previous v* tag.

    python release_notes.py v1.2.0 notes.md

Only pull request titles are listed, never who wrote them or their emails. Squash merges end their
subject with "(#12)"; merge commits say "Merge pull request #12 from ..." and carry the title in
their body. Other commits (merges of one branch into another, direct pushes) are left out.
"""
import re
import subprocess
import sys

SQUASHED = re.compile(r"^(?P<title>.+?)\s*\(#(?P<number>\d+)\)$")
MERGED = re.compile(r"^Merge pull request #(?P<number>\d+) from ")
EMAIL = re.compile(r"\S+@\S+\.\S+")


def git(*args, cwd=None):
    return subprocess.run(["git", *args], cwd=cwd, check=True, capture_output=True, text=True).stdout


def previous_tag(tag, cwd=None):
    """The newest v* tag before this one on its history, or None for the first release."""
    try:
        return git("describe", "--tags", "--abbrev=0", "--match", "v*", f"{tag}^", cwd=cwd).strip() or None
    except subprocess.CalledProcessError:
        return None


def pull_requests(tag, cwd=None):
    """(number, title) of each pull request merged since the previous tag, newest first."""
    since = previous_tag(tag, cwd)
    span = f"{since}..{tag}" if since else tag
    # One record per commit on main's own line: subject, then body, split by unit separators.
    log = git("log", "--first-parent", "--format=%s%x1f%b%x1e", span, cwd=cwd)
    found = []
    for record in log.split("\x1e"):
        if not record.strip():
            continue
        subject, _, body = record.strip("\n").partition("\x1f")
        subject = subject.strip()
        squashed = SQUASHED.match(subject)
        merged = MERGED.match(subject)
        if merged:
            title = next((line.strip() for line in body.splitlines() if line.strip()), "")
            found.append((int(merged["number"]), title or subject))
        elif squashed:
            found.append((int(squashed["number"]), squashed["title"]))
    return since, found


def notes(tag, cwd=None):
    since, found = pull_requests(tag, cwd)
    lines = [f"## What changed since {since}" if since else "## What is in this first release", ""]
    if found:
        lines += [f"- {EMAIL.sub('', title).strip()} (#{number})" for number, title in found]
    else:
        lines.append("- Small fixes.")
    lines += [
        "",
        "## Installing",
        "",
        "- The **.msix** file is the package Windows installs.",
        "- The **.zip** file is for people without the Store: unzip it and open ComParta.exe (see \"Installing without the Store\" in the README).",
        "- **SHA256SUMS.txt** lists each file's fingerprint, to check a download is complete.",
    ]
    return "\n".join(lines) + "\n"


def main(argv):
    if len(argv) != 3:
        print(__doc__)
        return 2
    with open(argv[2], "w", encoding="utf-8", newline="\n") as out:
        out.write(notes(argv[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
