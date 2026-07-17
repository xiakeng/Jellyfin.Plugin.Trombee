CREATE TABLE credits (
    generation_id INTEGER NOT NULL,
    source_item_id TEXT NOT NULL,
    actor_key TEXT NOT NULL,
    person_id TEXT NULL,
    name TEXT NOT NULL,
    role TEXT NOT NULL,
    person_type TEXT NOT NULL,
    PRIMARY KEY (generation_id, source_item_id, actor_key, person_type, role),
    FOREIGN KEY (generation_id, source_item_id)
        REFERENCES media_items (generation_id, source_item_id) ON DELETE CASCADE
);

CREATE INDEX idx_credits_actor_query
    ON credits (generation_id, person_type, actor_key, source_item_id);
