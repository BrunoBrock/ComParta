import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { helpQuestions } from './components/HelpDialog.tsx'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { accessibilityProblems } from './test/axe.ts'
import { railButton, setup, type User } from './test/appHarness.tsx'

type Words = Record<keyof typeof en, string>

async function openHelp(user: User, words: Words) {
  await user.click(await railButton(words['nav.help']))
  return screen.getByRole('dialog', { name: words['help.title'] })
}

describe('the Help page', () => {
  it.each([
    ['English', 'en', en],
    ['Portuguese', 'pt-BR', ptBR],
  ] as const)('answers every question in %s, each answer under its question', async (_, language, words) => {
    const { user } = setup({ language })
    const help = await openHelp(user, words)

    expect(within(help).getByRole('heading', { name: words['help.questions'] })).toBeInTheDocument()
    for (const question of helpQuestions) {
      const asked = within(help).getByText(words[`help.q.${question}`])
      const answer = within(help).getByText(words[`help.a.${question}`])
      expect(answer).not.toBeVisible()
      await user.click(asked)
      expect(answer).toBeVisible()
      expect(asked.closest('details')).toContainElement(answer)
    }
    expect(await accessibilityProblems()).toEqual([])
  })

  it('answers honestly that someone with the invite can get the file', async () => {
    const { user } = setup()
    const help = await openHelp(user, en)

    await user.click(within(help).getByText(en['help.q.strangers']))

    expect(within(help).getByText(en['help.a.strangers'])).toHaveTextContent(/Anyone who gets the invite/)
  })
})
