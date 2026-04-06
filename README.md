# WrenchWise (Offline + Sync)

This workspace includes:

- `WrenchWise.Mobile` - .NET MAUI Blazor Hybrid Android frontend
- `WrenchWise.Backend` - ASP.NET Core API (Docker-ready)
- `WrenchWise.Shared` - shared models/sync contracts

## Architecture

- App writes data locally first (works fully offline).
- Every local change is queued as a sync operation.
- When you tap **Sync Now**, queued operations are pushed to the backend.
- Backend returns the latest full dataset back to the app.

## Run Backend with Docker + Postgres

From `D:\repo\maintenance app`:

```powershell
docker compose up -d --build
```

Backend endpoints:

- Health: `http://<server-ip>:18080/api/health`
- Sync: `http://<server-ip>:18080/api/sync`

## App Setup

1. Run the MAUI app.
2. Open the **Sync** page.
3. Set API URL to your server:
   - LAN example: `http://192.168.1.25:18080`
   - Tailscale example: `http://100.x.y.z:18080`
4. Tap **Sync Now**.

## Notes

- Default conflict strategy is last-write-wins per entity.
- Sync operations are idempotent on the backend via operation IDs.
- Postgres data is persisted in Docker volume `wrenchwise_pgdata`.
