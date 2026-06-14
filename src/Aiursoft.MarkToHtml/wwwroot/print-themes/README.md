# Print theme plugins

Each folder under this directory is a print theme plugin.

Required files:

- `theme.json`
- `theme.css`

Example `theme.json`:

```json
{
  "id": "my-theme",
  "name": "My Theme",
  "pageBackground": "white",
  "order": 100
}
```

Rules:

- The folder name must match `id`.
- `id` may only contain letters, digits, and `-`.
- `theme.css` should scope all rules under `.print-theme-{id}`.
- `pageBackground` is used for the browser print `@page` background.
