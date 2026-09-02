# RapidRelief Design System — MANDATORY READ

> **Every agent (Copilot, Claude, Antigravity, …) and every teammate MUST read this file before
> creating or changing any page, component, or style.** It has the same authority as the
> architecture rules in [PROJECT-CONTEXT.md](PROJECT-CONTEXT.md) §4. If a UI decision isn't
> covered here, follow the closest pattern in the codebase and add a decision note to §7 of
> PROJECT-CONTEXT.md.

The single source of truth for tokens is
[src/RapidRelief.Client/wwwroot/css/app.css](src/RapidRelief.Client/wwwroot/css/app.css).
This document explains how to use it.

---

## 1. Design identity

| Aspect | Decision |
| --- | --- |
| Personality | Calm, trustworthy, government/health-grade. Premium through spacing, hierarchy and consistency — never through flashiness. |
| Style | "Accessible & Ethical": high contrast, 16px+ body text, visible focus, WCAG-first. |
| Typeface | **Lexend** (variable, vendored in `wwwroot/fonts/` — never a CDN link). Fallback: `Segoe UI, system-ui`. Mono for ids/code: `var(--rr-font-mono)`. |
| Color roles | **Blue = action** (`--rr-primary`). **Red = emergency/destructive ONLY** (`--rr-danger`, brand mark). Never make ordinary buttons red — red must keep its alarm meaning in a disaster app. |
| Modes | Light + dark are first-class. Both must be checked before shipping any page. |
| Motion | Subtle and purposeful: 150–300 ms, small fades/rises. Everything dies under `prefers-reduced-motion`. |
| Anti-patterns | No emojis as icons. No complex/multi-layer shadows. No 3D. No color-only status indicators (always pair color with text or an icon). No `MarkupString`/`innerHTML` (enforced by `ClientRenderSafetyTests`). |

## 2. Tokens (use these, never hex values)

Defined in `app.css` on `:root` (light) and `[data-theme='dark']` (dark). Components written
with tokens are automatically correct in both themes.

```text
Surfaces   --rr-bg  --rr-surface  --rr-surface-2  --rr-surface-3
Text       --rr-text  --rr-text-2 (secondary)  --rr-text-3 (meta/captions)
Borders    --rr-border  --rr-border-strong
Action     --rr-primary  --rr-primary-hover  --rr-on-primary  --rr-link
Emergency  --rr-danger  --rr-danger-hover  --rr-brand
Soft pairs --rr-{danger|warning|success|info}-soft / -soft-text / -soft-border
Status     --rr-success
Focus      --rr-focus (3px outline, applied globally via :focus-visible)
Shadow     --rr-shadow (cards)  --rr-shadow-pop (popovers/toasts only)
Radius     --rr-radius-s (6) --rr-radius (10) --rr-radius-l (14, cards) --rr-radius-pill
Spacing    --space-1..12 (0.25rem steps of an 8px rhythm)
Z-index    --z-sticky 100 · --z-drawer 1100 · --z-popover 1200 · --z-toast 1300
Motion     --rr-ease  --rr-fast (150ms)  --rr-base (240ms)
```

**Hard rule:** scoped `.razor.css` files may only use `var(--rr-*)` / `var(--space-*)` tokens
for colors, spacing, radii, shadows and z-index. Raw hex values in a feature stylesheet are a
review-blocker.

## 3. Ready-made components & classes

Reuse before inventing. All of these are in `app.css` unless noted.

| Need | Use |
| --- | --- |
| Page container | `<div class="rr-page">` (1100px) or `rr-page-narrow` (720px) |
| Page header | `rr-page-head` + `<h1>` + `<p class="rr-page-lede">` |
| Card/section | `rr-card` (+ `rr-section` for bottom margin) |
| Status chip | `rr-chip` + `rr-chip-{success,warning,danger,info}`; pair with `rr-dot rr-dot-*` |
| Empty state | `rr-empty` → icon + `<strong>` + `<p>` + optional CTA button |
| Loading | `rr-skeleton` blocks (`style="width:…;height:…"`), container gets `aria-busy="true"` |
| Table | wrap `<table class="table">` in `rr-table-wrap` |
| Forms | Bootstrap classes (`form-label`, `form-control`, `mb-3`) — already themed. Help: `rr-field-help`. Errors: `rr-field-error`. Max width via `rr-form` |
| Buttons | `btn btn-primary` (main action, one per view), `btn-secondary`, `btn-outline-secondary`, `btn-danger` (destructive only), `btn-link`. Icon-only: `rr-icon-btn` + `aria-label` |
| Alerts | Bootstrap `alert alert-{danger,warning,info,success}` — themed soft surfaces |
| Auth screens | `rr-auth-shell` > `rr-auth-card` (+ `rr-auth-brand`, `rr-auth-lede`, `rr-auth-foot`) |
| Icons | `<AppIcon Name="map-pin" Size="19" />` ([Common/Ui/AppIcon.razor](src/RapidRelief.Client/Common/Ui/AppIcon.razor)). Add new glyphs THERE (Feather-style, 24 grid, stroke 1.8). Never emoji, never a second icon style |
| Theme toggle | `<ThemeToggle />` — already in the header; don't add more |
| Router states | styled `rr-empty rr-router-state` blocks live in `App.razor` |
| Map | `<RapidMap …/>` (`.rapid-map` = 480px, rounded) |

