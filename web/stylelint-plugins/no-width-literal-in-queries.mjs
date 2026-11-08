import path from 'node:path';

import stylelint from 'stylelint';

const ruleName = 'skarbiec/no-width-literal-in-queries';
const messages = stylelint.utils.ruleMessages(ruleName, {
  rejected: (query) =>
    `Unexpected width literal in "${query}", use sk-from() or sk-below() from _breakpoints.scss`,
});

const WIDTH_LITERAL = /width[^)]*\d(\.\d+)?(px|em|rem)\b/i;
const PARTIAL = '_breakpoints.scss';

const rule = (primary) => (root, result) => {
  const valid = stylelint.utils.validateOptions(result, ruleName, {
    actual: primary,
    possible: [true],
  });
  if (!valid || path.basename(root.source?.input.file ?? '') === PARTIAL) {
    return;
  }

  root.walkAtRules(/^(media|container)$/i, (atRule) => {
    if (WIDTH_LITERAL.test(atRule.params)) {
      stylelint.utils.report({
        result,
        ruleName,
        node: atRule,
        message: messages.rejected(`@${atRule.name} ${atRule.params}`),
      });
    }
  });
};

rule.ruleName = ruleName;
rule.messages = messages;

export default stylelint.createPlugin(ruleName, rule);
