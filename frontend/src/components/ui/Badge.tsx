import { cn } from '../../lib/utils'

type Tone = 'neutral' | 'primary' | 'up' | 'down'

const tones: Record<Tone, string> = {
  neutral: 'bg-surface-2 text-muted border-border',
  primary: 'bg-primary/10 text-primary border-primary/20',
  up: 'bg-up/10 text-up border-up/20',
  down: 'bg-down/10 text-down border-down/20'
}

interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  tone?: Tone
}

export function Badge({ className, tone = 'neutral', ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full border px-2 py-0.5 text-[0.7rem] font-medium leading-none',
        tones[tone],
        className
      )}
      {...props}
    />
  )
}
