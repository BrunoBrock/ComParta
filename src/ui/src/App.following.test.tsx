import { act, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { en } from './i18n/en.ts'
import { ptBR } from './i18n/pt-BR.ts'
import { setup, sharesSection, video } from './test/appHarness.tsx'

const itemOf = async (name: string) => (await within(sharesSection()).findByText(name)).closest('li')!

describe('the sender follows who is getting a file', () => {
  it('says how many are getting it, how far they are and how long is left', async () => {
    const { bridge } = setup({ shares: [video] })
    await itemOf(video.name)

    act(() =>
      bridge.setShares([
        { ...video, activity: 'sending', receiverCount: 1, percent: 45, finishedCount: 0, secondsLeft: 300 },
      ]),
    )

    const item = await itemOf(video.name)
    expect(within(item).getByText('Sending to 1')).toBeInTheDocument()
    expect(
      within(item).getByText('Sending to 1 person: 45 % · about 5 minutes left. Keep ComParta open.'),
    ).toBeInTheDocument()
    expect(within(item).getByRole('progressbar', { name: video.name })).toHaveAttribute('value', '45')
  })

  it('counts several people and says when they have the whole file', async () => {
    const { bridge } = setup({ shares: [video] })
    await itemOf(video.name)

    act(() =>
      bridge.setShares([
        { ...video, activity: 'sending', receiverCount: 2, percent: 10, finishedCount: 1, secondsLeft: null },
      ]),
    )
    expect(
      within(await itemOf(video.name)).getByText('Sending to 2 people: 10 %. Keep ComParta open.'),
    ).toBeInTheDocument()

    act(() =>
      bridge.setShares([
        { ...video, activity: 'sent', receiverCount: 0, percent: 0, finishedCount: 3, secondsLeft: null },
      ]),
    )
    const item = await itemOf(video.name)
    expect(within(item).getByText('Sent 3 times')).toBeInTheDocument()
    expect(within(item).getByText('3 people have the whole file.')).toBeInTheDocument()
    expect(within(item).queryByRole('progressbar')).not.toBeInTheDocument()
  })

  it('follows a device from the start to the whole file', async () => {
    const { bridge } = setup({ shares: [video], stepMs: 5 })
    await itemOf(video.name)

    act(() => bridge.sendTo(video.id))

    expect(
      within(await itemOf(video.name)).getByText(en['shares.chip.sending'].replace('{count}', '1')),
    ).toBeInTheDocument()
    expect(await within(sharesSection()).findByText(en['shares.chip.sentOnce'])).toBeInTheDocument()
    expect(within(await itemOf(video.name)).getByText(en['shares.status.sentOnce'])).toBeInTheDocument()
  })

  it('reads in Portuguese', async () => {
    const { bridge } = setup({
      shares: [{ ...video, activity: 'sending', receiverCount: 1, percent: 45, finishedCount: 0, secondsLeft: 30 }],
      language: 'pt-BR',
    })

    const item = (await screen.findByText(video.name)).closest('li')!
    expect(within(item).getByText('Enviando para 1')).toBeInTheDocument()
    expect(
      within(item).getByText(
        `Enviando para 1 pessoa: 45 % · falta menos de um minuto. ${ptBR['shares.status.keepOpen']}`,
      ),
    ).toBeInTheDocument()

    act(() =>
      bridge.setShares([
        { ...video, activity: 'sent', receiverCount: 0, percent: 0, finishedCount: 1, secondsLeft: null },
      ]),
    )
    expect(await screen.findByText('Enviado 1 vez')).toBeInTheDocument()
  })

  it('shows Stopped instead while sending is stopped', async () => {
    setup({
      shares: [{ ...video, activity: 'sending', receiverCount: 1, percent: 45, finishedCount: 0, secondsLeft: null }],
      stopped: { sendingStopped: true },
    })

    const item = await itemOf(video.name)
    expect(within(item).getByText(en['stopped.chip'])).toBeInTheDocument()
    expect(within(item).queryByRole('progressbar')).not.toBeInTheDocument()
  })
})
