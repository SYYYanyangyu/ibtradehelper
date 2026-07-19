import { Card } from './ui/Card'

function Block({ className }: { className?: string }) {
  return <div className={`shimmer rounded-md bg-surface-2 ${className ?? ''}`} />
}

export function DashboardSkeleton() {
  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Card key={i} className="p-5">
            <div className="flex items-start justify-between">
              <Block className="h-4 w-20" />
              <Block className="h-9 w-9 rounded-xl" />
            </div>
            <Block className="mt-4 h-7 w-32" />
            <Block className="mt-4 h-4 w-16" />
          </Card>
        ))}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card className="p-5">
          <Block className="mb-4 h-4 w-24" />
          <Block className="h-[230px] w-full rounded-xl" />
        </Card>
        <Card className="p-5">
          <Block className="mb-4 h-4 w-24" />
          <Block className="h-[230px] w-full rounded-xl" />
        </Card>
      </div>

      <Card className="p-5">
        <Block className="mb-4 h-4 w-24" />
        {Array.from({ length: 5 }).map((_, i) => (
          <Block key={i} className="mb-3 h-10 w-full" />
        ))}
      </Card>
    </div>
  )
}
