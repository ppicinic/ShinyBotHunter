import { describe, expect, it } from 'vitest'
import type { HuntPlan } from '../../types/api'
import { loadPlans } from './HuntPlanEditor'

const plan = (name: string): HuntPlan => ({ game: 'HGSS', name, default: '36-land', rules: [] })
const ok = (list: HuntPlan[]): Response => new Response(JSON.stringify(list), { status: 200 })

describe('loadPlans', () => {
  it('shows the list it read', async () => {
    const shown: [HuntPlan[] | null, string | null][] = []
    await loadPlans(async () => ok([plan('Johto')]), { current: 0 }, (list, error) => shown.push([list, error]))
    expect(shown).toEqual([[[plan('Johto')], null]])
  })

  it('drops an earlier read that answers after a later one', async () => {
    const seq = { current: 0 }
    const shown: string[][] = []
    let answerFirst: (r: Response) => void = () => undefined
    const first = loadPlans(() => new Promise((resolve) => { answerFirst = resolve }), seq, (list) => shown.push(list!.map((p) => p.name)))
    await loadPlans(async () => ok([plan('Johto')]), seq, (list) => shown.push(list!.map((p) => p.name)))
    answerFirst(ok([]))
    await first
    expect(shown).toEqual([['Johto']])
  })

  it('drops a read answered after the unmount bumped the sequence', async () => {
    const seq = { current: 0 }
    const shown: unknown[] = []
    let answer: (r: Response) => void = () => undefined
    const read = loadPlans(() => new Promise((resolve) => { answer = resolve }), seq, (list) => shown.push(list))
    seq.current++
    answer(ok([plan('Johto')]))
    await read
    expect(shown).toEqual([])
  })

  it('shows the server\'s sentence for a refused read', async () => {
    const shown: [HuntPlan[] | null, string | null][] = []
    await loadPlans(async () => new Response('A refusal sentence', { status: 400 }), { current: 0 }, (list, error) => shown.push([list, error]))
    expect(shown).toEqual([[null, 'A refusal sentence']])
  })
})
