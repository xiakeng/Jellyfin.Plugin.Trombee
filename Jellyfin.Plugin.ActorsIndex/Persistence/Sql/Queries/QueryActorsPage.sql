WITH filtered_credits AS (
    SELECT c.actor_key, c.name, c.person_id, m.display_item_id
    FROM credits AS c
    INNER JOIN media_items AS m
        ON m.generation_id = c.generation_id
        AND m.source_item_id = c.source_item_id
    WHERE c.generation_id = $generationId
        AND (($personType = 'Actor' AND c.person_type IN ('Actor', 'GuestStar')) OR c.person_type = $personType)
        AND ($searchTerm IS NULL OR c.name LIKE '%' || $searchTerm || '%' ESCAPE '\' COLLATE NOCASE)
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
),
actor_counts AS (
    SELECT actor_key,
        MAX(name) AS name,
        MAX(person_id) AS person_id,
        COUNT(DISTINCT display_item_id) AS appearances
    FROM filtered_credits
    GROUP BY actor_key
    HAVING COUNT(DISTINCT display_item_id) >= $minimumAppearances
)
SELECT actor_key, name, appearances, person_id
FROM actor_counts
ORDER BY
    CASE WHEN $sortBy = 'Appearances' AND $sortDirection = 'Descending' THEN appearances END DESC,
    CASE WHEN $sortBy = 'Appearances' AND $sortDirection = 'Ascending' THEN appearances END ASC,
    CASE WHEN $sortBy = 'Name' AND $sortDirection = 'Descending' THEN name END COLLATE NOCASE DESC,
    CASE WHEN $sortBy = 'Name' AND $sortDirection = 'Ascending' THEN name END COLLATE NOCASE ASC,
    name COLLATE NOCASE ASC,
    actor_key ASC
LIMIT $limit OFFSET $startIndex;
