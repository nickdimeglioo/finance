# Shared Reference

This project is intended to reuse infrastructure from:

`C:\Users\nicky\Desktop\projects\pipeline\saas-template`

Current direct reuse in pass 2:

- Backend references `api/SaasTemplate/SqlMapper/SqlMapper.csproj` so finance domain models can use the established `[TableName]` and `[PrimaryKey]` mapping attributes.

Future reuse targets:

- Authentication/account screens and backend identity services.
- Email abstractions if finance notifications are sent.
- Redis cache only if repeated report/import workloads need it.
- Frontend `schema.ts`, `index.css`, HTTP/service, auth store, and reusable UI component patterns.
- S3 storage implementation patterns from the pipeline server.

