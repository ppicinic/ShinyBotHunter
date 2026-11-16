import { test } from '@playwright/test'
import { wildSpin } from './rs-wild'
import { MINUTE } from './static'

test('RS Wild Encounter on Ruby: the Sapphire checks on RU-Wild with five encounters, one address table for both games', async () => {
  test.setTimeout(15 * MINUTE)
  await wildSpin('RU-Wild', 5)
})
