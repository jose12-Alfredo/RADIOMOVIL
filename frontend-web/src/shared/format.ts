export function formatTime(value: string | Date): string {
  const date = value instanceof Date ? value : new Date(value)
  const today = new Date().toDateString() === date.toDateString()
  return new Intl.DateTimeFormat('es', today
    ? { hour: '2-digit', minute: '2-digit' }
    : { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(date)
}
