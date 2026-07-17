# Trombee — Jellyfin Plugin

Browse all the actors in your Jellyfin library on a single screen: a full grid with photos, appearance counts, and a native-style detail view with biography and filmography.

![Trombee — actors grid](Jellyfin.Plugin.ActorsIndex/Images/screenshot-grid.jpg)

![Trombee — actor detail](Jellyfin.Plugin.ActorsIndex/Images/screenshot-actor.jpg)

## How This Fork Differs

This fork keeps the original Trombee feature set while improving the experience and performance for large libraries:

- **Better single-page experience** — smoother in-page navigation, stable browser history, actor detail transitions, and responsive paging.
- **Persistent actor data** — derived actor data is stored in SQLite under Jellyfin's plugin data path instead of being rebuilt for page requests and held only in a short-lived memory cache.
- **Server-side paging** — the browser requests pages with `startIndex` and `limit`, so it receives only the records needed for the current view.
- **Background maintenance** — a daily incremental task, optional immediate library-change monitoring, and a manual full rebuild keep SQLite synchronized with Jellyfin.
- **Large-library performance** — actor and filmography pages query indexed SQLite data instead of scanning the complete Jellyfin library at request time.

Jellyfin remains the source of truth. The SQLite database contains disposable derived data and can be rebuilt at any time.

## Features

- **Actors grid** — all actors/actresses, sortable by number of appearances or name A→Z, with photos
- **Real-time search** — filter by name as you type
- **Person type filter** — switch between Actors, Directors, Writers, and other credited roles ("Actor" also includes Guest Star credits)
- **Library filter** — restrict results to one or more specific libraries, scoped to what you have access to
- **Native-style actor detail** — click an actor to see their photo, biography, birth date, and a poster grid of their filmography (movies and TV series — episodes are automatically grouped under their parent series)
- **Pagination** — configurable page size (default 60 actors per page)
- **Appearance filter** — hide actors with fewer than N appearances
- **Automatic update** — updates itself from the settings page, no manual build required
- **Non-admin access** — optionally expose the actors page to all users (not just admins) via Plugin Pages, scoped to each user's library permissions
- **Internal caching** — results are cached for 10 minutes so repeat visits load instantly, even on large libraries

## Requirements

- Jellyfin **10.11.x** or later
- .NET 9 (included in Jellyfin 10.11+)

## Installation

### Method A — Custom repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**
2. Click **+** and add:
   - Name: `Trombee`
   - URL: `https://raw.githubusercontent.com/drbuju/Jellyfin.Plugin.Trombee/main/manifest.json`
3. Go to **Dashboard → Plugins → Catalog**, find **Trombee**, click **Install**
4. Restart Jellyfin

### Method B — Manual installation

1. Download the latest `Jellyfin.Plugin.Trombee_x.x.x.x.zip` from the [Releases](https://github.com/drbuju/Jellyfin.Plugin.Trombee/releases) page
2. Extract and copy all the files into Jellyfin's plugin folder:
   - **Linux**: `/var/lib/jellyfin/plugins/Trombee/`
   - **Windows**: `%LOCALAPPDATA%\jellyfin\plugins\Trombee\`
3. Restart Jellyfin

---

## Making the actors page available to non-admin users

By default, Jellyfin only exposes plugin pages to server administrators (via the Dashboard). Trombee's actors grid is a browsing feature meant for **all** users, so it can optionally be exposed outside the Dashboard using the community **Plugin Pages** plugin.

### Prerequisites

Install these two plugins (in this order) from their custom repository, **before** Trombee can register its user-facing page:

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**, click **+**, and add:
   - Name: `IAmParadox27`
   - URL: `https://www.iamparadox.dev/jellyfin/plugins/manifest.json`
2. Go to **Dashboard → Plugins → Catalog** and install, in order:
   - **File Transformation**
   - **Plugin Pages**
3. Restart Jellyfin after each install (or once at the end, then verify both loaded successfully in **Dashboard → Logs**)

### How it works

Once both plugins are installed and Jellyfin has restarted, Trombee automatically registers its actors page with Plugin Pages on startup — no configuration needed on your part. You'll see a log line confirming it:

```
Trombee browse page registered with Plugin Pages successfully.
```

Any signed-in user (not just admins) can then reach the page from the **hamburger menu**, under the section Plugin Pages adds (shown alongside other user-facing plugin pages, e.g. "Modular Home" if you use Home Screen Sections). The page itself only shows actors from the libraries that user actually has access to — it respects the same library permissions and parental controls as the rest of Jellyfin.

The **Settings** button (⚙) inside the actors page is automatically hidden for non-admin users, and the underlying settings page — along with all maintenance actions (actor image refresh, self-update) — is locked to administrators only, both in the interface and at the API level.

> If Plugin Pages isn't installed, the actors page remains reachable only through **Dashboard → Plugins → Trombee** (admin-only), exactly like before.

### Troubleshooting

- **"Page not found" right after installing Plugin Pages** — your browser likely cached an old version of the Jellyfin web client before Plugin Pages patched it. Do a hard refresh (Ctrl+Shift+R) or clear the site's cache, then try again.
- **Still not visible** — check **Dashboard → Logs** for a line containing `PluginPagesRegistrationService`; a warning there means Plugin Pages wasn't detected at Trombee's startup (double-check both prerequisite plugins are installed **and** Jellyfin was restarted after installing them, not before).
- **"Settings" button on the plugin's Dashboard page opens the wrong page** — usually a stale browser cache too; hard-refresh and check again.

---

## Updating the plugin

If a red error appears when using any of the settings-page actions after a Jellyfin update, the installed build was likely compiled for a previous Jellyfin version and is no longer compatible.

**Fix in 3 clicks:**

1. Dashboard → Plugins → Trombee → **"Check for updates"**
2. If a newer version appears → click **"Download and install update"**
3. Click **"Restart Jellyfin"**

✅ No terminal, no manual build.

> If the restart button stops the server but doesn't bring it back, that's not Trombee — it's how Jellyfin's own restart API behaves depending on how the server is deployed (systemd, Docker, etc.). Restart it manually from your OS/service manager in that case.

---

## How automatic updates work

The plugin updates itself without you having to build anything:

1. When Jellyfin releases a new version, **Renovate** automatically opens a Pull Request on this repository to update dependencies
2. Once the PR is merged, **GitHub Actions** builds the plugin, creates a signed ZIP, and publishes a new [Release](https://github.com/drbuju/Jellyfin.Plugin.Trombee/releases)
3. You click **"Check for updates"** in the plugin's settings — everything else happens automatically

## Building from source (developers only)

```bash
git clone https://github.com/drbuju/Jellyfin.Plugin.Trombee.git
cd Jellyfin.Plugin.ActorsIndex
dotnet publish --configuration=Release Jellyfin.Plugin.ActorsIndex.sln
```

Output: `Jellyfin.Plugin.ActorsIndex/bin/Release/net9.0/publish/`

## License

[GPL-3.0](LICENSE)
