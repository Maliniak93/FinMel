const space = 'var\\(--sk-space-([1-9]|1[0-2])\\)';
const percent = '-?\\d+(\\.\\d+)?%';
const calc = `calc\\((${space}|${percent}|[12]px|-1|[+\\-*/ ])+\\)`;
const base = `${space}|0|auto|inherit|initial|${percent}|[12]px|${calc}`;
const spacingValue = (extra) => `/^(${base}${extra})(\\s+(${base}${extra}))*$/`;

export default {
  customSyntax: 'postcss-scss',
  rules: {
    'declaration-property-value-allowed-list': {
      'font-size': ['/^var\\(--sk-text-(xs|sm|base|lg|xl|2xl|3xl)\\)$/', 'inherit', 'initial'],
      '/^margin(-[a-z-]+)?$/': [spacingValue('|-\\d+(\\.\\d+)?px')],
      '/^(padding(-[a-z-]+)?|inset(-[a-z-]+)?|gap|row-gap|column-gap|top|right|bottom|left)$/': [
        spacingValue(''),
      ],
    },
  },
};
