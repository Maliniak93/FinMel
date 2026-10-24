export function hasSwatch(root: ParentNode, css: string): boolean {
  const probe = document.createElement('span');
  probe.style.backgroundColor = css;
  return Array.from(root.querySelectorAll<HTMLElement>('*')).some(
    (element) => element.style.backgroundColor === probe.style.backgroundColor,
  );
}
