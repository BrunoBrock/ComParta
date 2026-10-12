import axe from 'axe-core'

/**
 * The accessibility rules axe can check in a test page. Colour contrast needs a real browser, so
 * it is checked on the tokens instead (src/tokens.test.ts).
 */
export async function accessibilityProblems(root: Element = document.body): Promise<string[]> {
  const results = await axe.run(root, { rules: { 'color-contrast': { enabled: false } } })
  return results.violations.map(
    (violation) => `${violation.id}: ${violation.help} (${violation.nodes.map((node) => node.target.join(' ')).join(', ')})`,
  )
}
