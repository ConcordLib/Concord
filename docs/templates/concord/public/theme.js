/* Concord docs theme controller — Protocol-style sidebar nav, theme/version
   dropdowns, breadcrumb, right-rail scrollspy, and code line numbers.
   DocFX's docfx.min.js still owns highlight.js, search wiring, and mermaid. */

(function () {
  'use strict';

  var root = document.documentElement;

  function meta(name) {
    var el = document.querySelector('meta[name="' + name + '"]');
    return el ? el.getAttribute('content') : null;
  }
  function urlPath(u) { try { return new URL(u, location.href).pathname; } catch (e) { return u; } }
  function samePage(a, b) { return urlPath(a) === urlPath(b); }
  function fetchJson(url) { return fetch(url).then(function (r) { return r.ok ? r.json() : null; }).catch(function () { return null; }); }

  /* ---------- theme ---------- */
  function resolveTheme(pref) {
    if (pref === 'auto') return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    return pref;
  }
  function applyTheme(pref) {
    var resolved = resolveTheme(pref);
    root.setAttribute('data-bs-theme', resolved);
    root.setAttribute('data-theme', resolved === 'dark' ? 'concord-dark' : 'concord-light');
    root.setAttribute('data-theme-pref', pref);
    root.classList.toggle('cc-dark', resolved === 'dark');
    root.classList.toggle('cc-light', resolved === 'light');
  }
  function wireDetailsDropdown(details, onSelect) {
    if (!details) return;
    function close() { if (details.open) details.open = false; }
    document.addEventListener('click', function (e) { if (details.open && !details.contains(e.target)) close(); });
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && details.open) close(); });
    details.addEventListener('click', function (e) {
      var item = e.target.closest('[data-action]');
      if (!item) return;
      if (onSelect) onSelect(item);
      close();
    });
  }
  function initThemeSwitcher() {
    var details = document.getElementById('cc-theme-dropdown');
    if (!details) return;
    wireDetailsDropdown(details, function (item) {
      var pref = item.getAttribute('data-theme-value');
      try { localStorage.setItem('theme', pref); } catch (e) {}
      applyTheme(pref);
    });
    var mq = window.matchMedia('(prefers-color-scheme: dark)');
    var listener = function () { if ((root.getAttribute('data-theme-pref') || 'auto') === 'auto') applyTheme('auto'); };
    if (mq.addEventListener) mq.addEventListener('change', listener); else if (mq.addListener) mq.addListener(listener);
  }

  /* ---------- version dropdown ---------- */
  function initVersionSelector() {
    var details = document.getElementById('cc-version-dropdown');
    if (!details) return;
    var label = 'latest';
    var m = location.pathname.match(/^\/(v\d+\.\d+(?:\.\d+)?|latest|unstable)\//);
    if (m) label = m[1];
    details.innerHTML =
      '<summary class="btn btn-ghost btn-sm gap-1" aria-haspopup="true">' +
        '<span id="cc-version-label">' + label + '</span>' +
        '<svg xmlns="http://www.w3.org/2000/svg" class="size-3.5 opacity-60" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>' +
      '</summary>' +
      '<ul class="menu dropdown-content z-[60] mt-2 w-40 rounded-box border border-base-300 bg-base-200 p-1 shadow-lg" role="menu">' +
        '<li><button type="button" data-action="version" data-version="latest">latest</button></li>' +
      '</ul>';
    function navigate(version) {
      if (!version || version === 'current') return;
      var u = new URL(location.href);
      var path = u.pathname.replace(/^\/(latest|unstable|v\d+\.\d+(?:\.\d+)?)\//, '/');
      location.href = '/' + version + path + u.search + u.hash;
    }
    wireDetailsDropdown(details, function (item) { navigate(item.getAttribute('data-version')); });
    fetch('/versions.json').then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
      var latest = (data && data.latest) || 'latest';
      var versions = (data && data.versions) || [];
      var latestLabel = latest === 'latest' ? 'latest' : 'latest (' + latest + ')';
      var html = '<li><button type="button" data-action="version" data-version="latest">' + latestLabel + '</button></li>';
      versions.forEach(function (v) { html += '<li><button type="button" data-action="version" data-version="' + v + '">' + v + '</button></li>'; });
      var menu = details.querySelector('#cc-version-dropdown > ul') || details.querySelector('ul');
      if (menu) menu.innerHTML = html;
    }).catch(function () {});
  }

  /* ---------- mobile sidebar drawer ---------- */
  function initSidebarDrawer() {
    var sidebar = document.getElementById('cc-sidebar');
    var backdrop = document.getElementById('cc-nav-backdrop');
    var openBtn = document.getElementById('cc-nav-open');
    if (!sidebar || !openBtn) return;
    var mobileQuery = window.matchMedia('(max-width: 1023px)');
    function isOpen() { return mobileQuery.matches && !sidebar.classList.contains('-translate-x-full'); }
    function visibleFocusables() {
      return Array.prototype.slice.call(sidebar.querySelectorAll('a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])')).filter(function (el) {
        return el.getClientRects().length > 0;
      });
    }
    function syncAccessibility() {
      var hidden = mobileQuery.matches && sidebar.classList.contains('-translate-x-full');
      if (hidden) sidebar.setAttribute('inert', ''); else sidebar.removeAttribute('inert');
      sidebar.setAttribute('aria-hidden', String(hidden));
      openBtn.setAttribute('aria-expanded', String(isOpen()));
      if (isOpen()) {
        sidebar.setAttribute('role', 'dialog');
        sidebar.setAttribute('aria-modal', 'true');
        sidebar.setAttribute('aria-label', 'Site navigation');
      } else {
        sidebar.removeAttribute('role');
        sidebar.removeAttribute('aria-modal');
        sidebar.removeAttribute('aria-label');
      }
    }
    function open() {
      sidebar.classList.remove('-translate-x-full');
      if (backdrop) backdrop.classList.remove('hidden');
      document.body.style.overflow = 'hidden';
      syncAccessibility();
      var focusables = visibleFocusables();
      try { (focusables[0] || sidebar).focus(); } catch (e) {}
    }
    function close(restoreFocus) {
      var wasOpen = isOpen();
      sidebar.classList.add('-translate-x-full');
      if (backdrop) backdrop.classList.add('hidden');
      document.body.style.overflow = '';
      syncAccessibility();
      if (wasOpen && restoreFocus !== false) { try { openBtn.focus(); } catch (e) {} }
    }
    openBtn.addEventListener('click', open);
    if (backdrop) backdrop.addEventListener('click', function () { close(true); });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && isOpen()) { e.preventDefault(); close(true); return; }
      if (e.key !== 'Tab' || !isOpen()) return;
      var focusables = visibleFocusables();
      if (!focusables.length) { e.preventDefault(); sidebar.focus(); return; }
      var first = focusables[0], last = focusables[focusables.length - 1];
      if (e.shiftKey && (document.activeElement === first || !sidebar.contains(document.activeElement))) {
        e.preventDefault(); last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault(); first.focus();
      }
    });
    sidebar.addEventListener('click', function (e) { if (e.target.closest('a')) close(false); });
    var onBreakpointChange = function () {
      if (!mobileQuery.matches) {
        if (backdrop) backdrop.classList.add('hidden');
        document.body.style.overflow = '';
      }
      syncAccessibility();
    };
    if (mobileQuery.addEventListener) mobileQuery.addEventListener('change', onBreakpointChange); else mobileQuery.addListener(onBreakpointChange);
    syncAccessibility();
  }

  /* ---------- full sidebar TOC (docs + api) ---------- */
  var CHEVRON = '<svg xmlns="http://www.w3.org/2000/svg" class="size-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><path d="m9 18 6-6-6-6"/></svg>';

  function subtreeHasActive(items, base, here) {
    for (var i = 0; i < items.length; i++) {
      var h = items[i].topicHref || items[i].href;
      if (h && samePage(new URL(h, base).href, here)) return true;
      if (items[i].items && subtreeHasActive(items[i].items, base, here)) return true;
    }
    return false;
  }

  function buildTocList(items, base, here, depth) {
    var ul = document.createElement('ul');
    ul.className = 'cc-toc-list' + (depth ? ' cc-toc-sub' : '');
    items.forEach(function (it) {
      if (!it.name) return;
      var li = document.createElement('li');
      li.className = 'cc-toc-item';
      var href = it.topicHref || it.href;
      var a = document.createElement('a');
      a.className = 'cc-toc-link';
      if (href) {
        a.href = new URL(href, base).href;
        if (samePage(a.href, here)) {
          a.classList.add('active');
          a.setAttribute('aria-current', 'page');
        }
      }
      else { a.classList.add('cc-toc-heading'); }
      a.textContent = it.name;
      li.appendChild(a);

      if (it.items && it.items.length) {
        var subActive = (href && samePage(new URL(href, base).href, here)) || subtreeHasActive(it.items, base, here);
        var sub = buildTocList(it.items, base, here, depth + 1);
        sub.classList.add('cc-toc-children');
        if (!subActive) sub.classList.add('hidden');
        var toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'cc-toc-toggle' + (subActive ? ' open' : '');
        toggle.innerHTML = CHEVRON;
        toggle.setAttribute('aria-expanded', subActive ? 'true' : 'false');
        toggle.setAttribute('aria-label', (subActive ? 'Collapse ' : 'Expand ') + it.name);
        toggle.addEventListener('click', function (e) {
          e.preventDefault();
          var open = !sub.classList.contains('hidden');
          sub.classList.toggle('hidden', open);
          toggle.classList.toggle('open', !open);
          toggle.setAttribute('aria-expanded', String(!open));
          toggle.setAttribute('aria-label', (open ? 'Expand ' : 'Collapse ') + it.name);
        });
        li.classList.add('cc-toc-expandable');
        li.insertBefore(toggle, a);
        li.appendChild(sub);
      }
      ul.appendChild(li);
    });
    return ul;
  }

  function initSidebarToc() {
    var box = document.getElementById('cc-toc');
    if (!box) return;
    box.innerHTML = '';
    var navrel = meta('docfx:navrel');
    if (!navrel) { box.innerHTML = '<p class="px-3 text-sm text-base-content/40">No navigation.</p>'; return; }
    var rootUrl = new URL(navrel.replace(/\.html$/i, '.json'), location.href);
    var here = location.pathname;
    fetchJson(rootUrl.href).then(function (root) {
      if (!root || !root.items || !root.items.length) return;
      var sections = root.items.filter(function (s) { return s.tocHref || s.href; });
      Promise.all(sections.map(function (sec) {
        var secUrl = new URL((sec.tocHref || sec.href).replace(/\.html$/i, '.json'), rootUrl);
        return fetchJson(secUrl.href).then(function (d) { return { name: sec.name, base: secUrl, items: (d && d.items) || [{ name: sec.name, href: sec.topicHref || sec.href }] }; });
      })).then(function (groups) {
        groups.forEach(function (g, index) {
          var section = document.createElement('section');
          section.className = 'cc-toc-section';
          var h = document.createElement('h2');
          h.className = 'cc-toc-group';
          h.id = 'cc-toc-group-' + index;
          h.textContent = g.name;
          section.setAttribute('aria-labelledby', h.id);
          var list = buildTocList(g.items, g.base, here, 0);
          list.classList.add('cc-toc-root');
          section.appendChild(h);
          section.appendChild(list);
          box.appendChild(section);
        });
        // scroll the active link into view in the sidebar
        var active = box.querySelector('.cc-toc-link.active');
        if (active && active.scrollIntoView) active.scrollIntoView({ block: 'nearest' });
      });
    });
  }

  /* ---------- breadcrumb ---------- */
  function findActive(items, here) {
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      if (it.topicHref && samePage(it.topicHref, here)) return it;
      if (it.href && samePage(it.href, here)) return it;
      if (it.items) { var sub = findActive(it.items, here); if (sub) return sub; }
    }
    return null;
  }
  function ancestorsOf(items, active, chain) {
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var hit = (it.topicHref && samePage(it.topicHref, active.topicHref || '')) || (it.href && samePage(it.href, active.href || ''));
      if (hit) return chain.slice();
      if (it.items) {
        var before = chain.length;
        chain.push({ name: it.name, href: it.topicHref || it.href });
        var found = ancestorsOf(it.items, active, chain);
        if (found) return found;
        chain.length = before;
      }
    }
    return null;
  }
  function initBreadcrumb() {
    var crumb = document.getElementById('cc-breadcrumb');
    if (!crumb) return;
    crumb.innerHTML = '';
    var navrel = meta('docfx:navrel');
    var tocrel = meta('docfx:tocrel');
    var here = location.pathname.split('#')[0].split('?')[0];
    var navUrl = navrel ? new URL(navrel.replace(/\.html$/i, '.json'), location.href) : null;
    var tocUrl = tocrel ? new URL(tocrel.replace(/\.html$/i, '.json'), location.href) : null;
    var navP = navUrl ? fetchJson(navUrl.href) : Promise.resolve(null);
    var tocP = tocUrl ? fetchJson(tocUrl.href) : Promise.resolve(null);
    Promise.all([navP, tocP]).then(function (res) {
      var navData = res[0], tocData = res[1];
      var chain = [];
      if (navData && navData.items) {
        for (var i = 0; i < navData.items.length; i++) {
          var n = navData.items[i];
          var prefix = urlPath(n.topicHref || n.href).replace(/\/[^/]*$/, '/');
          if (prefix !== '/' && (here + '/').indexOf(prefix) === 0) { chain.push({ name: n.name, href: new URL(n.topicHref || n.href, navUrl).href }); break; }
        }
      }
      if (tocData && tocData.items) {
        var active = findActive(tocData.items, here);
        if (active) { var anc = ancestorsOf(tocData.items, active, []) || []; chain = chain.concat(anc); }
      }
      if (!chain.length) { crumb.classList.add('hidden'); return; }
      crumb.classList.remove('hidden');
      var ol = document.createElement('ol');
      ol.className = 'cc-breadcrumb-list';
      chain.forEach(function (c, idx) {
        var li = document.createElement('li');
        li.className = 'cc-breadcrumb-item' + (idx === chain.length - 1 ? ' active' : '');
        if (c.href && idx !== chain.length - 1) { var a = document.createElement('a'); a.href = c.href; a.textContent = c.name; li.appendChild(a); }
        else { li.textContent = c.name; }
        ol.appendChild(li);
      });
      crumb.appendChild(ol);
    });
  }

  /* ---------- right-rail scrollspy ---------- */
  function affixLabel(heading) {
    var copy = heading.cloneNode(true);
    copy.querySelectorAll('.anchorjs-link').forEach(function (anchor) { anchor.remove(); });
    return copy.textContent.trim();
  }
  function buildAffixTree(headings) {
    var root = { level: 1, items: [] };
    var parents = [root];
    headings.forEach(function (heading) {
      var level = Number(heading.tagName.slice(1));
      var item = { heading: heading, level: level, items: [] };
      while (parents.length > 1 && parents[parents.length - 1].level >= level) parents.pop();
      parents[parents.length - 1].items.push(item);
      parents.push(item);
    });
    return root.items;
  }
  function renderAffixItems(items) {
    var ul = document.createElement('ul');
    items.forEach(function (item) {
      var li = document.createElement('li');
      var a = document.createElement('a');
      a.className = 'cc-affix-link cc-affix-level-' + item.level;
      a.href = '#' + item.heading.id;
      a.textContent = affixLabel(item.heading);
      li.appendChild(a);
      if (item.items.length) li.appendChild(renderAffixItems(item.items));
      ul.appendChild(li);
    });
    return ul;
  }
  function renderAffix(affix) {
    var headings = Array.prototype.slice.call(document.querySelectorAll(
      'article h2:not(.no-anchor), article h3:not(.no-anchor), article h4:not(.no-anchor)'
    )).filter(function (heading) { return heading.id; });
    affix.replaceChildren();
    if (!headings.length) return [];

    var title = document.createElement('h2');
    title.id = 'cc-affix-title';
    title.textContent = meta('loc:inThisArticle') || 'Table of Contents';
    affix.setAttribute('aria-labelledby', title.id);
    affix.removeAttribute('aria-label');
    affix.appendChild(title);
    affix.appendChild(renderAffixItems(buildAffixTree(headings)));
    return affix.querySelectorAll('a[href^="#"]');
  }
  function initAffixScrollspy() {
    var affix = document.getElementById('affix');
    if (!affix) return;
    var tries = 0;
    (function wait() {
      if ((!window.docfx || !window.docfx.ready) && tries++ < 50) { setTimeout(wait, 100); return; }
      var links = renderAffix(affix);
      if (!links.length) return;
      wireScrollspy(links);
    })();
  }
  function wireScrollspy(links) {
    var targets = [];
    links.forEach(function (a) { var el = document.getElementById(a.getAttribute('href').slice(1)); if (el) targets.push({ el: el, a: a }); });
    if (!targets.length) return;
    var ticking = false;
    function update() {
      // Activate the section whose heading has crossed an activation line set a bit
      // below the fixed header, so the highlighted entry matches what's actually being
      // read rather than lagging until the heading reaches the very top.
      var line = Math.max(96, window.innerHeight * 0.22);
      var active = null;
      for (var i = 0; i < targets.length; i++) {
        if (targets[i].el.getBoundingClientRect().top <= line) active = targets[i];
        else break;
      }
      // Before the first heading passes the line, keep the first entry lit.
      if (!active) active = targets[0];
      // At the bottom of the page, force the last entry so short trailing sections
      // that never reach the line still get highlighted.
      if (window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2) active = targets[targets.length - 1];
      links.forEach(function (a) {
        var isActive = a === active.a;
        a.classList.toggle('cc-affix-active', isActive);
        if (isActive) a.setAttribute('aria-current', 'location');
        else a.removeAttribute('aria-current');
      });
      ticking = false;
    }
    function onScroll() { if (!ticking) { ticking = true; requestAnimationFrame(update); } }
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll);
    update();
  }

  /* ---------- code line numbers ---------- */
  function addLineNumbers() {
    document.querySelectorAll('pre code').forEach(function (code) {
      if (code.dataset.ccLn) return;
      var pre = code.parentElement;
      if (!pre || pre.tagName !== 'PRE') return;
      code.dataset.ccLn = '1';
      var lineCount = code.textContent.split('\n').length;
      if (code.textContent.endsWith('\n')) lineCount--;
      var gutter = document.createElement('div');
      gutter.className = 'cc-line-gutter';
      for (var i = 1; i <= lineCount; i++) { var s = document.createElement('span'); s.textContent = i; gutter.appendChild(s); }
      pre.insertBefore(gutter, code);
    });
  }
  function watchHighlight() {
    var attempt = 0;
    function check() {
      var blocks = document.querySelectorAll('pre code');
      var done = true;
      blocks.forEach(function (cb) { if (!cb.classList.contains('hljs') && !cb.dataset.ccLn) done = false; });
      if (done || attempt > 12) addLineNumbers();
      else { attempt++; setTimeout(check, 200); }
    }
    check();
  }


  /* ---------- command palette (Orama search, substring fallback) ---------- */
  var _oramaDb = null;
  var _searchEntries = null;
  var _searchRel = '';
  var _oramaFailed = false;
  var ORAMA_CDN = 'https://cdn.jsdelivr.net/npm/@orama/orama/+esm';

  function escHtml(s) {
    return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }
  function highlight(text, q) {
    var lower = text.toLowerCase(), ql = q.toLowerCase(), idx = lower.indexOf(ql);
    if (idx < 0) return escHtml(text);
    return escHtml(text.slice(0, idx)) + '<b>' + escHtml(text.slice(idx, idx + q.length)) + '</b>' + escHtml(text.slice(idx + q.length));
  }
  function truncate(s, n) { return s.length > n ? s.slice(0, n) + '\u2026' : s; }

  async function ensureSearchIndex() {
    if (_oramaDb || (_oramaFailed && _searchEntries)) return;
    var rel = meta('docfx:rel') || '';
    _searchRel = rel;
    var data = await fetchJson(rel + 'index.json');
    _searchEntries = data || {};
    if (_oramaFailed) return;
    try {
      var Orama = await import(ORAMA_CDN);
      var db = await Orama.create({ schema: { title: 'string', summary: 'string', href: 'string' } });
      var entries = Object.keys(_searchEntries);
      for (var i = 0; i < entries.length; i++) {
        var e = _searchEntries[entries[i]];
        await Orama.insert(db, { title: e.title || e.href || entries[i], summary: e.summary || '', href: e.href || entries[i] });
      }
      _oramaDb = { db: db, Orama: Orama };
    } catch (err) {
      console.warn('Orama unavailable, using substring fallback:', err);
      _oramaFailed = true;
    }
  }

  function renderSearchResults(docs, query) {
    var container = document.getElementById('search-results');
    if (!container) return;
    if (!docs.length) {
      container.innerHTML = '<div class="no-result">No results for "' + escHtml(query) + '"</div>';
      return;
    }
    var rel = _searchRel;
    var html = '<div class="search-list">' + docs.length + ' result' + (docs.length > 1 ? 's' : '') + ' for "' + escHtml(query) + '"</div>';
    html += '<div class="sr-items">';
    docs.forEach(function (entry) {
      var href = rel + (entry.href || '');
      var title = highlight(entry.title || entry.href || '', query);
      var brief = highlight(truncate(entry.summary || '', 200), query);
      html += '<div class="sr-item"><div class="item-title"><a href="' + escHtml(href) + '">' + title + '</a></div><div class="item-href">' + escHtml(entry.href || '') + '</div><div class="item-brief">' + brief + '</div></div>';
    });
    html += '</div>';
    container.innerHTML = html;
  }

  async function runSearch(query) {
    var container = document.getElementById('search-results');
    if (!container) return;
    var q = (query || '').trim();
    if (!q) { container.innerHTML = ''; return; }
    await ensureSearchIndex();
    if (_oramaDb) {
      try {
        var res = await _oramaDb.Orama.search(_oramaDb.db, {
          term: q, properties: ['title', 'summary'], tolerance: 1, limit: 20, boost: { title: 2, summary: 1 }
        });
        renderSearchResults((res.hits || res.result || []).map(function (h) { return h.document || h; }), q);
        return;
      } catch (e) { console.warn('Orama search error, falling back:', e); }
    }
    // Substring fallback
    if (!_searchEntries) return;
    var ql = q.toLowerCase(), results = [];
    for (var key in _searchEntries) {
      var entry = _searchEntries[key];
      var title = (entry.title || entry.href || key).toLowerCase();
      var summary = (entry.summary || '').toLowerCase();
      var ti = title.indexOf(ql), si = summary.indexOf(ql);
      if (ti < 0 && si < 0) continue;
      var score = (ti === 0 ? 100 : ti > 0 ? 50 : 0) + (si >= 0 ? 10 : 0);
      results.push({ entry: entry, score: score });
    }
    results.sort(function (a, b) { return b.score - a.score; });
    renderSearchResults(results.slice(0, 20).map(function (r) { return r.entry; }), q);
  }

  function initCommandPalette() {
    var cmdk = document.getElementById('cc-cmdk');
    if (!cmdk) return;
    var input = document.getElementById('cc-search-input');
    var triggers = Array.prototype.slice.call(document.querySelectorAll('#cc-search-trigger, #cc-search-trigger-mobile'));
    var lastTrigger = null;
    var isMac = /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent || '');
    document.querySelectorAll('[data-cc-kbd]').forEach(function (k) { k.textContent = isMac ? '\u2318K' : 'Ctrl K'; });

    var debounceTimer = null;
    function onInput() {
      clearTimeout(debounceTimer);
      debounceTimer = setTimeout(function () { runSearch(input ? input.value : ''); }, 120);
    }
    function setExpanded(expanded) {
      triggers.forEach(function (trigger) { trigger.setAttribute('aria-expanded', String(expanded)); });
    }
    function focusables() {
      return Array.prototype.slice.call(cmdk.querySelectorAll('a[href], button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])')).filter(function (el) {
        return el.getClientRects().length > 0;
      });
    }
    function open(trigger) {
      lastTrigger = trigger || document.activeElement;
      cmdk.classList.remove('hidden');
      document.body.style.overflow = 'hidden';
      setExpanded(true);
      if (input) { try { input.focus(); input.select(); } catch (e) {} }
      ensureSearchIndex();
    }
    function close(restoreFocus) {
      cmdk.classList.add('hidden');
      document.body.style.overflow = '';
      setExpanded(false);
      if (input) input.value = '';
      var c = document.getElementById('search-results');
      if (c) c.innerHTML = '';
      if (restoreFocus !== false && lastTrigger && typeof lastTrigger.focus === 'function') {
        try { lastTrigger.focus(); } catch (e) {}
      }
    }
    function toggle() { cmdk.classList.contains('hidden') ? open(document.activeElement) : close(true); }

    triggers.forEach(function (trigger) { trigger.addEventListener('click', function () { open(trigger); }); });
    document.addEventListener('keydown', function (e) {
      if ((e.metaKey || e.ctrlKey) && (e.key === 'k' || e.key === 'K')) { e.preventDefault(); toggle(); }
      else if (e.key === 'Escape' && !cmdk.classList.contains('hidden')) { e.preventDefault(); close(true); }
      else if (e.key === 'Tab' && !cmdk.classList.contains('hidden')) {
        var items = focusables();
        if (!items.length) { e.preventDefault(); return; }
        var first = items[0], last = items[items.length - 1];
        if (e.shiftKey && (document.activeElement === first || !cmdk.contains(document.activeElement))) {
          e.preventDefault(); last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault(); first.focus();
        }
      }
    });
    var backdrop = cmdk.querySelector('.cc-cmdk-backdrop');
    if (backdrop) backdrop.addEventListener('click', function () { close(true); });
    if (input) input.addEventListener('input', onInput);

    var results = document.getElementById('search-results');
    if (results) {
      results.addEventListener('click', function (e) {
        var a = e.target.closest('a');
        if (!a || !a.getAttribute('href')) return;
        e.preventDefault();
        close(false);
        location.href = a.href;
      });
    }
  }

  /* ---------- unwrap docfx's open-in-new-tab image links ---------- */
  /* docfx.min.js wraps every <article img> in <a target="_blank"> linking to the
     full image. We don't want content images opening a new tab, so strip that wrapper.
     The wrap happens on image load, so unwrap on an initial pass and keep watching. */
  function unwrapImageLink(a) {
    if (!a || a.tagName !== 'A' || a.target !== '_blank') return;
    if (a.children.length !== 1) return;
    var only = a.children[0];
    if (only.tagName !== 'IMG' && only.tagName !== 'PICTURE') return;
    a.replaceWith(only);
  }
  function unwrapContentImages(root) {
    (root || document).querySelectorAll('article a[target="_blank"]').forEach(unwrapImageLink);
  }
  function initImageUnwrap() {
    var article = document.querySelector('article');
    if (!article) return;
    unwrapContentImages(article);
    var observer = new MutationObserver(function (mutations) {
      mutations.forEach(function (m) {
        m.addedNodes.forEach(function (node) {
          if (node.nodeType !== 1) return;
          if (node.tagName === 'A') unwrapImageLink(node);
          else if (node.querySelectorAll) unwrapContentImages(node);
        });
      });
    });
    observer.observe(article, { childList: true, subtree: true });
    window.addEventListener('load', function () {
      unwrapContentImages(article);
      setTimeout(function () { observer.disconnect(); }, 1000);
    });
  }

  function init() {
    initThemeSwitcher();
    initVersionSelector();
    initSidebarDrawer();
    initSidebarToc();
    initBreadcrumb();
    initAffixScrollspy();
    watchHighlight();
    initCommandPalette();
    initImageUnwrap();
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
