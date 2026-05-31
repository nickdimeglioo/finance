# Pipeline Task Manager

A terminal-native looking PyQt5 task manager with SQLite storage.

## Setup

```bash
pip install PyQt5
python app.py
```

`tasks.db` is created automatically in the same directory on first run.

---

## Features

| Feature | Notes |
|---|---|
| Add / Edit tasks | Title, status, priority 1–10, effort (pts), scope, phase, description |
| Status system | 1 Not Started · 2 In Progress · 3 Started · 4 Kinda Done · 5 Done |
| Priority | P1 = Critical (red) · P4–6 = Medium (yellow) · P7–10 = Low (green) |
| Scope | Backend · Frontend · Agent · DB · Compiler · All (multi-select) |
| Comments | Per-task threaded comments with timestamps |
| Audit log | Every field change, creation, deletion, and comment is tracked |
| Filter & sort | Search, status filter, scope filter, sort by any column |
| What's Next | Smart view: incomplete tasks sorted by priority + effort |
| Bulk import | Paste or load CSV/JSON — see format below |
| Export | Export all tasks to CSV |
| Right-click menu | Quick mark Done / In Progress / Delete from table |
| Keyboard shortcuts | Ctrl+N = new task · Ctrl+R = refresh |

---

## Bulk Import Format

### CSV

```
title,description,priority,status,effort,scope,phase
Fix log viewer,,1,1,2,Frontend,Phase 1
Add retry logic,Retry on failure,3,2,5,"Backend,Agent",Phase 2
```

First row must be the header. All fields except `title` are optional.

### JSON

```json
[
  {
    "title": "Fix log viewer",
    "description": "",
    "priority": 1,
    "status": 1,
    "effort": 2,
    "scope": "Frontend",
    "phase": "Phase 1"
  }
]
```

**Field reference:**

| Field | Values |
|---|---|
| `priority` | 1 (highest) – 10 (lowest) |
| `status` | 1=Not Started, 2=In Progress, 3=Started, 4=Kinda Done, 5=Done |
| `effort` | Integer (story points) |
| `scope` | Comma-separated: Backend, Frontend, Agent, DB, Compiler, All |
| `phase` | Any string |

---

## Data Storage

Everything is stored in `tasks.db` (SQLite) in the same directory as `app.py`.

Tables: `tasks` · `comments` · `audit_log`
