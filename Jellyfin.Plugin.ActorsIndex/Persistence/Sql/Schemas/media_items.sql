CREATE TABLE media_items (
    generation_id INTEGER NOT NULL,
    source_item_id TEXT NOT NULL,
    display_item_id TEXT NOT NULL,
    name TEXT NOT NULL,
    item_type TEXT NOT NULL,
    production_year INTEGER NULL,
    date_last_saved_utc TEXT NOT NULL,
    PRIMARY KEY (generation_id, source_item_id),
    FOREIGN KEY (generation_id) REFERENCES index_generations (generation_id) ON DELETE CASCADE
);

CREATE INDEX idx_media_items_display
    ON media_items (generation_id, display_item_id);
