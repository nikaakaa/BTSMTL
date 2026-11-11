const parser = require(process.argv[2]);
let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => input += chunk);
process.stdin.on('end', () => {
  const routes = [], fields = [];
  for (const source of JSON.parse(input)) {
    const file = parser.parse(source.Text, { sourceType: 'module',
      plugins: source.File.endsWith('.tsx') ? ['typescript', 'jsx'] : ['typescript'] });
    const add = (target, node, text, prefix = false) => target.push({ file: source.File,
      line: node.loc.start.line, text, prefix });
    const visit = node => {
      const literal = node.type === 'StringLiteral';
      if (literal && node.value.startsWith('/api/')) add(routes, node, node.value);
      if (node.type === 'TemplateLiteral' && node.quasis[0].value.cooked.startsWith('/api/'))
        add(routes, node.quasis[0], node.quasis[0].value.cooked, node.expressions.length > 0);
      const text = node.type === 'Identifier' ? node.name : literal ? node.value
        : node.type === 'TemplateElement' ? node.value.cooked : null;
      if (text !== null)
        for (const match of text.matchAll(/\bm_[A-Za-z_]\w*\b/g)) add(fields, node, match[0]);
      for (const [key, value] of Object.entries(node))
        if (!['comments', 'leadingComments', 'innerComments', 'trailingComments'].includes(key)) {
          if (Array.isArray(value)) for (const child of value) { if (child?.type) visit(child); }
          else if (value?.type) visit(value);
        }
    };
    visit(file);
  }
  process.stdout.write(JSON.stringify({ node: process.execPath, routes, fields }));
});
