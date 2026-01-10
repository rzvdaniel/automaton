# Phase 2.2 - Dashboard Implementation

**AI Agent Prompt for Dashboard Implementation**

---

## Prompt for AI Agent

```
Implement Phase 2.2 - Dashboard from REDESIGN_PLAN.md.

PREREQUISITE: Before starting, READ THE ENTIRE src/automaton-studio-vue/REDESIGN_PLAN.md file.
Pay special attention to:
- Section 6: Project Structure (Feature-Based) - work in features/dashboard/ structure
- Section 7.1: Dashboard design spec - follow exact layout
- Common Pitfalls for AI Agents - avoid these mistakes!
- Phase 1.2 completed: Dashboard stub already exists, you're adding functionality

IMPORTANT:
- Dashboard stub already exists from Phase 1.2 - REPLACE placeholder/mock data with real API calls
- Follow Project Structure (Section 6) for features/dashboard/ folder
- Review Dashboard design spec (Section 7.1) for exact layout (already implemented in stub)
- Review Blazor Dashboard implementation for feature parity:
  - src/Automaton.Studio/Pages/Dashboard/DashboardPage.razor
  - Backend: src/Automaton.Studio.Server/Controllers/DashboardController.cs
  - Backend: src/Automaton.Studio.Server/Services/DashboardStatsService.cs

Key Implementation Tasks:

1. Dashboard Feature Structure (ADD to existing):
   - features/dashboard/types/dashboard.types.ts (NEW)
   - features/dashboard/services/dashboard.service.ts (NEW)
   - features/dashboard/stores/dashboard.store.ts (NEW)
   - features/dashboard/components/ExecutionTrendChart.vue (NEW)
   - features/dashboard/views/DashboardView.vue (EXISTS - UPDATE)

2. TypeScript Interfaces (dashboard.types.ts):
   ```typescript
   export interface DashboardStats {
     activeFlowsCount: number
     activeFlowsChange: number  // e.g., +2 or -1
     executionsLast24h: number
     successRate: number  // e.g., 94.2
     successRateChange: number  // e.g., +1.2 or -0.5
     runnersOnline: number
     runnersTotal: number
   }

   export interface RecentExecution {
     id: string
     flowName: string
     status: 'success' | 'failed' | 'running'
     startedAt: string  // ISO date string
     completedAt?: string  // ISO date string
   }

   export interface ExecutionTrendData {
     date: string  // e.g., "Mon", "Tue", etc.
     executions: number
     successful: number
     failed: number
   }

   export interface DashboardData {
     stats: DashboardStats
     recentExecutions: RecentExecution[]
     executionTrend: ExecutionTrendData[]  // Last 7 days
     recentFlows: string[]  // Array of flow names
   }
   ```

3. Dashboard Service (dashboard.service.ts):
   - GET /api/dashboard/stats - Fetch dashboard statistics
   - Should return DashboardData shape (verify with Blazor API)
   - Handle errors gracefully with try/catch
   - Use Axios instance from shared/services/api/

   ```typescript
   import axios from '@/shared/services/api/axios-instance'
   import type { DashboardData } from '../types/dashboard.types'

   export const dashboardService = {
     async getStats(): Promise<DashboardData> {
       const response = await axios.get('/api/dashboard/stats')
       return response.data
     }
   }
   ```

4. Dashboard Store (dashboard.store.ts - Pinia):
   ```typescript
   import { defineStore } from 'pinia'
   import { dashboardService } from '../services/dashboard.service'
   import type { DashboardData } from '../types/dashboard.types'

   export const useDashboardStore = defineStore('dashboard', {
     state: () => ({
       data: null as DashboardData | null,
       loading: false,
       error: null as string | null
     }),

     actions: {
       async fetchDashboard() {
         this.loading = true
         this.error = null
         try {
           this.data = await dashboardService.getStats()
         } catch (err: any) {
           this.error = err.response?.data?.message || 'Failed to load dashboard'
           throw err
         } finally {
           this.loading = false
         }
       },

       async refresh() {
         await this.fetchDashboard()
       }
     },

     getters: {
       hasData: (state) => state.data !== null,
       isLoading: (state) => state.loading,
       hasError: (state) => state.error !== null
     }
   })
   ```

5. Update Dashboard View (DashboardView.vue):

   CRITICAL: The stub already has the layout structure. DO NOT change the layout.
   Your job is to:
   - Import and use the dashboard store
   - Replace mock/placeholder data with real data from store
   - Add loading states (use Skeleton components from stub)
   - Add error states (use Message component)
   - Add empty states (if no data available)

   Key Changes:

   a) Script Setup:
      ```typescript
      <script setup lang="ts">
      import { onMounted, onUnmounted, computed } from 'vue'
      import { useDashboardStore } from '../stores/dashboard.store'
      import { useRouter } from 'vue-router'
      import ExecutionTrendChart from '../components/ExecutionTrendChart.vue'

      const dashboardStore = useDashboardStore()
      const router = useRouter()

      // Auto-refresh every 30 seconds
      let refreshInterval: NodeJS.Timeout | null = null

      onMounted(async () => {
        await dashboardStore.fetchDashboard()

        // Set up auto-refresh
        refreshInterval = setInterval(() => {
          dashboardStore.refresh()
        }, 30000) // 30 seconds
      })

      onUnmounted(() => {
        if (refreshInterval) {
          clearInterval(refreshInterval)
        }
      })

      // Navigation handlers
      const handleCreateFlow = () => {
        router.push('/flows/new')
      }

      const handleViewAllExecutions = () => {
        router.push('/executions')
      }

      const handleCreateApp = () => {
        router.push('/apps/new')
      }

      const handleCreateStep = () => {
        router.push('/steps/new')
      }

      const handleFlowClick = (flowName: string) => {
        // Navigate to flow detail (implement based on your routing)
        router.push(`/flows/${flowName}`)
      }

      const handleExecutionClick = (executionId: string) => {
        router.push(`/executions/${executionId}`)
      }

      // Helper functions for formatting
      const formatTimeAgo = (dateString: string) => {
        const now = new Date()
        const past = new Date(dateString)
        const diffMs = now.getTime() - past.getTime()
        const diffMins = Math.floor(diffMs / 60000)

        if (diffMins < 1) return 'Just now'
        if (diffMins < 60) return `${diffMins}m ago`

        const diffHours = Math.floor(diffMins / 60)
        if (diffHours < 24) return `${diffHours}h ago`

        const diffDays = Math.floor(diffHours / 24)
        return `${diffDays}d ago`
      }

      const getStatusIcon = (status: string) => {
        return status === 'success' ? '✅' :
               status === 'failed' ? '❌' : '⏳'
      }

      const getStatusSeverity = (status: string) => {
        return status === 'success' ? 'success' :
               status === 'failed' ? 'error' : 'info'
      }

      const formatChangeIndicator = (change: number) => {
        if (change > 0) return `↑ ${change}`
        if (change < 0) return `↓ ${Math.abs(change)}`
        return '—'
      }

      // Computed properties
      const stats = computed(() => dashboardStore.data?.stats)
      const recentExecutions = computed(() => dashboardStore.data?.recentExecutions || [])
      const executionTrend = computed(() => dashboardStore.data?.executionTrend || [])
      const recentFlows = computed(() => dashboardStore.data?.recentFlows || [])
      </script>
      ```

   b) Template Structure:
      ```vue
      <template>
        <div class="p-4">
          <!-- Page Header -->
          <div class="flex align-items-center justify-content-between mb-6">
            <h1 class="text-3xl font-bold">Dashboard</h1>
            <Button
              icon="pi pi-plus"
              label="Create Flow"
              @click="handleCreateFlow"
            />
          </div>

          <!-- Loading State -->
          <div v-if="dashboardStore.isLoading && !dashboardStore.hasData" class="grid">
            <div class="col-12 md:col-3">
              <Skeleton height="120px" />
            </div>
            <div class="col-12 md:col-3">
              <Skeleton height="120px" />
            </div>
            <div class="col-12 md:col-3">
              <Skeleton height="120px" />
            </div>
            <div class="col-12 md:col-3">
              <Skeleton height="120px" />
            </div>
            <div class="col-12 md:col-8">
              <Skeleton height="300px" />
            </div>
            <div class="col-12 md:col-4">
              <Skeleton height="300px" />
            </div>
          </div>

          <!-- Error State -->
          <Message v-else-if="dashboardStore.hasError" severity="error" :closable="false">
            <div class="flex align-items-center justify-content-between">
              <span>{{ dashboardStore.error }}</span>
              <Button
                label="Retry"
                severity="danger"
                @click="dashboardStore.fetchDashboard()"
                size="small"
              />
            </div>
          </Message>

          <!-- Dashboard Content -->
          <div v-else-if="dashboardStore.hasData">
            <!-- Stats Cards -->
            <div class="grid mb-4">
              <!-- Active Flows Card -->
              <div class="col-12 md:col-3">
                <Card>
                  <template #content>
                    <div class="text-center">
                      <div class="text-500 mb-2">Active Flows</div>
                      <div class="text-4xl font-bold mb-2">{{ stats?.activeFlowsCount || 0 }}</div>
                      <div class="text-sm" :class="stats?.activeFlowsChange >= 0 ? 'text-green-500' : 'text-red-500'">
                        {{ formatChangeIndicator(stats?.activeFlowsChange || 0) }} new
                      </div>
                    </div>
                  </template>
                </Card>
              </div>

              <!-- Executions Card -->
              <div class="col-12 md:col-3">
                <Card>
                  <template #content>
                    <div class="text-center">
                      <div class="text-500 mb-2">Executions</div>
                      <div class="text-4xl font-bold mb-2">{{ stats?.executionsLast24h?.toLocaleString() || 0 }}</div>
                      <div class="text-sm text-500">Last 24h</div>
                    </div>
                  </template>
                </Card>
              </div>

              <!-- Success Rate Card -->
              <div class="col-12 md:col-3">
                <Card>
                  <template #content>
                    <div class="text-center">
                      <div class="text-500 mb-2">Success Rate</div>
                      <div class="text-4xl font-bold mb-2">{{ stats?.successRate?.toFixed(1) || 0 }}%</div>
                      <div class="text-sm" :class="stats?.successRateChange >= 0 ? 'text-green-500' : 'text-red-500'">
                        {{ formatChangeIndicator(stats?.successRateChange || 0) }}%
                      </div>
                    </div>
                  </template>
                </Card>
              </div>

              <!-- Runners Card -->
              <div class="col-12 md:col-3">
                <Card>
                  <template #content>
                    <div class="text-center">
                      <div class="text-500 mb-2">Runners</div>
                      <div class="text-4xl font-bold mb-2">
                        {{ stats?.runnersOnline || 0 }} / {{ stats?.runnersTotal || 0 }}
                      </div>
                      <div class="text-sm text-500">Online</div>
                    </div>
                  </template>
                </Card>
              </div>
            </div>

            <!-- Main Content Area -->
            <div class="grid">
              <!-- Left Column: Recent Executions & Chart -->
              <div class="col-12 md:col-8">
                <!-- Recent Executions -->
                <Card class="mb-4">
                  <template #title>Recent Executions</template>
                  <template #content>
                    <div v-if="recentExecutions.length === 0" class="text-center text-500 py-4">
                      No recent executions
                    </div>
                    <div v-else>
                      <div
                        v-for="execution in recentExecutions"
                        :key="execution.id"
                        class="flex align-items-center justify-content-between py-3 border-bottom cursor-pointer hover:surface-hover"
                        @click="handleExecutionClick(execution.id)"
                      >
                        <div class="flex align-items-center gap-3">
                          <span class="text-2xl">{{ getStatusIcon(execution.status) }}</span>
                          <div>
                            <div class="font-semibold">{{ execution.flowName }}</div>
                            <div class="text-sm text-500">{{ formatTimeAgo(execution.startedAt) }}</div>
                          </div>
                        </div>
                        <Tag
                          :severity="getStatusSeverity(execution.status)"
                          :value="execution.status"
                        />
                      </div>
                      <div class="text-center mt-4">
                        <Button
                          label="View All →"
                          link
                          @click="handleViewAllExecutions"
                        />
                      </div>
                    </div>
                  </template>
                </Card>

                <!-- Execution Trend Chart -->
                <Card>
                  <template #title>Execution Trend (7 days)</template>
                  <template #content>
                    <ExecutionTrendChart :data="executionTrend" />
                  </template>
                </Card>
              </div>

              <!-- Right Column: Quick Actions -->
              <div class="col-12 md:col-4">
                <Card>
                  <template #title>Quick Actions</template>
                  <template #content>
                    <div class="flex flex-column gap-3">
                      <Button
                        label="🤖 Generate Flow"
                        class="w-full"
                        @click="handleCreateFlow"
                      />
                      <Button
                        label="📱 Create App"
                        class="w-full"
                        outlined
                        @click="handleCreateApp"
                      />
                      <Button
                        label="🧩 New Step"
                        class="w-full"
                        outlined
                        @click="handleCreateStep"
                      />
                    </div>

                    <Divider />

                    <div class="text-500 mb-2">Recent Flows:</div>
                    <div v-if="recentFlows.length === 0" class="text-center text-500 py-2">
                      No recent flows
                    </div>
                    <ul v-else class="list-none p-0 m-0">
                      <li
                        v-for="flow in recentFlows"
                        :key="flow"
                        class="mb-2 cursor-pointer hover:text-primary"
                        @click="handleFlowClick(flow)"
                      >
                        • {{ flow }}
                      </li>
                    </ul>
                  </template>
                </Card>
              </div>
            </div>
          </div>
        </div>
      </template>
      ```

6. Execution Trend Chart Component (ExecutionTrendChart.vue):

   Install chart library first:
   ```bash
   npm install chart.js vue-chartjs
   ```

   Create component:
   ```vue
   <script setup lang="ts">
   import { computed } from 'vue'
   import { Bar } from 'vue-chartjs'
   import {
     Chart as ChartJS,
     Title,
     Tooltip,
     Legend,
     BarElement,
     CategoryScale,
     LinearScale
   } from 'chart.js'
   import type { ExecutionTrendData } from '../types/dashboard.types'

   ChartJS.register(Title, Tooltip, Legend, BarElement, CategoryScale, LinearScale)

   interface Props {
     data: ExecutionTrendData[]
   }

   const props = defineProps<Props>()

   const chartData = computed(() => ({
     labels: props.data.map(d => d.date),
     datasets: [
       {
         label: 'Successful',
         backgroundColor: '#22c55e',
         data: props.data.map(d => d.successful)
       },
       {
         label: 'Failed',
         backgroundColor: '#ef4444',
         data: props.data.map(d => d.failed)
       }
     ]
   }))

   const chartOptions = {
     responsive: true,
     maintainAspectRatio: false,
     plugins: {
       legend: {
         display: true,
         position: 'top' as const
       }
     },
     scales: {
       x: {
         stacked: true
       },
       y: {
         stacked: true,
         beginAtZero: true
       }
     }
   }
   </script>

   <template>
     <div style="height: 250px">
       <Bar :data="chartData" :options="chartOptions" />
     </div>
   </template>
   ```

Deliverables:
- [ ] Dashboard loads stats from API on mount
- [ ] All 4 stat cards display real data with correct formatting
- [ ] Recent executions list shows real data with status icons
- [ ] Execution trend chart displays 7 days of stacked bar data
- [ ] Quick action buttons navigate to correct pages
- [ ] Recent flows list displays and is clickable
- [ ] Loading state shows skeletons while fetching
- [ ] Error state shows message with retry button
- [ ] Empty states show helpful messages (no executions, no flows)
- [ ] Auto-refresh data every 30 seconds
- [ ] Clicking executions navigates to detail page
- [ ] Clicking flows navigates to flow detail page
- [ ] Responsive layout works (mobile/tablet/desktop)
- [ ] Dark mode works
- [ ] No console errors
- [ ] No TypeScript errors

Testing Checklist:
- [ ] Test initial page load (should show loading, then data)
- [ ] Test with no executions (empty state message)
- [ ] Test with API error (error message + retry button works)
- [ ] Test all 4 stat cards display correct values
- [ ] Test stat card trend indicators (↑ for positive, ↓ for negative)
- [ ] Test recent executions list:
  - ✅ icon for success
  - ❌ icon for failed
  - ⏳ icon for running
  - Time ago formatting (2m, 5h, 3d)
  - Clicking navigates to execution detail
- [ ] Test execution trend chart:
  - Shows 7 days of data
  - Green bars for successful
  - Red bars for failed
  - Stacked correctly
  - Tooltips show correct values
- [ ] Test quick action buttons:
  - "Generate Flow" → /flows/new
  - "Create App" → /apps/new
  - "New Step" → /steps/new
- [ ] Test recent flows:
  - List displays correctly
  - Clicking navigates to flow detail
  - Empty state when no flows
- [ ] Test [View All →] button navigates to /executions
- [ ] Test auto-refresh (data updates every 30 seconds)
- [ ] Test retry button on error state
- [ ] Test responsive on mobile (375px), tablet (768px), desktop (1440px)
- [ ] Test dark mode toggle
- [ ] Verify cleanup on unmount (clear interval)
- [ ] Verify no TypeScript errors
- [ ] Verify no console warnings/errors

API Integration Notes:
- Review existing Blazor API endpoint: GET /api/dashboard/stats
- Check DashboardController.cs for exact response shape
- The API should already exist in the backend
- Axios instance from Phase 2.1 already includes auth headers
- Handle 401 errors (should redirect to login via auth guard)
- Handle 403 errors (insufficient permissions)
- Handle 500 errors (server error, show retry)

Performance Considerations:
- Dashboard should load in < 2 seconds on good connection
- Auto-refresh should not block UI (runs in background)
- Chart rendering should be smooth (use requestAnimationFrame if needed)
- Consider caching data for 30 seconds to reduce API calls

Accessibility:
- All cards should be keyboard navigable
- Chart should have aria-label
- Status icons should have aria-label (not just emoji)
- Error messages should be announced to screen readers
- Buttons should have proper focus states

CRITICAL: This is the FIRST PAGE users see after login. Make it informative, fast, and error-free.
```

