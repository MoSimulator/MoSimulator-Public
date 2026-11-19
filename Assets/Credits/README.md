# Credits

`credits.json` is the credits content. Editing it is all that is needed to add, remove or reorder
people - nothing in a prefab has to be touched.

Each entry under `sections` becomes one block on the credits page, in the order they appear here:

```json
{
  "layout": "list",
  "header": "Admins",
  "people": [
    { "name": "A Person", "title": "What They Did" }
  ]
}
```

- `layout` is `list`, `grid` or `list long`:
  - `list` gives each person their own row with a title beside them, so `title` is required.
  - `grid` packs names four to a row and ignores `title`.
  - `list long` is a list row holding a block of text rather than a name: one wide bubble, nothing on
    the right, and no `title`. The text keeps its designed size and wraps, and the bubble, the row and
    the section all grow to however many lines that comes to. How wide the bubble is, is the
    `List Long > Long Bubble Width` field on the Credits Populator.
- `header` is shown exactly as typed.
- Every card is as wide as the text in it, at the size the prefab draws that text - nothing is shrunk
  to fit. In a grid, a row takes names until none of those left would fit, bringing a later name
  forward when the next one will not go, and then shares the room still spare between that row's cards
  so the row comes out flush. The last row of a section is left as it falls.

The page is built while the main menu loads, so opening the credits is immediate no matter how
quickly it is opened.
