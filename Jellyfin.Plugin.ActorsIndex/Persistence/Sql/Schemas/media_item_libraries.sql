CREATE TABLE media_item_libraries (
    generation_id INTEGER NOT NULL,
    source_item_id TEXT NOT NULL,
    library_id TEXT NOT NULL,
    PRIMARY KEY (generation_id, source_item_id, library_id),
    FOREIGN KEY (generation_id, source_item_id)
        REFERENCES media_items (generation_id, source_item_id) ON DELETE CASCADE
);

CREATE INDEX idx_media_item_libraries_filter
    ON media_item_libraries (generation_id, library_id, source_item_id);
