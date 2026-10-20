"""Writes or reads the pilot database that check-package.ps1 leaves behind (epic 5 story 5).

    python pilot_db.py make <path>   a database from the first version, with one share
    python pilot_db.py read <path>   prints the schema version and the share's name
"""
import sqlite3
import sys

action, path = sys.argv[1], sys.argv[2]
db = sqlite3.connect(path)
if action == "make":
    db.executescript(
        """
        CREATE TABLE shares (id TEXT PRIMARY KEY, file_path TEXT NOT NULL, name TEXT NOT NULL,
                             size_bytes INTEGER NOT NULL, added_at TEXT NOT NULL);
        CREATE TABLE transfers (id TEXT PRIMARY KEY, name TEXT NULL, size_bytes INTEGER NULL,
                                done INTEGER NOT NULL, added_at TEXT NOT NULL);
        INSERT INTO shares VALUES ('6f1c1a52-6f53-4c39-9d43-2b8d1f0c7a11', 'C:\\pilot\\pilot.bin', 'pilot.bin',
                                   1000, '2026-10-12T10:00:00+00:00');
        PRAGMA user_version = 1;
        """
    )
    db.commit()
else:
    version = db.execute("PRAGMA user_version").fetchone()[0]
    name = db.execute("SELECT name FROM shares").fetchone()[0]
    print(version, name)
