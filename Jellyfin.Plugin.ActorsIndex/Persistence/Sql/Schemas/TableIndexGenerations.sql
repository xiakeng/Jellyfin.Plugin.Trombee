CREATE TABLE index_generations (
    generation_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    status TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    completed_utc TEXT NULL
);
