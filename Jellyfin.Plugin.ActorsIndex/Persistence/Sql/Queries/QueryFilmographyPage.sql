WITH filmography AS (
    SELECT m.display_item_id,
        MAX(m.name) AS item_name,
        MAX(m.item_type) AS item_type,
        MAX(m.production_year) AS production_year,
        COALESCE(GROUP_CONCAT(DISTINCT NULLIF(c.role, '')), '') AS role
    FROM credits AS c
    INNER JOIN media_items AS m
        ON m.generation_id = c.generation_id
        AND m.source_item_id = c.source_item_id
    WHERE c.generation_id = $generationId
        AND c.actor_key = $actorKey
        AND (($personType = 'Actor' AND c.person_type IN ('Actor', 'GuestStar')) OR c.person_type = $personType)
        AND ($filterLibraries = 0 OR EXISTS (
            SELECT 1
            FROM media_item_libraries AS ml
            INNER JOIN selected_libraries AS sl ON sl.library_id = ml.library_id
            WHERE ml.generation_id = c.generation_id
                AND ml.source_item_id = c.source_item_id
        ))
        AND ($filterVisibleItems = 0 OR EXISTS (
            SELECT 1
            FROM visible_source_items AS vsi
            WHERE vsi.source_item_id = c.source_item_id
        ))
    GROUP BY m.display_item_id
)
SELECT display_item_id, item_name, role, production_year, item_type
FROM filmography
ORDER BY production_year IS NULL ASC,
    production_year DESC,
    item_name COLLATE NOCASE ASC,
    display_item_id ASC
LIMIT $limit OFFSET $startIndex;
