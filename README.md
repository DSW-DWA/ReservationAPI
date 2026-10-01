# Restaurant Seating API

## Run

Start Docker Desktop, then:

```bash
cp .env.example .env
```

Set `MSSQL_SA_PASSWORD` in `.env`, then run:

```bash
docker compose up --build -d
```

Swagger: http://localhost:8080/swagger

The database starts empty. Add tables through the API.

## API

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/tables` | Add a table: `{"capacity":4}` (2–6 seats). |
| POST | `/api/groups` | Add a group: `{"size":2}` (1–6 guests). Seat it or queue it. |
| POST | `/api/groups/{id}/leave` | Remove a waiting group from the queue. |
| POST | `/api/groups/{id}/complete` | Complete a seated group's visit and seat waiting groups. |
| GET | `/api/restaurant` | All tables, active groups and up to 100 closed groups. Use `?historyBeforeId=<nextHistoryBeforeId>` for more history. |
| GET | `/health` | Database connectivity: `200` healthy, `503` unavailable. |

Groups share tables but are never split. Empty suitable tables have priority. The queue follows arrival order, skipping groups that do not fit. Adding a table also processes the queue.