Bootstrap 5.1 is themed via the "Bootstrap bridge" section of `app.css` — standard Bootstrap
markup (cards, badges, tables, list-groups) automatically matches the design system in both
themes. Prefer the `rr-*` primitives for new work.

## 4. Layout & shell

- The shell ([MainLayout](src/RapidRelief.Client/Layout/MainLayout.razor)) provides: skip-link,
  sidebar (drawer < 900px), sticky glass header ([AppHeader](src/RapidRelief.Client/Layout/AppHeader.razor)),
  `<main id="rr-main">`. Pages render inside — never add another `position: sticky` header.
- Add nav links in [NavMenu.razor](src/RapidRelief.Client/Layout/NavMenu.razor) under the right
  section (`Response` / `Personal` (authed) / `Administration`) with an `AppIcon` and, for
  role-gated pages, the `nav-tag` badge.
- Breakpoints: **900px** (sidebar/drawer switch), 640px, 480px, 400px for fine-tuning.
  Test every page at **375px** and desktop before calling it done.
- The header is the app's ONLY translucent ("glass") surface. Do not add more glassmorphism.

## 5. Theme rules (dark + light)

- Theme = `data-theme` attribute on `<html>`, set pre-paint by
  [js/theme.js](src/RapidRelief.Client/wwwroot/js/theme.js) (localStorage `rr-theme`, falls back
  to `prefers-color-scheme`, follows OS while no explicit choice).
- localStorage may hold **UI preferences only** — never tokens, never PII (F1 rule stands).
- Never branch on the theme in C# or markup. Write token-based CSS and both themes work.
- New colors: add a token to BOTH `:root` and `[data-theme='dark']`, keeping ≥4.5:1 contrast
  for text (≥3:1 for large/secondary), then use the token.

## 6. Motion rules

- Page/section entrances: `rr-reveal` (+ `rr-reveal-1..4` for ≤5 staggered items). Nothing else
  animates on load.
- Interactive transitions: 150ms (`--rr-fast`) using the existing patterns (background, color,
  small translate). No bounces, no parallax, no infinite decoration (skeleton shimmer and the
  assistant "thinking" pulse are the only loops).
- `prefers-reduced-motion: reduce` is globally enforced in `app.css` — never override it.
- GSAP is intentionally NOT used: CSS covers our subtle tier, keeps the CSP strict and the
  bundle small (see D-067). Don't add animation libraries without a team decision.

## 7. Content & tone

- Voice: calm, direct, action-first. No jargon toward citizens; technical detail belongs on the
  sandbox page or in `rr-field-help`.
- Emergency escalation ("call **999**") must stay visible on Home, the nav footer and the
  assistant banner. Never remove it.
- Errors tell the user what happened AND what to do next. Empty states always name the next
  action. Degraded mode (D-005) is worded as "Demo mode — live data limited" for citizens.
- User/AI text is always rendered with `@`-interpolation (automatic escaping). `MarkupString`,
  `innerHTML` and Markdown renderers are forbidden and test-enforced.

## 8. Definition of Done for any UI change

1. Light AND dark mode checked (toggle in header).
2. 375px AND desktop checked; no horizontal overflow.
3. Keyboard: every interactive element reachable, visible focus ring, icon-only buttons have
   `aria-label`; exactly one `<h1>` per page (router focuses it on navigation).
4. Loading, empty, and error/degraded states exist (skeleton / `rr-empty` / alert patterns).
5. No new hex colors, no emoji icons, no new fonts/CDNs (offline rule — vendor everything).
6. `dotnet test` green (architecture guards scan client sources).
7. PROJECT-CONTEXT.md updated per repo rules.

## 9. New-page recipe (copy this)

```razor
@page "/myfeature"

<PageTitle>My feature — RapidRelief</PageTitle>

<div class="rr-page">
    <header class="rr-page-head rr-reveal">
        <h1>My feature</h1>
        <p class="rr-page-lede">One sentence that says what the user can do here.</p>
    </header>

    @if (_notice is not null)
    {
        <div class="alert alert-warning" role="alert">@_notice</div>
    }

    <section class="rr-card rr-section rr-reveal rr-reveal-1">
        <h2>Section title</h2>
        @* content *@
    </section>

    @if (_items is null)
    {
        <div class="rr-card" aria-busy="true"><span class="rr-skeleton" style="width:100%;height:1rem;"></span></div>
    }
    else if (_items.Count == 0)
    {
        <div class="rr-empty">
            <AppIcon Name="inbox" Size="28" />
            <strong>Nothing here yet</strong>
            <p>Explain the next action.</p>
        </div>
    }
    else
    {
        <div class="rr-table-wrap"><table class="table">…</table></div>
    }
</div>
```

Then: add the `NavMenu` link, run through §8, update PROJECT-CONTEXT.md.
