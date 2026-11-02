import { readFileSync } from 'node:fs'
import { homedir } from 'node:os'
import { join } from 'node:path'
import { expect, type Browser, type Page } from '@playwright/test'

// Test-server accounts from the agent credentials file. Values are never printed or written to results.
export type Role = 'ADMIN' | 'USER'

const file = process.env.AUTOMATON_TEST_ACCOUNTS ?? join(homedir(), '.config', 'automaton-test', 'accounts.env')

function readAccounts(): Record<string, string> {
  const values: Record<string, string> = {}
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const match = /^(AUTOMATON_TEST_[A-Z0-9_]+)=(.*)$/.exec(line)
    if (match) values[match[1]] = match[2]
  }
  return values
}

export function credentials(role: Role): { email: string; password: string } {
  const values = readAccounts()
  const email = values[`AUTOMATON_TEST_${role}_EMAIL`]
  const password = values[`AUTOMATON_TEST_${role}_PASSWORD`]
  if (!email || !password) throw new Error(`No ${role} account in the test credentials file.`)
  return { email, password }
}

/**
 * Signs in once and returns the page to reuse for every check of that role. Studio rotates the refresh token on
 * each page load, so a saved session works only once; one page per role also keeps sign-ins under the rate limit.
 */
export async function signIn(browser: Browser, role: Role, viewport = { width: 1280, height: 800 }): Promise<Page> {
  const { email, password } = credentials(role)
  const page = await (await browser.newContext({ viewport })).newPage()
  await page.goto('/login')
  await page.locator('input[type=email], #email').fill(email)
  await page.locator("input[placeholder='Enter your password']").fill(password)
  await page.locator('button[type=submit]').click()
  await expect(page).not.toHaveURL(/\/login|\/change-password/, { timeout: 20_000 })
  return page
}

/** Studio keeps the session in memory and restores it from the refresh cookie, so navigate inside the app. */
export async function goTo(page: Page, path: string): Promise<void> {
  await expect(page).not.toHaveURL(/\/login/)
  await page.evaluate(async (target) => {
    const app = (document.querySelector('#app') as unknown as { __vue_app__: { config: { globalProperties: { $router: { push(path: string): Promise<unknown> } } } } }).__vue_app__
    await app.config.globalProperties.$router.push(target)
  }, path)
  await expect(page).not.toHaveURL(/\/login/)
}
