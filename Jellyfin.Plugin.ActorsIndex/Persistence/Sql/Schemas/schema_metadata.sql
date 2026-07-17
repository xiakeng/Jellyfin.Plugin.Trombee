CREATE TABLE schema_metadata (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    schema_hash TEXT NOT NULL
);
