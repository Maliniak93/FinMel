export default {
  customSyntax: 'postcss-scss',
  rules: {
    'declaration-property-value-allowed-list': {
      'font-size': ['/^var\\(--sk-text-(xs|sm|base|lg|xl|2xl|3xl)\\)$/', 'inherit', 'initial'],
    },
  },
};
