<template>
  <div class="portal-ai-feedback-tab">
    <div class="portal-card portal-audit">
      <div class="portal-audit-head">
        <h2 class="portal-audit-title">[ AI FEEDBACK ]</h2>
        <div class="portal-feedback-head-actions">
          <div class="portal-feedback-stats">
            <span class="portal-feedback-stat portal-feedback-stat--positive">{{ positiveCount }}</span>
            <span class="portal-feedback-stat portal-feedback-stat--negative">{{ negativeCount }}</span>
          </div>
          <select v-model="filterValue" class="portal-cron-select" @change="load(1)">
            <option value="all">All</option>
            <option value="positive">Positive only</option>
            <option value="negative">Negative only</option>
          </select>
          <button
            type="button"
            class="portal-btn portal-btn--ghost portal-btn--sm"
            :disabled="loading"
            @click="load()"
          >
            {{ loading ? 'loading…' : 'refresh' }}
          </button>
        </div>
      </div>
      <div v-if="error" class="portal-cron-err">{{ error }}</div>
      <div class="portal-audit-table-wrap">
        <table class="portal-audit-table">
          <thead>
            <tr>
              <th style="width: 50px">rating</th>
              <th style="width: 200px">prompt</th>
              <th>response</th>
              <th style="width: 140px">date</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="entry in items"
              :key="entry.id"
              class="portal-feedback-row"
              :class="{ 'portal-feedback-row--negative': !entry.isPositive }"
              @click="toggleExpanded(entry.id)"
            >
              <td>
                <span
                  class="portal-feedback-badge"
                  :class="entry.isPositive ? 'portal-feedback-badge--positive' : 'portal-feedback-badge--negative'"
                >
                  {{ entry.isPositive ? '+' : '-' }}
                </span>
              </td>
              <td class="portal-audit-mono portal-feedback-prompt">{{ truncate(entry.prompt, 120) }}</td>
              <td class="portal-feedback-response">{{ truncate(entry.response, 180) }}</td>
              <td class="portal-audit-mono">{{ formatDate(entry.createdAt) }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="items.length === 0 && !loading" class="portal-empty">
        <span class="portal-empty-dash">&empty;</span>
        <span class="portal-empty-title">No feedback yet</span>
        <span class="portal-empty-desc">Feedback appears when users rate AI responses.</span>
      </div>

      <!-- Pagination -->
      <div v-if="totalCount > pageSize" class="portal-pagination">
        <span>{{ totalCount }} total &middot; page {{ currentPage }} of {{ totalPages }}</span>
        <div style="display: flex; gap: 0.5rem">
          <button
            type="button"
            class="portal-btn portal-btn--ghost portal-btn--sm"
            :disabled="currentPage <= 1"
            @click="load(currentPage - 1)"
          >
            prev
          </button>
          <button
            type="button"
            class="portal-btn portal-btn--ghost portal-btn--sm"
            :disabled="currentPage >= totalPages"
            @click="load(currentPage + 1)"
          >
            next
          </button>
        </div>
      </div>

      <div class="portal-audit-foot">User feedback on AI chat responses. Click a row to view full details.</div>
    </div>

    <!-- Expanded detail modal -->
    <Teleport to="body">
      <div v-if="expandedEntry" class="portal-feedback-overlay" @click.self="expandedEntry = null">
        <div class="portal-feedback-detail">
          <div class="portal-feedback-detail-head">
            <span
              class="portal-feedback-badge"
              :class="expandedEntry.isPositive ? 'portal-feedback-badge--positive' : 'portal-feedback-badge--negative'"
            >
              {{ expandedEntry.isPositive ? 'POSITIVE' : 'NEGATIVE' }}
            </span>
            <span class="portal-feedback-detail-date">{{ formatDate(expandedEntry.createdAt) }}</span>
            <button type="button" class="portal-btn portal-btn--ghost portal-btn--sm" @click="expandedEntry = null">close</button>
          </div>
          <div class="portal-feedback-detail-section">
            <h4 class="portal-feedback-detail-label">Prompt</h4>
            <pre class="portal-feedback-detail-text">{{ expandedEntry.prompt }}</pre>
          </div>
          <div class="portal-feedback-detail-section">
            <h4 class="portal-feedback-detail-label">Response</h4>
            <pre class="portal-feedback-detail-text portal-feedback-detail-response">{{ expandedEntry.response }}</pre>
          </div>
          <div v-if="expandedEntry.comment" class="portal-feedback-detail-section">
            <h4 class="portal-feedback-detail-label">Comment</h4>
            <pre class="portal-feedback-detail-text">{{ expandedEntry.comment }}</pre>
          </div>
          <div v-if="expandedEntry.pageContext" class="portal-feedback-detail-section">
            <h4 class="portal-feedback-detail-label">Page Context</h4>
            <pre class="portal-feedback-detail-text">{{ formatContext(expandedEntry.pageContext) }}</pre>
          </div>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { adminDataService, type AIChatFeedbackEntry } from '@/services/adminDataService';

const items = ref<AIChatFeedbackEntry[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
const totalCount = ref(0);
const positiveCount = ref(0);
const negativeCount = ref(0);
const currentPage = ref(1);
const pageSize = 50;
const filterValue = ref<'all' | 'positive' | 'negative'>('all');
const expandedEntry = ref<AIChatFeedbackEntry | null>(null);

const totalPages = computed(() => Math.max(1, Math.ceil(totalCount.value / pageSize)));

async function load(page?: number) {
  if (page != null) currentPage.value = page;
  loading.value = true;
  error.value = null;
  try {
    const isPositive = filterValue.value === 'all' ? null : filterValue.value === 'positive';
    const res = await adminDataService.getAIChatFeedback(currentPage.value, pageSize, isPositive);
    items.value = res.items;
    totalCount.value = res.totalCount;
    positiveCount.value = res.positiveCount;
    negativeCount.value = res.negativeCount;
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load feedback';
    items.value = [];
  } finally {
    loading.value = false;
  }
}

function toggleExpanded(id: number) {
  const entry = items.value.find((e) => e.id === id);
  expandedEntry.value = expandedEntry.value?.id === id ? null : entry ?? null;
}

function truncate(text: string, max: number): string {
  if (!text) return '';
  return text.length > max ? text.slice(0, max) + '…' : text;
}

function formatDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }) + ' ' +
      d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
  } catch {
    return iso;
  }
}

