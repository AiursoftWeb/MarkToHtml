# How to Add Migrations for MySQL

This guide explains how to create database migrations for the MySQL provider in this project.

## Prerequisites

- Install the EF Core CLI tools globally:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

## Creating a Migration

**Important:** You do NOT need a running MySQL database to create migrations. The `MySqlContextFactory` handles design-time operations.

1. Navigate to the MySQL project directory:
   ```bash
<<<<<<< HEAD
   cd ./src/Aiursoft.Template.MySql/
=======
   cd ./src/Aiursoft.MarkToHtml.MySql/
>>>>>>> template-upgrade-layer
   ```

2. Create a new migration with a descriptive name:
   ```bash
   dotnet ef migrations add YourMigrationName \
     --context "MySqlContext" \
<<<<<<< HEAD
     -s ../Aiursoft.Template/Aiursoft.Template.csproj
=======
     -s ../Aiursoft.MarkToHtml/Aiursoft.MarkToHtml.csproj
>>>>>>> template-upgrade-layer
   ```

3. Review the generated migration file in `./Migrations/` to ensure it matches your expectations.

## Example

```bash
<<<<<<< HEAD
cd ./src/Aiursoft.Template.MySql/
dotnet ef migrations add AddUserProfileTable \
  --context "MySqlContext" \
  -s ../Aiursoft.Template/Aiursoft.Template.csproj
=======
cd ./src/Aiursoft.MarkToHtml.MySql/
dotnet ef migrations add AddUserProfileTable \
  --context "MySqlContext" \
  -s ../Aiursoft.MarkToHtml/Aiursoft.MarkToHtml.csproj
>>>>>>> template-upgrade-layer
```

## Important Notes

- **No Database Required:** Thanks to the design-time factory, you don't need a running MySQL instance to create migrations.
- **Review Migrations:** Always review generated migrations carefully. EF Core might misinterpret schema changes (e.g., renaming a column as drop + add).
- **Startup Project:** The `-s` parameter specifies the startup project, which is necessary for EF Core to load configuration and dependencies.
- **Migrations are NOT Applied Automatically:** Creating a migration only generates the change script. The application will automatically apply pending migrations at startup.

## Removing the Last Migration

If you made a mistake, you can remove the most recent migration:

```bash
<<<<<<< HEAD
dotnet ef migrations remove --context "MySqlContext" -s ../Aiursoft.Template/Aiursoft.Template.csproj
=======
dotnet ef migrations remove --context "MySqlContext" -s ../Aiursoft.MarkToHtml/Aiursoft.MarkToHtml.csproj
>>>>>>> template-upgrade-layer
```

## After Creating Migrations

After creating migrations for MySQL, remember to:
<<<<<<< HEAD
1. Create the corresponding migration for SQLite (see `../Aiursoft.Template.Sqlite/HOW_TO_ADD_MIGRATIONS.md`)
=======
1. Create the corresponding migration for SQLite (see `../Aiursoft.MarkToHtml.Sqlite/HOW_TO_ADD_MIGRATIONS.md`)
>>>>>>> template-upgrade-layer
2. Create migrations for any other supported databases in your project