---

## Implementation Checklist

### Setup
- [ ] Install chart.js and vue-chartjs: `npm install chart.js vue-chartjs`
- [ ] Verify Phase 1.2 dashboard stub exists
- [ ] Verify Phase 2.1 authentication is complete (axios instance, auth store)

### Files to Create
- [ ] features/dashboard/types/dashboard.types.ts
- [ ] features/dashboard/services/dashboard.service.ts
- [ ] features/dashboard/stores/dashboard.store.ts
- [ ] features/dashboard/components/ExecutionTrendChart.vue

### Files to Modify
- [ ] features/dashboard/views/DashboardView.vue (replace stub implementation)

### Backend Verification
- [ ] Verify GET /api/dashboard/stats endpoint exists
- [ ] Test endpoint returns expected data shape
- [ ] Verify authentication works with endpoint

### Testing
- [ ] All deliverables checked
- [ ] All test cases passed
- [ ] No TypeScript errors
- [ ] No console errors
- [ ] Dark mode works
- [ ] Responsive works

---

## Notes

- The dashboard stub from Phase 1.2 already has the correct PrimeVue layout structure
- DO NOT change the layout - only replace placeholder data with real API calls
- Focus on data binding, state management, and interactivity
- The execution trend chart is a new component to add
- Auto-refresh is important for monitoring active executions
- Empty states are important for new users with no data yet
