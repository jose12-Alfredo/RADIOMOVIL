const paths = {
  panel: 'M4 5h7v7H4zM13 5h7v4h-7zM13 11h7v8h-7zM4 14h7v5H4z',
  solicitudes: 'M6 4h12v16H6zM9 8h6M9 12h6M9 16h3',
  despacho: 'M12 21s7-6.2 7-11.5A7 7 0 0 0 5 9.5C5 14.8 12 21 12 21zM12 12a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z',
  flota: 'M5 16V11l2-5h10l2 5v5M5 16h14M5 16v2.5M19 16v2.5M5 11h14M8 13.5h.01M16 13.5h.01',
  cuenta:'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4.5 20.5a7.5 7.5 0 0 1 15 0',
  menu: 'M4 7h16M4 12h16M4 17h16',
  close: 'M6 6l12 12M18 6L6 18',
  logout: 'M15 8l4 4-4 4M19 12H9M12 4H5v16h7',
  refresh: 'M20 11a8 8 0 0 0-14.6-3.6L4 9M4 4v5h5M4 13a8 8 0 0 0 14.6 3.6L20 15M20 20v-5h-5',
  check: 'M5 12.5l4.5 4.5L19 7.5',
  alert: 'M12 4l9 16H3zM12 10v4.5M12 17.5v.5',
  back: 'M10 6l-6 6 6 6M4 12h16',
  chevron: 'M9 6l6 6-6 6',
} as const

export type IconName = keyof typeof paths

export function Icon({ name }: { name: IconName }) {
  return <svg className="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d={paths[name]} /></svg>
}
