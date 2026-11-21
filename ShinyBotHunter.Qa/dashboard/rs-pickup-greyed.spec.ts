// rs-pickup-greyed (docs/specs/rs-pickup.md, QA; FR-1): `rs.qa-route101`, a Mudkip alone, no Pickup member. The Pickup box
// is disabled with its reason on the dashboard; API Starts with Pickup and Train Type None, and with Standard, are 400s with
// the HGSS sentences; no event. The plan's run line (#490, Q1a): `npx playwright test rs-pickup-greyed`.
import { expect, test, type Page } from '@playwright/test'
import { shotAll, step } from './driver'
import {
  ACTION, cancelBot, dashboardState, flashUnchanged, installPristine, launchRs, offered, PHASE, removeCheats, teardown
} from './rs-wild'
import { MUDKIP, readParty } from './rs-training'
import { API, backendLog, MINUTE, shutdown, startFrame, type Frame } from './static'

const ROM = 'SA-Wild'
const NO_PICKUP = 'No Pokémon in your party has the Pickup ability.'
const NO_TRAIN = 'Pickup needs a train mode: the Pokémon must be knocked out, not fled from.'

async function startWithPickup(extra: Record<string, unknown>): Promise<{ status: number; body: string }> {
  const res = await fetch(`${API}/bot/startAction/0`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ EventName: ACTION, PhaseName: PHASE, TargetType: 'ShinyTarget', EncounterType: 'Spin', Pickup: true, ...extra })
  })
  return { status: res.status, body: await res.text() }
}

test('RS Pickup rs-pickup-greyed: no Pickup member, the box disabled with its reason, both API Starts 400s, no event', async () => {
  test.setTimeout(10 * MINUTE)
  const frame: Frame = { backend: null, app: null }
  removeCheats()
  installPristine(ROM)
  try {
    const admin = await startFrame(frame)
    const pages: Record<string, Page> = { admin }
    const card = await launchRs(admin, ROM, 'emulator')
    await shotAll('01-launched', pages)
    const party = await readParty()
    step(`party with the bot idle: ${JSON.stringify(party)}`)
    expect(party.map((m) => [m.species, m.checksumOk]), 'the qa-route101 save\'s party: one Mudkip').toEqual([[MUDKIP, true]])

    // "the box disabled with No Pokémon in your party has the Pickup ability.": the box shows once a train type is chosen.
    await card.getByTestId('param-PhaseName').locator('select').selectOption(PHASE)
    await card.getByTestId('param-TrainType').locator('select').selectOption('Standard')
    const field = card.getByTestId('param-Pickup')
    const box = field.locator('input[type="checkbox"]')
    await expect(box, 'no Pickup checkbox with train Standard').toBeAttached({ timeout: 30_000 })
    const label = ((await field.locator('label').textContent()) ?? '').trim()
    step(`Pickup checkbox: "${label}", disabled ${await box.isDisabled()}`)
    expect(label, '"the box disabled with its reason": the label').toBe(`Pickup (${NO_PICKUP})`)
    expect(await box.isDisabled(), '"the box disabled": it is enabled').toBe(true)
    await shotAll('02-greyed', pages)

    // "both Starts 400 with the HGSS sentences", and no event.
    const from = backendLog().length
    const noTrain = await startWithPickup({ TrainType: 'None' })
    step(`startAction with Pickup and TrainType None: ${noTrain.status} "${noTrain.body}"`)
    expect([noTrain.status, noTrain.body], 'the Start with Pickup and no train mode').toEqual([400, NO_TRAIN])
    const noMember = await startWithPickup({ TrainType: 'Standard', TargetLevel: 101 })
    step(`startAction with Pickup and TrainType Standard: ${noMember.status} "${noMember.body}"`)
    expect([noMember.status, noMember.body], 'the Start with Pickup and no Pickup member').toEqual([400, NO_PICKUP])
    const state = await dashboardState()
    step(`dashboard state: ${JSON.stringify(state)}`)
    expect(state?.eventState, 'an event started').toBe('None')
    await expect(offered(card), 'the Wild Encounter form is gone').toBeVisible()
    expect(backendLog().slice(from).filter((l) => /\[Pickup\]|\[Orchestrator\] State:/.test(l)), 'a [Pickup] or state line after the refused Starts')
      .toEqual([])
    await shotAll('03-refused', pages)

    await cancelBot(card)
    await shotAll('04-cancelled', pages)
    expect(flashUnchanged(ROM), `the ${ROM} SaveRAM's first 131,072 bytes changed`).toBe(true)
    await shutdown(frame, admin)
  } finally {
    await teardown(frame, ROM)
  }
})
