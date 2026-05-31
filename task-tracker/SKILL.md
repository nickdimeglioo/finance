---
name: task-tracker
description: Use when an agent needs to query, import, update, delete, or export finance project tasks through the local task-tracker/app.py CLI and its SQLite task database.
---

# Finance Task Tracker

Use `task-tracker/app.py` as the API for task data. Do not edit `task-tracker/tasks.db` directly.

## Commands

Run commands from the repository root:

```bash
python3 task-tracker/app.py --list --format json
python3 task-tracker/app.py --next --limit 20 --format json
python3 task-tracker/app.py --get 12 --format json
python3 task-tracker/app.py --add --title "Task title" --priority 2 --effort 3 --status 1 --scope Backend --phase "Pass 4"
python3 task-tracker/app.py --update 12 --status 5 --priority 1
python3 task-tracker/app.py --delete 12
python3 task-tracker/app.py --upload task-board-pass-N.csv --format json
python3 task-tracker/app.py --export task-export.csv
```

Use `--db path/to/tasks.db` when testing against a temporary database.

## Upload Schema

CSV and JSON uploads use this mutation schema:

```csv
insert_type,id,title,description,status,priority,effort,scope,phase
```

- `insert_type` is required for mutation intent: `insert`, `update`, or `delete`. Missing values default to `insert` for backward compatibility.
- `id` is ignored for `insert` and required for `update` or `delete`.
- `priority`, `effort`, and `status` must be numeric.
- Do not provide `created_at` or `updated_at`; the app owns those timestamps.
- For updates, include only fields that should change.

## Agent Workflow

1. Query the current state with `--list`, `--next`, or `--get`.
2. Create a pass-specific CSV for task mutations when a pass adds or finishes work.
3. Upload the CSV with `--upload` and check the returned `inserted`, `updated`, `deleted`, and `errors` counts.
4. Keep `MASTER_TASKS.txt` as the human-readable ledger for pass completion IDs.
5. Leave the original `task-board.csv` unchanged unless the user explicitly asks to rewrite it.
