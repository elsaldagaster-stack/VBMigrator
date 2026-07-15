CREATE TABLE IF NOT EXISTS patterns (
    id          TEXT PRIMARY KEY,
    tag         TEXT NOT NULL,
    vb_template TEXT NOT NULL,
    cs_template TEXT NOT NULL,
    embedding   BLOB,
    source      TEXT NOT NULL DEFAULT 'seed',
    applied     INTEGER NOT NULL DEFAULT 0,
    successes   INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL,
    UNIQUE(tag, vb_template)
);

CREATE INDEX IF NOT EXISTS ix_patterns_tag ON patterns(tag);

CREATE TABLE IF NOT EXISTS translation_log (
    id              TEXT PRIMARY KEY,
    pattern_id      TEXT REFERENCES patterns(id),
    file_path       TEXT NOT NULL,
    method_name     TEXT,
    vb_input        TEXT NOT NULL,
    cs_output       TEXT NOT NULL,
    was_corrected   INTEGER NOT NULL DEFAULT 0,
    human_cs        TEXT,
    compiler_passed INTEGER NOT NULL DEFAULT 0,
    confidence      REAL NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL
);

CREATE VIEW IF NOT EXISTS pattern_stats AS
SELECT tag,
       COUNT(*) as total_patterns,
       SUM(applied) as total_applied,
       CAST(SUM(successes) AS REAL) / NULLIF(SUM(applied), 0) as success_rate
FROM patterns GROUP BY tag;
