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

function createGitHubLink() {
  const link = document.createElement('a')
  link.className = 'cf-github-link nav-link'
  link.href = 'https://github.com/mirrorpulse/cfsharp'
  link.target = '_blank'
  link.rel = 'noopener noreferrer'
  link.setAttribute('aria-label', 'CfSharp on GitHub')
  link.setAttribute('title', 'CfSharp on GitHub')
  link.innerHTML = '<svg viewBox="0 0 16 16" aria-hidden="true" focusable="false"><path d="M8 0C3.58 0 0 3.64 0 8.13c0 3.59 2.29 6.63 5.47 7.7.4.08.55-.18.55-.39 0-.19-.01-.7-.01-1.38-2.23.5-2.7-1.1-2.7-1.1-.36-.95-.89-1.2-.89-1.2-.73-.51.06-.5.06-.5.81.06 1.23.85 1.23.85.72 1.26 1.89.9 2.35.69.07-.53.28-.9.51-1.11-1.78-.21-3.65-.91-3.65-4.03 0-.89.31-1.61.82-2.18-.08-.21-.36-1.04.08-2.16 0 0 .67-.22 2.2.83A7.42 7.42 0 0 1 8 3.86c.68 0 1.36.1 2 .3 1.53-1.05 2.2-.83 2.2-.83.44 1.12.16 1.95.08 2.16.51.57.82 1.29.82 2.18 0 3.13-1.88 3.81-3.67 4.02.29.26.54.77.54 1.55 0 1.12-.01 2.02-.01 2.29 0 .22.15.48.56.39C13.71 14.76 16 11.72 16 8.13 16 3.64 12.42 0 8 0Z"/></svg><span class="visually-hidden">CfSharp on GitHub</span>'
  return link
}

function findNavigationTarget(navbar) {
  const selectors = [
    'button[aria-label*="theme" i]',
    '[data-bs-theme-toggle]',
    '.theme-toggle',
    '#search',
    'form.search',
  ]
  for (const selector of selectors) {
    const element = navbar.querySelector(selector)
    if (element) {
      return element.closest('#navbar > *') || element
    }
  }
  return null
}

function ensureGitHubLink() {
  const navbar = document.querySelector('#navbar')
  if (!navbar) {
    return false
  }

  let link = navbar.querySelector(':scope > .cf-github-link')
  if (!link) {
    link = createGitHubLink()
  }

  const target = findNavigationTarget(navbar)
  if (target && target !== link && link.nextElementSibling !== target) {
    navbar.insertBefore(link, target)
  } else if (!target && link.parentElement !== navbar) {
    navbar.append(link)
  }
  return true
}

function startGitHubLink() {
  if (ensureGitHubLink()) {
    const navbar = document.querySelector('#navbar')
    new MutationObserver(ensureGitHubLink).observe(navbar, { childList: true, subtree: true })
    return
  }

  const observer = new MutationObserver(() => {
    if (ensureGitHubLink()) {
      observer.disconnect()
      startGitHubLink()
    }
  })
  observer.observe(document.body, { childList: true, subtree: true })
}

function startCustomNavigation() {
  startNavigationRepair()
  startGitHubLink()
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', startCustomNavigation, { once: true })
} else {
  startCustomNavigation()
}

// DocFX loads this module as its theme-options file as well as a custom
// behavior module. Keep the default export required by the modern template.
export default {
  defaultTheme: 'dark',
}
