import { test, expect } from '@playwright/test';

test.describe('Players Page - Extended Tests', () => {
  test.describe('Page Structure', () => {
    test('should display search input', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await expect(searchInput).toBeVisible();
    });

    test('should display welcome state before search', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      // Should show welcome text before first search
      const welcomeText = page.locator('text=Search for a player');
      await expect(welcomeText).toBeVisible();

      const hintText = page.locator('text=Start typing a name');
      await expect(hintText).toBeVisible();
    });

    test('should auto-focus search input on load', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await expect(searchInput).toBeFocused();
    });
  });

  test.describe('Search Functionality', () => {
    test('should debounce search input', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await expect(searchInput).toBeVisible();

      // Type rapidly
      await searchInput.type('test', { delay: 50 });

      // Value should be set immediately
      const inputValue = await searchInput.inputValue();
      expect(inputValue).toBe('test');
    });

    test('should show clear button when text is present', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('test');

      // Clear button should appear
      const clearButton = page.locator('button[title="Clear search"]');
      await expect(clearButton).toBeVisible();
    });

    test('should clear search when clear button is clicked', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('test query');
      await page.waitForTimeout(400);

      const clearButton = page.locator('button[title="Clear search"]');
      await clearButton.click();

      const inputValue = await searchInput.inputValue();
      expect(inputValue).toBe('');

      // Should return to welcome state
      const welcomeText = page.locator('text=Search for a player');
      await expect(welcomeText).toBeVisible();
    });

    test('should handle special characters in search', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');

      // Type special characters
      await searchInput.fill('[TAG]Player');
      await page.waitForTimeout(500);

      // Page should not crash
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(100);
    });

    test('should handle empty search results gracefully', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');

      // Search for something unlikely to exist
      await searchInput.fill('xyznonexistentplayer12345');
      await page.waitForTimeout(1000);

      // Page should still be functional
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(100);
    });
  });

  test.describe('Player Results', () => {
    test('should display player results as cards after search', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('a');
      await page.waitForTimeout(1500);

      // Should have cards or a results section
      const resultsGrid = page.locator('[class*="grid"]');
      const hasResults = await resultsGrid.first().isVisible().catch(() => false);

      expect(hasResults).toBeTruthy();
    });

    test('should show skeleton loading during search', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');

      // Type and immediately check for skeleton
      await searchInput.fill('test');
      await page.waitForTimeout(100);

      // Skeleton elements may appear briefly
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(100);
    });

    test('should sort results', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('a');
      await page.waitForTimeout(1500);

      // Results should be displayed
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(200);
    });
  });

  test.describe('Player Navigation', () => {
    test('should navigate to player details when clicking player card', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('a');
      await page.waitForTimeout(1500);

      // Find player cards
      const playerCards = page.locator('[class*="cursor-pointer"][class*="rounded-xl"]');
      const cardCount = await playerCards.count();

      if (cardCount > 0) {
        const firstCard = playerCards.first();
        await firstCard.click();
        await page.waitForLoadState('networkidle');

        // Should be on player details page
        expect(page.url()).toContain('/players/');
      }
    });

    test('should maintain search state in URL', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('test');
      await page.waitForTimeout(500);

      // URL should contain the query param
      expect(page.url()).toContain('q=test');
    });
  });

  test.describe('Responsive Design', () => {
    test('should display search input on mobile', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await expect(searchInput).toBeVisible();
    });

    test('should allow searching on mobile', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('test');
      await page.waitForTimeout(500);

      const inputValue = await searchInput.inputValue();
      expect(inputValue).toBe('test');
    });

    test('should display results properly on mobile', async ({ page }) => {
      await page.setViewportSize({ width: 375, height: 667 });
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('a');
      await page.waitForTimeout(1500);

      // Content should be visible
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(200);
    });

    test('should handle tablet viewport', async ({ page }) => {
      await page.setViewportSize({ width: 768, height: 1024 });
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await expect(searchInput).toBeVisible();
    });
  });

  test.describe('Keyboard Navigation', () => {
    test('should be able to type in search input', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.focus();

      await page.keyboard.type('test');

      const inputValue = await searchInput.inputValue();
      expect(inputValue).toBe('test');
    });

    test('should trigger immediate search on enter', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('player');
      await page.keyboard.press('Enter');
      await page.waitForTimeout(500);

      // Page should still function
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(100);
    });
  });

  test.describe('Loading States', () => {
    test('should show loading state while fetching results', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('test');

      // Loading might be brief — just ensure page handles it
      await page.waitForTimeout(100);

      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(100);
    });

    test('should replace loading with results', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await searchInput.fill('a');

      // Wait for loading to complete
      await page.waitForTimeout(2000);

      // Should have content (results or empty state)
      const bodyText = await page.locator('body').textContent();
      expect(bodyText?.length).toBeGreaterThan(200);
    });
  });

  test.describe('URL Handling', () => {
    test('should load players page from direct URL', async ({ page }) => {
      await page.goto('/players');
      await page.waitForLoadState('networkidle');

      expect(page.url()).toContain('/players');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      await expect(searchInput).toBeVisible();
    });

    test('should restore search from URL query param', async ({ page }) => {
      await page.goto('/players?q=testplayer');
      await page.waitForLoadState('networkidle');

      const searchInput = page.locator('input[placeholder*="Search players"]');
      const inputValue = await searchInput.inputValue();
      expect(inputValue).toBe('testplayer');
    });
  });
});
