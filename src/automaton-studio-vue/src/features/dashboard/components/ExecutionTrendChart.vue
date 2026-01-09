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
  LinearScale,
  type ChartOptions,
  type ChartData,
} from 'chart.js'
import type { DailyExecutionTrend } from '../types/dashboard.types'

// Register Chart.js components
ChartJS.register(Title, Tooltip, Legend, BarElement, CategoryScale, LinearScale)

interface Props {
  data: DailyExecutionTrend[]
}

const props = defineProps<Props>()

// Format date for display (e.g., "Mon", "Tue")
const formatDay = (dateString: string): string => {
  const date = new Date(dateString)
  return date.toLocaleDateString('en-US', { weekday: 'short' })
}

// Chart data
const chartData = computed<ChartData<'bar'>>(() => {
  const sortedData = [...props.data].sort(
    (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime(),
  )

  return {
    labels: sortedData.map((d) => formatDay(d.date)),
    datasets: [
      {
        label: 'Successful',
        data: sortedData.map((d) => d.successCount),
        backgroundColor: 'rgba(34, 197, 94, 0.8)',
        borderColor: 'rgb(34, 197, 94)',
        borderWidth: 1,
        borderRadius: 4,
      },
      {
        label: 'Failed',
        data: sortedData.map((d) => d.failureCount),
        backgroundColor: 'rgba(239, 68, 68, 0.8)',
        borderColor: 'rgb(239, 68, 68)',
        borderWidth: 1,
        borderRadius: 4,
      },
    ],
  }
})

// Chart options
const chartOptions = computed<ChartOptions<'bar'>>(() => ({
  responsive: true,
  maintainAspectRatio: false,
  plugins: {
    legend: {
      position: 'top' as const,
      labels: {
        usePointStyle: true,
        padding: 16,
      },
    },
    tooltip: {
      mode: 'index' as const,
      intersect: false,
      callbacks: {
        title: (context) => {
          const idx = context[0]?.dataIndex
          if (idx !== undefined && idx >= 0 && idx < props.data.length) {
            const sortedData = [...props.data].sort(
              (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime(),
            )
            const d = sortedData[idx]
            if (d) {
              return new Date(d.date).toLocaleDateString('en-US', {
                weekday: 'long',
                month: 'short',
                day: 'numeric',
              })
            }
          }
          return ''
        },
      },
    },
  },
  scales: {
    x: {
      grid: {
        display: false,
      },
    },
    y: {
      beginAtZero: true,
      ticks: {
        stepSize: 1,
        precision: 0,
      },
      grid: {
        color: 'rgba(0, 0, 0, 0.05)',
      },
    },
  },
  interaction: {
    mode: 'index' as const,
    intersect: false,
  },
}))

// Empty state check
const isEmpty = computed(() => {
  return props.data.length === 0 || props.data.every((d) => d.totalCount === 0)
})
</script>

<template>
  <div class="execution-trend-chart">
    <div v-if="isEmpty" class="empty-chart">
      <i class="pi pi-chart-bar text-color-secondary" style="font-size: 2rem"></i>
      <p class="text-color-secondary mt-2">No execution data available</p>
    </div>
    <div v-else class="chart-container">
      <Bar :data="chartData" :options="chartOptions" />
    </div>
  </div>
</template>

<style scoped>
.execution-trend-chart {
  width: 100%;
  height: 100%;
  min-height: 200px;
}

.chart-container {
  width: 100%;
  height: 200px;
}

.empty-chart {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 200px;
  text-align: center;
}
</style>
