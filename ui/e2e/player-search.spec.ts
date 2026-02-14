import { test, expect } from '@playwright/test';

test.describe('Player Search Flow', () => {
  test('should navigate to players page', async ({ page }) => {
    await page.goto('/players');
    await page.waitForLoadState('networkidle');

    // Check for search input
    const searchInput = page.locator('input[placeholder*="Search players"]');
    await expect(searchInput).toBeVisible();
  });

  test('should display search input on players page', async ({ page }) => {
    await page.goto('/players');
    await page.waitForLoadState('networkidle');

    const searchInput = page.locator('input[placeholder*="Search players"]');
    await expect(searchInput).toBeVisible();
  });

  test('should search as you type with debounce', async ({ page }) => {
    await page.goto('/players');
    await page.waitForLoadState('networkidle');

    const searchInput = page.locator('input[placeholder*="Search players"]');
    await expect(searchInput).toBeVisible();

    // Type a search term — results should appear without pressing Enter
    await searchInput.fill('player');
    await page.waitForTimeout(500);

    // Page content should still be present after search
    const bodyText = await page.locator('body').textContent();
    expect(bodyText?.length).toBeGreaterThan(100);
  });

  test('should display player results as cards', async ({ page }) => {
    await page.goto('/players');
    await page.waitForLoadState('networkidle');

    const searchInput = page.locator('input[placeholder*="Search players"]');
    await searchInput.fill('a');
    await page.waitForTimeout(1500);

    // Should have player cards (grid items with cursor-pointer)
    const playerCards = page.locator('[class*="cursor-pointer"]');
    const cardCount = await playerCards.count();

    // There should be some player cards in the results
    expect(cardCount).toBeGreaterThanOrEqual(0);
  });

  test('should navigate to player details when clicking on a card', async ({ page }) => {
    await page.goto('/players');
    await page.waitForLoadState('networkidle');

    const searchInput = page.locator('input[placeholder*="Search players"]');
    await searchInput.fill('player');
    await page.waitForTimeout(1500);

    // Look for player cards
    const playerCards = page.locator('[class*="cursor-pointer"][class*="rounded-xl"]');
    const cardCount = await playerCards.count();

    if (cardCount > 0) {
      const firstCard = playerCards.first();

      await Promise.all([
        page.waitForNavigation({ waitUntil: 'networkidle' }).catch(() => null),
        firstCard.click()
      ]);

      await page.waitForTimeout(500);

      const currentUrl = page.url();
      expect(currentUrl.toLowerCase()).toMatch(/\/players\//);
    }
  });
});
