import os
import subprocess
import tempfile
import unittest

from versionsel import latest, version_branches


class VersionBranchesTest(unittest.TestCase):
    def setUp(self):
        prior = os.environ.pop("VERSION", None)
        if prior is not None:
            self.addCleanup(os.environ.__setitem__, "VERSION", prior)
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.addCleanup(os.chdir, os.getcwd())
        self.work = os.path.join(self.tmp.name, "work")
        subprocess.run(["git", "init", "-q", self.work], check=True)
        os.chdir(self.work)

    def set_origin(self, path):
        subprocess.run(["git", "remote", "add", "origin", path], check=True)

    def test_unreachable_origin_raises(self):
        self.set_origin(os.path.join(self.tmp.name, "gone.git"))
        with self.assertRaises(subprocess.CalledProcessError):
            version_branches()

    def test_origin_with_no_version_branches_is_unstable(self):
        bare = os.path.join(self.tmp.name, "bare.git")
        subprocess.run(["git", "init", "-q", "--bare", bare], check=True)
        self.set_origin(bare)
        self.assertEqual(latest(version_branches()), "unstable")


if __name__ == "__main__":
    unittest.main()
