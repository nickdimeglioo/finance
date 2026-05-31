# AGENTS.md

This file mirrors the root agent instructions for Codex sessions opened from `.codex`.

Read `../AGENTS.md` first. The important local decisions are:

- Current stack: React + TypeScript + Vite frontend, ASP.NET Core backend, PostgreSQL, Liquibase, S3-compatible storage.
- Reuse `C:\Users\nicky\Desktop\projects\pipeline\saas-template` infrastructure wherever practical.
- Finance local ports are frontend `5273`, backend `8353`, PostgreSQL `5438`, MinIO API `9010`, MinIO console `9011`.
- Use Liquibase for all schema changes.
- Treat `task-board.csv` as the original baseline. For later work, create pass-specific CSVs such as `task-board-pass-2.csv` and update `MASTER_TASKS.txt` with completed IDs and pass numbers.
- Do not push git changes unless explicitly asked.
