# RapidRelief — Website Theme & Design System

> **Status:** current. Replaces the Forest Green identity used until 2026-09-04.
> The previous palette (`#1E7A5A` primary, "no blue anywhere") is **retired** —
> do not reintroduce it. Legacy aliases such as `--rr-forest` and `--rr-mint`
> still exist in the stylesheets but resolve to the tokens defined here.

---

## 1. Design principle

**Usability beats visual decoration.**

People reach RapidRelief while stressed, on a phone, often on a poor connection.
Every decision below is downstream of that. Where beauty and legibility conflict,
legibility wins.

Three rules the whole system obeys:

1. **Colour communicates meaning.** A control is not coloured to look nice. Red
   means emergency, amber means attention, emerald means resolved, cyan means
   "this is the action". Everything else is neutral.
2. **No raw values in markup.** Every size, colour, radius, duration and z-index
   comes from a token. If a value is needed and no token exists, add the token.
3. **One vocabulary per concept.** One button family, one focus ring, one
   severity chip, one timeline. A pattern that exists twice is a defect.

### Personality

Calm, trustworthy, human-centred, actionable, reliable under pressure.

It should **not** feel like a gaming/cyberpunk console, a crypto product, or an
"excessively colourful emergency app". Restraint is the point: the interface is
quiet so that an emergency can be loud.

---

## 2. Colour

### 2.1 Roles

| Role | Meaning | Light | Dark |
| --- | --- | --- | --- |
| Primary | Brand, headings, primary button fill | `#0f172a` deep navy | `#e8edf5` |
| Accent | The action, links, focus | `#0e7490` cyan | `#22d3ee` |
| Emergency | SOS and critical only | `#dc2626` | `#dc2626` |
| Warning | Needs attention, degraded | `#b45309` | `#b45309` |
| Success | Delivered, resolved, safe | `#059669` | `#059669` |
| Surface | Page and card backgrounds | `--n-0` / `--n-50` | `#0b1120` / `#111a2b` |
| Text | Body, secondary, tertiary | `--rr-text-1/2/3` | idem |

Cyan is deliberately darker in light mode (`#0e7490`) than in dark mode
(`#22d3ee`): the bright cyan does not reach 3:1 against a white background, so
the focus ring would have failed WCAG 2.2 non-text contrast.

### 2.2 Neutral ramp

`--n-0` `#ffffff` through `--n-950` `#060b16`, cool-tinted slate. The neutrals
carry almost all of the interface. If a screen looks grey, that is correct.

### 2.3 On-colour tokens (mandatory)

Every tinted surface has a matching foreground token:

```
--rr-danger-soft / --rr-danger-soft-text / --rr-danger-soft-border
--rr-warning-soft / ...-text / ...-border
--rr-success-soft / ...-text / ...-border
--rr-accent-soft  / ...-text / ...-border
--rr-on-danger  --rr-on-warning  --rr-on-success  --rr-on-primary
```

**Never** put `#ffffff` on a tinted background. In dark mode the tints are light,
and white text on them measured between 1.4:1 and 2.8:1 - unreadable. Pair the
tint with its `-text`/`on-` token instead.

### 2.4 Colour budget

A citizen screen should show at most **one** saturated element: the SOS control.
An operator screen should show colour only in severity chips and status dots.

---

## 3. Typography

```
--rr-font: 'Inter', 'Aptos', 'Segoe UI Variable Text', 'Lexend',
           'Segoe UI', system-ui, -apple-system, sans-serif;
```

Lexend is vendored locally and stays last in the stack as the guaranteed offline
baseline - the CSP forbids CDN fonts and the app must work offline.

| Token | Use |
| --- | --- |
| `--text-2xs` ... `--text-3xl` | The only permitted font sizes |
| `--weight-regular/medium/semibold/bold` | The only permitted weights |
| `--leading-tight/snug/normal/relaxed` | Line height |
| `--tracking-tight/normal/wide/caps` | Letter spacing |

Headings step down without skipping. Numbers that are compared vertically
(distance, wait time, capacity) use `font-variant-numeric: tabular-nums`.

---

## 4. Space, radius, elevation, motion

