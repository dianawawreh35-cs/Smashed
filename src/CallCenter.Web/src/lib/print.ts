/**
 * Printing a report, or a page of them, to paper or to PDF (26 Sep, Dia).
 *
 * The browser prints what the page shows; the print stylesheet in index.css
 * decides what that is: no sidebar, header, filter controls or buttons, white
 * paper, charts to the page's width, and a printed heading saying what the
 * report covers. Saving as PDF is the browser's own "Save as PDF" printer, so
 * nothing is added to the app to make one.
 *
 * Printing **one report** marks it and the page, so the stylesheet hides every
 * other report for the length of the print and puts them back afterwards.
 */

/** The page, as it is: every report on the open tab, under the printed heading. */
export function printPage(): void {
  window.print()
}

/** One report card and the printed heading, nothing else on the page. */
export function printOnly(element: HTMLElement): void {
  const body = document.body
  const done = () => {
    delete body.dataset.printOne
    delete element.dataset.printing
    window.removeEventListener('afterprint', done)
  }

  body.dataset.printOne = 'true'
  element.dataset.printing = 'true'
  window.addEventListener('afterprint', done)
  window.print()
}
