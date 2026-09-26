function normalizeBreadcrumb() {
  const breadcrumb = document.querySelector('#breadcrumb')
  if (!breadcrumb) {
    return
  }

  // DocFX's modern renderer includes href="undefined" for active TOC
  // containers (for example Guides and API reference). They are directories,
  // not pages, so keep them as readable breadcrumb labels instead of links
  // that navigate to a literal /undefined URL.
  breadcrumb.querySelectorAll('a').forEach((link) => {
    const rawHref = link.getAttribute('href')
    if (rawHref === null || rawHref === '' || rawHref === '#' || rawHref === 'undefined' || rawHref === 'null') {
      const label = document.createElement('span')
      label.className = 'breadcrumb-item-label'
      label.innerHTML = link.innerHTML
      link.replaceWith(label)
    }
  })

  const list = breadcrumb.querySelector('ol.breadcrumb')
  if (list && list.children.length > 1) {
    const items = Array.from(list.children)
    const ordered = [...items].sort((left, right) => {
      const leftIsDirectory = left.querySelector('.breadcrumb-item-label') !== null
      const rightIsDirectory = right.querySelector('.breadcrumb-item-label') !== null
      return Number(rightIsDirectory) - Number(leftIsDirectory)
    })
    if (ordered.some((item, index) => item !== items[index])) {
      list.replaceChildren(...ordered)
    }
  }
}

function startNavigationRepair() {
  normalizeBreadcrumb()
  const breadcrumb = document.querySelector('#breadcrumb')
  if (!breadcrumb) {
    return
  }

  new MutationObserver(normalizeBreadcrumb).observe(breadcrumb, { childList: true, subtree: true })
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startNavigationRepair, { once: true })
} else {
  startNavigationRepair()
}

// DocFX loads this module as its theme-options file as well as a custom
// behavior module. Keep the default export required by the modern template.
export default {
  defaultTheme: 'dark',
}