| Scale | Tokens |
| --- | --- |
| Spacing | `--space-1` (0.25rem) ... `--space-16` (4rem) |
| Radius | `--rr-radius-s`, `-l`, `-xl`, `--rr-radius-pill` |
| Z-index | `--z-base` through `--z-critical` (never a bare number) |
| Motion | `--rr-fast 120ms`, `--rr-base 200ms`, `--rr-slow 320ms`, `--rr-ease*` |
| Breakpoints | `--bp-sm 480` `--bp-md 768` `--bp-lg 1024` `--bp-xl 1280` |

Motion is functional: state changes, not entrances. Everything animated is
disabled under `prefers-reduced-motion: reduce`.

---

## 5. Component layer

`wwwroot/css/components.css` loads **after** every legacy sheet and is the single
definition of shared behaviour.

| Component | Notes |
| --- | --- |
| `.rr-btn` + `-primary/-secondary/-outline/-ghost/-danger/-link` | The only button family. `.btn-dash-*`, `.btn-auth-primary`, `.btn-google-auth` are aliases onto it. Minimum height 44px. Active state uses `filter: brightness()`, never `transform` (a jumping button is a mis-tap). |
| `:focus-visible` | Exactly one ring, defined once, 2px `--rr-accent-bright` plus offset. |
| `.rr-sos` | The emergency control. See section 6. |
| `.rr-timeline` | Vertical progress rail. Used by the citizen dashboard and My reports. |
| `.rr-sev-*` | Severity: SOS triangle, Critical triangle, High diamond, Medium circle, Low bar. Shape **and** colour, so it survives greyscale and colour blindness. |
| `.rr-panel`, `.rr-metric`, `.rr-statstrip` | Framing and figures. |
| `.rr-table`, `.rr-queue-row`, `.rr-facts`, `.rr-filter` | Dense operational surfaces. |
| `.rr-drawer` | Detail panel; becomes a bottom sheet at 640px and below. |
| `.rr-ai-*` | Attributed AI output. See section 7. |

---

## 6. The SOS control

SOS is the single most important control in the product, and the single most
dangerous to fire by accident.

- **Visible:** largest target on the citizen home, full width, `--rr-danger`.
- **Hard to trigger accidentally:** two steps. Tapping SOS *arms* it; a second,
  separate CONFIRM is required.
- **Anti-misfire:** when armed, **Cancel occupies the coordinates the finger just
  tapped** and CONFIRM appears below it, disabled for 700ms. A double-tap
  therefore cancels; it can never confirm. The armed state auto-disarms after 12s.
- **Fast after confirmation:** one request, optimistic UI, offline-queued.
- **Receipt:** on success the citizen sees Incident ID, time, location, current
  status and rescue status - never a bare toast.

---

## 7. AI presentation

AI is decision *support*. It is never presented as authority.

- Labelled "AI - decision support" in the panel header.
- Confidence is shown as a number **and** a meter.
- The provider is named ("model X", "offline rule engine").
- A **Because** list gives the factors and the evidence behind the score.
- The verdict is worded "Suggested priority ...", not "Priority is ...".
- The standing disclaimer sits in the panel footer, always rendered.
- When there is no assessment, the panel says so rather than disappearing.

---

## 8. Role surfaces

**Citizen (`/c`)** - ordered by need, not by novelty:

1. Emergency / SOS
2. Report incident
3. Active emergency
4. Shelter
5. Relief
6. Notifications

**Rescue (`/r`)** - an operational interface. Severity, distance, waiting time,
status and location are the only facts on a queue row, in that order. Filters are
chips, not coloured tiles. Colour appears only on severity and on a call that has
been waiting too long.

**Government (`/g`)** - a command centre. The **situation map is a primary
information surface** on the overview, above the decision tables: the map answers
*where*, the tables answer *which one*. Navigation is one shared `CommandTabs`
component with `aria-current="page"`.

---

## 9. Accessibility contract

- Contrast: at least 4.5:1 body text, at least 3:1 large text, UI borders and
  focus rings.
- Touch targets at least 44x44 CSS px.
- Every interactive element is reachable and visible on keyboard focus.
- Dialogs trap focus, autofocus a sensible control, lock background scroll and
  restore focus on close (`wwwroot/js/dialog.js` plus `RrModal`).
- Status is never colour-only: severity has a shape, connectivity has a word.
- `prefers-reduced-motion` removes all animation.

---

## 10. Adding to the system

1. Look for an existing token or component first.
2. If a token is missing, add it to `app.css` section 2 in **both** themes.
3. If a component is missing, add it to `components.css`, not to a page sheet.
4. Never add a second way to do something that already exists.
5. Check contrast in both themes before shipping.