function formatContext(ctx: string): string {
  try {
    return JSON.stringify(JSON.parse(ctx), null, 2);
  } catch {
    return ctx;
  }
}

onMounted(() => load());

defineExpose({ load });
</script>

<style scoped>
.portal-ai-feedback-tab {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.portal-feedback-head-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.portal-feedback-stats {
  display: flex;
  gap: 0.35rem;
  font-size: 0.75rem;
  font-weight: 600;
  font-family: ui-monospace, monospace;
}

.portal-feedback-stat--positive {
  color: #28c840;
}

.portal-feedback-stat--positive::before {
  content: '+';
}

.portal-feedback-stat--negative {
  color: #f85149;
}

.portal-feedback-stat--negative::before {
  content: '-';
}

.portal-feedback-row {
  cursor: pointer;
  transition: background 0.15s;
}

.portal-feedback-row:hover td {
  background: rgba(0, 229, 160, 0.05);
}

.portal-feedback-row--negative {
  border-left: 2px solid rgba(248, 81, 73, 0.3);
}

.portal-feedback-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 0.7rem;
  font-weight: 700;
  font-family: ui-monospace, monospace;
  padding: 0.15rem 0.45rem;
  border-radius: 2px;
  letter-spacing: 0.08em;
}

.portal-feedback-badge--positive {
  color: #28c840;
  background: rgba(40, 200, 64, 0.1);
  border: 1px solid rgba(40, 200, 64, 0.3);
}

.portal-feedback-badge--negative {
  color: #f85149;
  background: rgba(248, 81, 73, 0.1);
  border: 1px solid rgba(248, 81, 73, 0.3);
}

.portal-feedback-prompt {
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.portal-feedback-response {
  max-width: 300px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-size: 0.75rem;
  color: var(--portal-text);
}

/* Detail overlay */
.portal-feedback-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(2px);
  z-index: 1100;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 2rem;
}

.portal-feedback-detail {
  background: var(--portal-surface, #0d1117);
  border: 1px solid var(--portal-border, #30363d);
  border-radius: 4px;
  max-width: 800px;
  width: 100%;
  max-height: 80vh;
  overflow-y: auto;
  padding: 1.25rem;
}

.portal-feedback-detail-head {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--portal-border, #30363d);
}

.portal-feedback-detail-date {
  flex: 1;
  font-size: 0.75rem;
  color: var(--portal-text, #8b949e);
  font-family: ui-monospace, monospace;
}

.portal-feedback-detail-section {
  margin-bottom: 1rem;
}

.portal-feedback-detail-label {
  font-size: 0.65rem;
  font-weight: 600;
  letter-spacing: 0.12em;
  color: var(--portal-accent, #00e5a0);
  margin: 0 0 0.35rem;
  font-family: ui-monospace, monospace;
  text-transform: uppercase;
}

.portal-feedback-detail-text {
  font-size: 0.8rem;
  color: var(--portal-text-bright, #e6edf3);
  background: var(--portal-surface-elevated, #161b22);
  border: 1px solid var(--portal-border, #30363d);
  border-radius: 2px;
  padding: 0.75rem;
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: ui-monospace, monospace;
  line-height: 1.5;
  max-height: 200px;
  overflow-y: auto;
}

.portal-feedback-detail-response {
  max-height: 300px;
}
</style>
