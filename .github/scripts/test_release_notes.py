"""Tests for release_notes.py against a throwaway git repository.

    python -m unittest discover -s .github/scripts -p "test_*.py"
"""
import os
import subprocess
import tempfile
import unittest

import release_notes


class ReleaseNotesTests(unittest.TestCase):
    def setUp(self):
        self.folder = tempfile.TemporaryDirectory()
        self.repo = self.folder.name
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.name", "Someone Private")
        self.git("config", "user.email", "someone@example.com")
        self.git("config", "commit.gpgsign", "false")
        self.git("config", "tag.gpgsign", "false")

    def tearDown(self):
        self.folder.cleanup()

    def git(self, *args):
        return subprocess.run(["git", *args], cwd=self.repo, check=True, capture_output=True, text=True).stdout

    def commit(self, message):
        self.git("commit", "-q", "--allow-empty", "-m", message)

    def merge_pull_request(self, number, title):
        self.git("checkout", "-q", "-b", f"work-{number}")
        self.commit(f"Work for {title}")
        self.git("checkout", "-q", "main")
        self.git("merge", "-q", "--no-ff", f"work-{number}", "-m", f"Merge pull request #{number} from someone/work-{number}", "-m", title)

    def test_lists_only_the_pull_requests_since_the_last_tag(self):
        self.commit("Old feature (#1)")
        self.git("tag", "v0.1.0")
        self.commit("Share from a phone (#2)")
        self.merge_pull_request(3, "Choose where received files go")
        self.commit("Fix a typo directly on main")
        self.git("tag", "v0.2.0")

        since, found = release_notes.pull_requests("v0.2.0", cwd=self.repo)

        self.assertEqual(since, "v0.1.0")
        self.assertEqual(found, [(3, "Choose where received files go"), (2, "Share from a phone")])

    def test_the_first_release_lists_everything(self):
        self.commit("First screen (#1)")
        self.commit("Second screen (#2)")
        self.git("tag", "v0.1.0")

        since, found = release_notes.pull_requests("v0.1.0", cwd=self.repo)

        self.assertIsNone(since)
        self.assertEqual([number for number, _ in found], [2, 1])
        self.assertIn("## What is in this first release", release_notes.notes("v0.1.0", cwd=self.repo))

    def test_a_test_tag_counts_from_the_last_release(self):
        self.commit("Old feature (#1)")
        self.git("tag", "v0.1.0")
        self.commit("New feature (#2)")
        self.git("tag", "v0.2.0-test")

        text = release_notes.notes("v0.2.0-test", cwd=self.repo)

        self.assertIn("## What changed since v0.1.0", text)
        self.assertIn("- New feature (#2)", text)
        self.assertNotIn("Old feature", text)

    def test_no_names_or_emails(self):
        self.commit("Start (#1)")
        self.git("tag", "v0.1.0")
        self.merge_pull_request(4, "Thanks to helper@example.org for the idea")
        self.git("tag", "v0.2.0")

        text = release_notes.notes("v0.2.0", cwd=self.repo)

        self.assertNotIn("@", text)
        self.assertNotIn("Someone Private", text)
        self.assertIn("(#4)", text)

    def test_writes_the_file(self):
        self.commit("Start (#1)")
        self.git("tag", "v0.1.0")
        out = os.path.join(self.repo, "notes.md")
        cwd = os.getcwd()
        os.chdir(self.repo)
        try:
            self.assertEqual(release_notes.main(["release_notes.py", "v0.1.0", out]), 0)
        finally:
            os.chdir(cwd)
        with open(out, encoding="utf-8") as written:
            self.assertIn("- Start (#1)", written.read())


if __name__ == "__main__":
    unittest.main()
