/// <reference types="vite/client" />

const ALLOWLIST: readonly string[] = ['Skarbiec', 'PLN', 'EUR', 'USD', 'GBP', 'CHF'];

const USER_FACING_ATTRIBUTES: readonly string[] = [
  'placeholder',
  'aria-label',
  'matTooltip',
  'title',
  'label',
];

const templates = import.meta.glob('./**/*.html', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const BLOCK_KEYWORD =
  /^@(else if|else|if|for|empty|switch|case|default|let|defer|placeholder|loading|error)\b/;
const INTERPOLATION = /\{\{[\s\S]*?\}\}/g;
const STRING_LITERAL = /'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*"|`(?:[^`\\]|\\.)*`/g;
const LETTER = /\p{L}/u;
const CAPITAL_FIRST = /^\s*\p{Lu}/u;

function withoutAllowlist(text: string): string {
  return text.replace(new RegExp(`\\b(?:${ALLOWLIST.join('|')})\\b`, 'g'), '');
}

function hasLetter(text: string): boolean {
  return LETTER.test(withoutAllowlist(text));
}

function skipBalanced(source: string, start: number, open: string, close: string): number {
  let depth = 0;
  let quote: string | null = null;
  for (let i = start; i < source.length; i++) {
    const char = source[i];
    if (quote) {
      if (char === '\\') {
        i++;
      } else if (char === quote) {
        quote = null;
      }
    } else if (char === "'" || char === '"' || char === '`') {
      quote = char;
    } else if (char === open) {
      depth++;
    } else if (char === close && --depth === 0) {
      return i + 1;
    }
  }
  return source.length;
}

function literalsWithoutTransloco(expression: string, onlyCapitalised: boolean): string[] {
  const found: string[] = [];
  for (const match of expression.matchAll(STRING_LITERAL)) {
    const literal = match[0].slice(1, -1);
    const rest = expression.slice((match.index ?? 0) + match[0].length);
    if (/^\s*\|\s*transloco\b/.test(rest) || !hasLetter(literal)) {
      continue;
    }
    if (onlyCapitalised && !CAPITAL_FIRST.test(withoutAllowlist(literal))) {
      continue;
    }
    found.push(literal);
  }
  return found;
}

function attributeProblem(rawName: string, value: string): string | null {
  const name = rawName.replace(/^\[(?:attr\.)?/, '').replace(/\]$/, '');
  if (!USER_FACING_ATTRIBUTES.includes(name)) {
    return null;
  }
  if (rawName.startsWith('[')) {
    return literalsWithoutTransloco(value, false).length > 0 ? `${rawName}="${value}"` : null;
  }
  return hasLetter(value.replace(INTERPOLATION, '')) ? `${rawName}="${value}"` : null;
}

function textProblems(text: string): string[] {
  const problems: string[] = [];
  const outside = text.replace(INTERPOLATION, ' ').replace(/\s+/g, ' ').trim();
  if (hasLetter(outside)) {
    problems.push(outside);
  }
  for (const interpolation of text.match(INTERPOLATION) ?? []) {
    for (const literal of literalsWithoutTransloco(interpolation, true)) {
      problems.push(`${interpolation.replace(/\s+/g, ' ')} (literal '${literal}')`);
    }
  }
  return problems;
}

function untranslatedIn(source: string): string[] {
  const problems: string[] = [];
  const html = source.replace(/<!--[\s\S]*?-->/g, '');
  let text = '';
  let iconDepth = 0;

  const flushText = () => {
    if (iconDepth === 0) {
      problems.push(...textProblems(text).map((problem) => `text "${problem}"`));
    }
    text = '';
  };

  let i = 0;
  while (i < html.length) {
    const char = html[i];

    if (char === '<' && /[A-Za-z]/.test(html[i + 1] ?? '')) {
      flushText();
      const nameMatch = /^<([A-Za-z][\w-]*)/.exec(html.slice(i));
      const tag = nameMatch![1];
      i += nameMatch![0].length;
      let selfClosing = false;
      for (;;) {
        while (i < html.length && /\s/.test(html[i])) {
          i++;
        }
        if (i >= html.length) {
          break;
        }
        if (html[i] === '>') {
          i++;
          break;
        }
        if (html[i] === '/' && html[i + 1] === '>') {
          selfClosing = true;
          i += 2;
          break;
        }
        const attributeName = /^[^\s=>/]+/.exec(html.slice(i))?.[0] ?? html[i];
        i += attributeName.length;
        let value = '';
        if (html[i] === '=') {
          i++;
          if (html[i] === '"' || html[i] === "'") {
            const end = html.indexOf(html[i], i + 1);
            value = html.slice(i + 1, end);
            i = end + 1;
          } else {
            const unquoted = /^[^\s>]+/.exec(html.slice(i))?.[0] ?? '';
            value = unquoted;
            i += unquoted.length;
          }
        }
        const problem = attributeProblem(attributeName, value);
        if (problem) {
          problems.push(`attribute ${problem} on <${tag}>`);
        }
      }
      if (tag === 'mat-icon' && !selfClosing) {
        iconDepth++;
      }
      continue;
    }

    if (char === '<' && html[i + 1] === '/') {
      flushText();
      const end = html.indexOf('>', i);
      if (/^<\/mat-icon\s*$/.test(html.slice(i, end))) {
        iconDepth = Math.max(0, iconDepth - 1);
      }
      i = end + 1;
      continue;
    }

    if (char === '@' && BLOCK_KEYWORD.test(html.slice(i))) {
      const keyword = BLOCK_KEYWORD.exec(html.slice(i))![1];
      i += keyword.length + 1;
      if (keyword === 'let') {
        while (i < html.length && html[i] !== ';') {
          i += html[i] === "'" || html[i] === '"' ? skipBalanced(html, i, html[i], html[i]) - i : 1;
        }
        i++;
        continue;
      }
      while (/\s/.test(html[i] ?? '')) {
        i++;
      }
      if (html[i] === '(') {
        i = skipBalanced(html, i, '(', ')');
      }
      while (/\s/.test(html[i] ?? '')) {
        i++;
      }
      if (html[i] === '{') {
        i++;
      }
      continue;
    }

    text += char;
    i++;
  }
  flushText();
  return problems;
}

describe('template i18n guard', () => {
  it('scans every template under src/app', () => {
    const files = Object.keys(templates);

    expect(files.length).toBeGreaterThan(30);
    expect(files).toContain('./features/dashboard/dashboard.html');
    expect(files).toContain('./features/deposits/deposit-form-dialog/deposit-form-dialog.html');
    expect(files).toContain(
      './features/assets/asset-form/blocks/instrument-picker/instrument-picker.html',
    );
    expect(files).toContain('./layout/shell/shell.html');
  });

  it('flags untranslated text and lets translated markup through', () => {
    expect(untranslatedIn('<h1>Dashboard</h1>')).toHaveLength(1);
    expect(untranslatedIn('<button>Save <mat-icon>save</mat-icon></button>')).toHaveLength(1);
    expect(untranslatedIn('<input placeholder="Search by ticker" />')).toHaveLength(1);
    expect(untranslatedIn('<a aria-label="Actions for {{ name }}"></a>')).toHaveLength(1);
    expect(untranslatedIn(`<a [attr.aria-label]="'Actions for ' + name"></a>`)).toHaveLength(1);
    expect(untranslatedIn(`<a [matTooltip]="'Matured on ' + date"></a>`)).toHaveLength(1);
    expect(untranslatedIn(`<h2>{{ isEdit ? 'Edit' : 'New' }}</h2>`)).toHaveLength(2);
    expect(untranslatedIn('@if (a) {<p>Nothing yet.</p>} @else {<p>Done</p>}')).toHaveLength(2);
    expect(
      untranslatedIn('<mat-label>Name</mat-label> <mat-error>Required.</mat-error>'),
    ).toHaveLength(2);

    const fine = [
      `<h1>{{ 'dashboard.title' | transloco }}</h1>`,
      `<p>{{ 'asOf' | transloco: { date: formatDate(d) } }}</p>`,
      `<a [attr.aria-label]="'actions.label' | transloco: { name: n }"></a>`,
      `<a [matTooltip]="descriptionTooltip(portfolio)" matTooltipClass="tooltip"></a>`,
      `<mat-icon>more_vert</mat-icon> <mat-icon aria-hidden="true">add</mat-icon>`,
      `<mat-error>{{ form.controls.name.getError('server') }}</mat-error>`,
      `<td>{{ formatMoney(x, 'PLN') }} — {{ y ?? '—' }} ({{ z }}%)</td>`,
      `<h1>Skarbiec</h1> <span>PLN</span>`,
      `<!-- A comment in English --> <p>{{ 'a.b' | transloco }}</p>`,
      `@for (item of items; track item.id) {<li>{{ item.name }}</li>} @empty {<li>{{ 'none' | transloco }}</li>}`,
      `@let label = cond ? 'a' : 'b'; <p>{{ label | transloco }}</p>`,
      `<input matInput type="number" step="0.01" formControlName="quantity" />`,
    ];
    for (const template of fine) {
      expect(untranslatedIn(template), template).toEqual([]);
    }
  });

  it('no template contains untranslated text', () => {
    const untranslated = Object.entries(templates)
      .flatMap(([file, source]) => untranslatedIn(source).map((problem) => `${file}: ${problem}`))
      .sort();

    expect(untranslated).toEqual([]);
  });
});
