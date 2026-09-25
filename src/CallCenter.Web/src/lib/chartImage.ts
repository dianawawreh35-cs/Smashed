/**
 * A chart as a picture, for presentations (S-06: "charts ... can be
 * downloaded as an image").
 *
 * The chart is an SVG drawn by recharts with its colours and fonts written on
 * the elements themselves, so it serialises as it looks. It is painted onto a
 * canvas at twice its size, on the card's own background (a transparent PNG
 * pasted into a white slide would lose its pale text), and handed over as a
 * PNG, which every presentation program takes.
 */

/** The card's surface (ink-900), behind the chart. */
const BACKGROUND = '#171A21'

/** Twice the screen size, so the picture stays sharp on a projector. */
const SCALE = 2

/** The legend's text, the chart's own ink (slate-200). */
const INK = '#e2e8f0'

/** Room under the chart for the legend, when there is one. */
const LEGEND_HEIGHT = 32

/**
 * @param legend The series, when there is more than one. Recharts draws its
 *   legend as HTML beside the SVG, so it is painted onto the picture here;
 *   without it a picture of two lines would tell them apart by colour alone.
 * @param rtl Arabic: the legend runs from the right, as it does on screen.
 */
export async function chartToPng(
  svg: SVGSVGElement,
  legend: { label: string; colour: string }[] = [],
  rtl = false,
): Promise<Blob> {
  const width = svg.width.baseVal.value || svg.getBoundingClientRect().width
  const chartHeight = svg.height.baseVal.value || svg.getBoundingClientRect().height
  const height = chartHeight + (legend.length > 1 ? LEGEND_HEIGHT : 0)

  const copy = svg.cloneNode(true) as SVGSVGElement
  copy.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  copy.setAttribute('width', String(width))
  copy.setAttribute('height', String(chartHeight))
  // The page's font, written onto the picture: the stylesheet does not travel with it.
  copy.setAttribute('font-family', getComputedStyle(svg).fontFamily || 'sans-serif')

  const source = new XMLSerializer().serializeToString(copy)
  const url = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(source)}`

  const image = new Image()
  await new Promise<void>((resolve, reject) => {
    image.onload = () => resolve()
    image.onerror = () => reject(new Error('the chart could not be drawn'))
    image.src = url
  })

  const canvas = document.createElement('canvas')
  canvas.width = Math.ceil(width * SCALE)
  canvas.height = Math.ceil(height * SCALE)
  const context = canvas.getContext('2d')
  if (!context) throw new Error('no canvas')
  context.fillStyle = BACKGROUND
  context.fillRect(0, 0, canvas.width, canvas.height)
  context.scale(SCALE, SCALE)
  context.drawImage(image, 0, 0, width, chartHeight)

  if (legend.length > 1) {
    const font = getComputedStyle(svg).fontFamily || 'sans-serif'
    context.font = `12px ${font}`
    context.textBaseline = 'middle'
    context.direction = rtl ? 'rtl' : 'ltr'
    const y = chartHeight + LEGEND_HEIGHT / 2
    const items = legend.map((item) => ({ ...item, width: 14 + 6 + context.measureText(item.label).width }))
    const total = items.reduce((sum, item) => sum + item.width, 0) + 16 * (items.length - 1)
    let x = (width - total) / 2
    for (const item of rtl ? [...items].reverse() : items) {
      // The swatch carries the colour; the words stay in the ink (dataviz).
      context.fillStyle = item.colour
      context.fillRect(rtl ? x + item.width - 14 : x, y - 5, 14, 10)
      context.fillStyle = INK
      context.textAlign = rtl ? 'right' : 'left'
      context.fillText(item.label, rtl ? x + item.width - 20 : x + 20, y)
      x += item.width + 16
    }
  }

  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('no picture'))), 'image/png'))
}
