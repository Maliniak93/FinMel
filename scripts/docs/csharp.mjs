import path from "node:path";
import { readCode, walkFiles } from "./extract-architecture.mjs";

const PAIRS = { "(": ")", "<": ">", "[": "]", "{": "}" };

export function matchClose(text, openIndex) {
  const open = text[openIndex];
  const close = PAIRS[open];
  let depth = 0;
  for (let i = openIndex; i < text.length; i++) {
    if (text[i] === open) depth++;
    else if (text[i] === close && !(open === "<" && text[i - 1] === "=")) {
      depth--;
      if (depth === 0) return i;
    }
  }
  return -1;
}

export function splitTop(text) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if ("(<[{".includes(c)) depth++;
    else if (")>]}".includes(c) && !(c === ">" && text[i - 1] === "=")) depth--;
    else if (c === "," && depth === 0) {
      parts.push(text.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(text.slice(start));
  return parts.map((p) => p.trim()).filter(Boolean);
}

export function paramTypes(list) {
  const types = [];
  for (const param of splitTop(list)) {
    const noDefault = param.replace(/\[[^\]]*\]/g, " ").split(/\s=\s|=(?!>)/)[0].replace(/\b(this|params|in|ref|out|readonly)\b/g, " ").trim();
    const typePart = noDefault.replace(/\s*\w+\s*$/, "");
    types.push(...(typePart.match(/\w+/g) ?? []));
  }
  return types;
}

const lastName = (name) => name.replace(/<[\s\S]*$/, "").split(".").pop().trim();

export function indexClasses(services) {
  const classes = [];
  for (const service of services) {
    for (const file of walkFiles(service.dir, (n) => n.endsWith(".cs"))) {
      const text = readCode(file);
      const relative = path.relative(service.dir, file).split(path.sep).join("/");
      for (const m of text.matchAll(/\bclass\s+(\w+)/g)) {
        const name = m[1];
        let pos = m.index + m[0].length;
        while (/\s/.test(text[pos] ?? "")) pos++;
        if (text[pos] === "<") pos = matchClose(text, pos) + 1;
        while (/\s/.test(text[pos] ?? "")) pos++;
        let ctorTypes = [];
        if (text[pos] === "(") {
          const close = matchClose(text, pos);
          ctorTypes = paramTypes(text.slice(pos + 1, close));
          pos = close + 1;
        }
        const header = /^[^{;]*/.exec(text.slice(pos))[0];
        const baseList = /^\s*:\s*([\s\S]*?)(?:\bwhere\b[\s\S]*)?$/.exec(header)?.[1] ?? "";
        const bases = splitTop(baseList).map(lastName).filter(Boolean);
        for (const ctor of text.matchAll(new RegExp(`\\b(?:public|internal|protected|private)\\s+${name}\\s*\\(`, "g"))) {
          const open = ctor.index + ctor[0].length - 1;
          ctorTypes.push(...paramTypes(text.slice(open + 1, matchClose(text, open))));
        }
        classes.push({ name, service: service.name, file: relative, bases, ctorTypes: [...new Set(ctorTypes)], text });
      }
    }
  }
  return classes;
}

export function consumersOf(classes, typeNames, exclude) {
  return classes.filter((c) => c.name !== exclude && typeNames.some((t) => c.ctorTypes.includes(t)));
}
