import { test } from '@playwright/test'
import { wildSpin } from './rs-wild'
import { MINUTE } from './static'

test('RS Wild Encounter on Sapphire: greyed choices, the shiny-lead line, ten fled encounters by Spin, the tile and the save unchanged', async () => {
  test.setTimeout(20 * MINUTE)
  await wildSpin('SA-Wild', 10)
})
