// Usage: imported by gh-project.mjs - the one branch naming rule: branchFor(kind, slug) and slugify(title)

const MAX_SLUG = 50;
const TRANSLITERATION = { ą: "a", ć: "c", ę: "e", ł: "l", ń: "n", ó: "o", ś: "s", ź: "z", ż: "z" };

export const branchFor = (kind, slug) => `${String(kind ?? "").toLowerCase() === "fix" ? "fix" : "feat"}/${slug}`;

export function slugify(title, number) {
  const slug = String(title ?? "")
    .toLowerCase()
    .replace(/[ąćęłńóśźż]/g, (c) => TRANSLITERATION[c])
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");
  if (!slug) return `issue-${number}`;
  if (slug.length <= MAX_SLUG) return slug;
  const cut = slug.slice(0, MAX_SLUG + 1);
  const edge = cut.lastIndexOf("-");
  return edge > 0 ? cut.slice(0, edge) : slug.slice(0, MAX_SLUG);
}
