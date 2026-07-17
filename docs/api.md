# HTTP API

All Trombee routes use the `/Trombee` prefix and require an authenticated Jellyfin user. Administrative maintenance routes additionally require Jellyfin's `RequiresElevation` policy.

Implementation: [`ActorsIndexController.cs`](../Jellyfin.Plugin.ActorsIndex/Api/ActorsIndexController.cs).

## Routes

| Method and route | Access | Purpose |
| --- | --- | --- |
| `GET /Trombee/ping` | Authenticated | Basic plugin health response. |
| `GET /Trombee/config` | Authenticated | Current enabled, minimum appearances, role, and monitoring settings. |
| `GET /Trombee/service-status` | Authenticated | Active-generation availability, ID, and incremental watermark. |
| `GET /Trombee/Pages/Browse` | Authenticated | Embedded actor browse HTML used by Plugin Pages. |
| `GET /Trombee/actors-index` | Authenticated | Paged actor summaries. |
| `GET /Trombee/actors/{actorKey}/items` | Authenticated | Paged filmography for one stable actor key. |
| `GET /Trombee/libraries` | Authenticated | Top-level libraries visible to the caller. |
| `GET /Trombee/library-stats` | Authenticated | Simple root library diagnostics. |
| `GET /Trombee/repository` | Authenticated | Minimal repository-shaped plugin metadata response. |
| `POST /Trombee/refresh-people` | Administrator | Queue full metadata and image replacement for every Person item. |
| `GET /Trombee/check-update` | Administrator | Compare the installed version with the xiakeng manifest. |
| `POST /Trombee/self-update` | Administrator | Download, verify, extract, and copy the latest fork release. |

## Actor Paging

Example:

```http
GET /Trombee/actors-index?startIndex=0&limit=60&searchTerm=Hugo%20Weaving&sortBy=appearances&sortOrder=descending&personType=Actor
```

| Parameter | Default | Behavior |
| --- | --- | --- |
| `startIndex` | `0` | Negative values are clamped to zero. |
| `limit` | `60` | Clamped to 1 through 200. |
| `searchTerm` | none | Case-insensitive substring match on person name. |
| `sortBy` | `appearances` | `name` selects name sorting; other values use appearances. |
| `sortOrder` | `descending` | `ascending` or `asc` selects ascending order. |
| `personType` | `Actor` | Jellyfin person kind. Actor includes both Actor and GuestStar credits. |
| `libraryIds` | all visible | Comma-separated top-level library GUIDs. Invalid values are ignored and duplicates removed. |

Response shape:

```json
{
  "actors": [
    {
      "actorKey": "f427cac73b374c312ed7b3d5ed1d1a55",
      "personId": "f427cac73b374c312ed7b3d5ed1d1a55",
      "name": "Hugo Weaving",
      "appearances": 10
    }
  ],
  "totalRecordCount": 1,
  "startIndex": 0,
  "limit": 60
}
```

`actorKey` is the required path key for filmography. `personId` may be null when no real Jellyfin Person item can be resolved; the SPA then uses a placeholder rather than issuing an invalid image request.

## Filmography Paging

Example:

```http
GET /Trombee/actors/f427cac73b374c312ed7b3d5ed1d1a55/items?startIndex=0&limit=60&personType=Actor
```

The paging and library parameters follow the actor endpoint. Results are sorted by known production year descending, then name and display item ID.

```json
{
  "items": [
    {
      "itemId": "046f57e6dd1f632d0fbebff2d2d9c6b4",
      "name": "V for Vendetta",
      "role": "V",
      "productionYear": 2006,
      "itemType": "Movie"
    }
  ],
  "totalRecordCount": 10,
  "startIndex": 0,
  "limit": 60
}
```

Episodes are grouped under the parent series display ID, preventing one series from appearing once per episode.

## User and Library Filtering

The controller resolves the calling Jellyfin user. `ActorsIndexService` then asks Jellyfin for the movie, series, and episode IDs currently visible to that user, optionally scoped to selected top-level libraries.

The store loads those IDs into a temporary table and intersects them with indexed credits. This preserves library permissions and parental controls without rebuilding an index per user.

## Administrative Actions

### Refresh people

`POST /Trombee/refresh-people` enumerates every Jellyfin Person and queues:

- full metadata refresh;
- full image refresh;
- replacement of all metadata and images.

This can be expensive on a large library and is not required for normal index updates.

### Check and install updates

The update routes read the fork's `manifest.json`, choose the highest semantic `version`, and compare it with the loaded assembly version. Installation downloads the ZIP, validates the manifest MD5 integrity checksum when present, extracts it to a temporary directory, and replaces DLL, JSON, and PNG files in the plugin directory. Jellyfin must restart to load the new assembly.
