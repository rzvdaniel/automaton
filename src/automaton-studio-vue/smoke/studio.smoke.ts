import { expect, test, type Page } from '@playwright/test'
import { goTo, signIn } from './accounts'

// Read-only checks of the main Studio pages after an update. Each page is also saved as a screenshot in
// smoke-results/ so a reviewer can look at it. Checks run in order on one signed-in page per role.
test.describe.configure({ mode: 'serial' })

const heading = (page: Page, name: string) => page.getByRole('heading', { name, exact: true })

/** Optional branches (no app or project on the server) are reported, not passed silently. */
function skipped(what: string) {
  test.info().annotations.push({ type: 'skipped', description: what })
}

async function shot(page: Page, name: string) {
  await page.screenshot({ path: `smoke-results/${name}.png`, fullPage: true })
}

test.describe('as the system administrator', () => {
  let page: Page
  test.beforeAll(async ({ browser }) => { page = await signIn(browser, 'ADMIN') })
  test.afterAll(async () => { await page?.context().close() })

  test('dashboard loads', async () => {
    await goTo(page, '/')
    await expect(heading(page, 'Dashboard')).toBeVisible()
    await shot(page, 'admin-dashboard')
  })

  test('Admin → Users lists users and opens Add user', async () => {
    await goTo(page, '/admin/users')
    await expect(heading(page, 'Users')).toBeVisible()
    // At least one user row below the header; the table's "No users found" placeholder is also a row.
    await expect(page.getByRole('row').filter({ hasNot: page.getByText(/No users found/) }).nth(1)).toBeVisible()
    await page.getByRole('button', { name: 'Add user' }).click()
    await page.getByText('Create with password').click()
    await expect(page.getByText('Must choose a new password at first sign-in')).toBeVisible()
    await shot(page, 'admin-add-user')
    await page.keyboard.press('Escape')
  })

  test('Admin → Apps lists apps and opens a Permissions tab when one exists', async () => {
    await goTo(page, '/admin/apps')
    await expect(page.getByText('Every app on this server')).toBeVisible()
    await shot(page, 'admin-apps')
    const app = page.locator('a[href*="/admin/apps/"]').first()
    if (await app.count()) {
      await app.click()
      await page.getByRole('tab', { name: 'Permissions' }).click()
      await expect(page.getByText('App permissions')).toBeVisible()
      await shot(page, 'admin-app-permissions')
    } else skipped('Permissions tab: the server has no apps')
  })

  test('Admin → Installation scrolls to its last section', async () => {
    await goTo(page, '/admin/installation')
    const nextSteps = page.getByText('Next steps', { exact: true })
    await nextSteps.scrollIntoViewIfNeeded()
    await expect(nextSteps).toBeInViewport()
    await shot(page, 'admin-installation')
  })

  test('dark theme renders', async () => {
    // Sets the theme class directly: the toolbar switch also saves the choice to the user's settings on the server.
    await goTo(page, '/admin/users')
    const background = () =>
      page.evaluate(() => getComputedStyle(document.documentElement).getPropertyValue('--p-content-background').trim())
    await page.evaluate(() => document.documentElement.classList.remove('app-dark'))
    const light = await background()
    await page.evaluate(() => document.documentElement.classList.add('app-dark'))
    await expect.poll(background).not.toBe(light)
    await shot(page, 'admin-users-dark')
    await page.evaluate(() => document.documentElement.classList.remove('app-dark'))
  })

  test('a direct load of /apikeys shows Studio, not the API (#540)', async () => {
    await page.goto('/apikeys')
    await expect(page).not.toHaveURL(/\/login/)
    await expect(heading(page, 'API Keys')).toBeVisible()
    await shot(page, 'admin-apikeys')
  })
})

test.describe('as a regular user', () => {
  let page: Page
  test.beforeAll(async ({ browser }) => { page = await signIn(browser, 'USER') })
  test.afterAll(async () => { await page?.context().close() })

  test('dashboard loads and Admin is refused', async () => {
    await goTo(page, '/')
    await expect(heading(page, 'Dashboard')).toBeVisible()
    await shot(page, 'user-dashboard')
    await goTo(page, '/admin/users')
    await expect(page).not.toHaveURL(/\/admin/)
  })

  test('project members are listed when the user is in a project', async () => {
    await goTo(page, '/projects')
    await shot(page, 'user-projects')
    const project = page.locator('a[href*="/projects/"][href$="/settings"]').first()
    if (await project.count()) {
      await project.click()
      await expect(page.getByRole('tab', { name: 'Members' })).toBeVisible()
      await shot(page, 'user-project-members')
    } else skipped('Project members: the user is in no project')
  })

  test('phone width has no horizontal scroll', async () => {
    await page.setViewportSize({ width: 390, height: 844 })
    await goTo(page, '/')
    await expect(heading(page, 'Dashboard')).toBeVisible()
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)
    expect(overflow).toBeLessThanOrEqual(1)
    await shot(page, 'user-phone')
  })
})
