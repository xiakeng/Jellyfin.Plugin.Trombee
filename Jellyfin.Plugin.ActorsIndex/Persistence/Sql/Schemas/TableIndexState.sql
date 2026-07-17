CREATE TABLE index_state (
    singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
    active_generation INTEGER NULL REFERENCES index_generations (generation_id),
    last_success_utc TEXT NULL,
    last_incremental_watermark_utc TEXT NULL
);

INSERT INTO index_state (singleton_id) VALUES (1);
