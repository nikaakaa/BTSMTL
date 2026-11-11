const ts = require(process.argv[2]);
let input = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => input += chunk);
process.stdin.on('end', () => {
  const routes = [], fields = [];
  for (const source of JSON.parse(input)) {
    const file = ts.createSourceFile(source.File, source.Text, ts.ScriptTarget.Latest, true,
      source.File.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    const add = (target, node, text, prefix = false) => target.push({ file: source.File,
      line: file.getLineAndCharacterOfPosition(node.getStart(file)).line + 1, text, prefix });
    const visit = node => {
      const literal = ts.isStringLiteralLike(node);
      if (literal && node.text.startsWith('/api/')) add(routes, node, node.text);
      if (ts.isTemplateExpression(node) && node.head.text.startsWith('/api/')) add(routes, node.head, node.head.text, true);
      if (ts.isIdentifier(node) || literal || node.kind === ts.SyntaxKind.TemplateHead ||
          node.kind === ts.SyntaxKind.TemplateMiddle || node.kind === ts.SyntaxKind.TemplateTail)
        for (const match of node.text.matchAll(/\bm_[A-Za-z_]\w*\b/g)) add(fields, node, match[0]);
      ts.forEachChild(node, visit);
    };
    visit(file);
  }
  process.stdout.write(JSON.stringify({ node: process.execPath, routes, fields }));
});
