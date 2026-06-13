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

The `kami` plugin is inspired by the public `tw93/kami` design system, which is MIT licensed. It does not copy kami templates, generated files, or font assets. Fonts referenced by the original kami project may have their own licenses; this plugin only uses local/system fallback fonts.
