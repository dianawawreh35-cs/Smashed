# Configurations

One `IEntityTypeConfiguration<T>` per entity, applied automatically by
`CallCenterDbContext.OnModelCreating` via `ApplyConfigurationsFromAssembly`.

These are the authority on how the model maps to PostgreSQL, and they are written
to match [`docs/SCHEMA.md`](../../../../docs/SCHEMA.md) exactly - table and column
names, CHECK constraints, indexes, defaults and the one generated column.

Conventions applied globally in the context rather than repeated here:

- tables and columns are `snake_case`
- `DateTimeOffset` maps to `timestamptz`
- `Guid` primary keys default to `gen_random_uuid()` (needs the `pgcrypto`
  extension, declared on the model)

If a CHECK constraint changes, the string constants in
`CallCenter.Shared/Enums.cs` must change with it - `EnumStringsTests` fails
otherwise, which is the intended tripwire.
